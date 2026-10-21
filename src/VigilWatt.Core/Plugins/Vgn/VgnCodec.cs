using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.Vgn;

/// <summary>
/// 功 能： VGN（蜻蜓 / Dragonfly 系列）鼠标的私有协议**纯帧编解码**。
/// 2026/10/19 新增
///
/// 协议来自官方网页驱动 hub.vgnlab.com.cn 的前端 bundle（设备表里
/// <c>{vendorId:14621, productId:6691/4134, usagePage:65473, usage:1, customDeviceType:"vgn-kc2"}</c>，
/// 控制器 <c>serveProvider:"kcMs2"</c>，<c>reportId=179(0xB3)</c>），并在本机
/// <c>391D:1026</c>（有线）/ <c>391D:1A23</c>（2.4G dongle）真机验证。
///
/// 帧结构（WebHID 数据层 63 字节，无校验字节——官方驱动 <c>sendHidBuffer</c> 直接 sendReport）：
/// <code>
///   [0]=命令(0x06)  [1..62]=0
/// </code>
/// Windows 的 WriteFile 缓冲首字节是 report ID，故缓冲 64 字节、数据层整体右移一位：
/// <code>
///   buf[0]=0xB3  buf[1]=0x06  ...
/// </code>
///
/// 应答是"配置帧"（命令回显 <c>payload[0]==0x06</c>），电量不在独立命令里，而是打包在其中：
/// <code>
///   payload[19]  bit0-6 = 电量 %   bit7 = 充电中
/// </code>
/// 真机样本（有线，插线充电中）：
/// <code>
///   B4 06 00 20 20 60 90 01 20 03 40 06 80 0C 00 19 01 06 00 05 A4 50 C3 ...
///   │  └ 命令回显                              └ payload[19]=0xA4 → 36% 且 bit7=1 充电
///   └ 应答 report ID (0xB4)
/// </code>
/// 注：这一帧是"设备配置"整帧（真机里回报率、灯效、<c>payload[20..21]=50 C3</c>
/// 这类数值都在），只有 <c>payload[19]</c> 的语义能从官方驱动源码直接对上，因此只取它。
/// </summary>
internal static class VgnCodec
{
    /// <summary>VGN 的 USB VID（设备表里的 14621）。</summary>
    public const ushort Vid = 0x391D;

    /// <summary>蜻蜓 F1 V5 Turbo+ 的 2.4G dongle PID（设备表 6691）。</summary>
    public const ushort PidWireless = 0x1A23;

    /// <summary>同一只鼠标插线后的有线 PID（设备表 4134，插拔实测确认）。</summary>
    public const ushort PidWired = 0x1026;

    /// <summary>厂商配置通道的 usagePage（65473）。</summary>
    public const ushort VendorUsagePage = 0xFFC1;

    /// <summary>厂商配置通道的 usage。</summary>
    public const ushort VendorUsage = 0x0001;

    /// <summary>官方驱动 <c>kcMs2</c> 的 report ID。</summary>
    public const byte ReportId = 0xB3;

    /// <summary>备用 report ID（同一只鼠标的另一个可写 ID，真机实测同样可用）。</summary>
    public const byte ReportIdAlt = 0xB5;

    /// <summary>读设备配置（电量就在这个应答里）。</summary>
    public const byte CmdConfig = 0x06;

    /// <summary>连接状态（无线模式下先发它判断鼠标是否在睡）。</summary>
    public const byte CmdConnectionStatus = 0x03;

    /// <summary>Windows 侧收发缓冲长度（该接口 in=64 / out=64）。</summary>
    public const int FrameLength = 64;

    /// <summary>电量字节在 payload 中的下标。</summary>
    public const int BatteryIndex = 19;

    /// <summary>payload 中"电量 + 充电位"那个字节里，充电标志的掩码。</summary>
    public const byte ChargingBit = 0x80;

    /// <summary>这一帧至少要有这么多非零字节才认，用来拒绝"全零空缓冲"。</summary>
    private const int MinNonZero = 3;

    /// <summary>
    /// 设备匹配规则（纯数据，方便单测）：VID + 两个 PID + 厂商 usage + 64/64。
    /// 同一 VID 下还有 <c>0x008C/0x0001</c> 的 33 字节接口，长度校验能把它排除。
    /// </summary>
    public static bool IsMatch(ushort vid, ushort pid, ushort usagePage, ushort usage,
        int inputLength, int outputLength)
    {
        if (vid != Vid) return false;
        if (pid != PidWireless && pid != PidWired) return false;
        if (usagePage != VendorUsagePage || usage != VendorUsage) return false;
        return inputLength >= FrameLength && outputLength >= FrameLength;
    }

    /// <summary>构造一条 64 字节的配置查询帧（电量查询就是它）。</summary>
    public static byte[] BuildConfigRequest(byte reportId = ReportId)
    {
        var frame = new byte[FrameLength];
        frame[0] = reportId;
        frame[1] = CmdConfig;
        return frame;
    }

    /// <summary>构造连接状态查询帧（无线模式下用来判断鼠标是否在睡）。</summary>
    public static byte[] BuildStatusRequest(byte reportId = ReportId)
    {
        var frame = new byte[FrameLength];
        frame[0] = reportId;
        frame[1] = CmdConnectionStatus;
        return frame;
    }

    /// <summary>
    /// 解析配置应答里的电量。
    /// <paramref name="frame"/> 允许"带 report ID"（64 字节，首字节是 0xB4 之类的应答 ID）
    /// 或"纯数据层"（63 字节）两种形态：命令回显在 payload[0]，
    /// 所以只要找到回显位置 <c>start</c>，电量就在 <c>start + 19</c>。
    /// </summary>
    public static bool TryParseBattery(byte[] frame, out int percent, out ChargingState charging)
    {
        percent = 0;
        charging = ChargingState.Unknown;
        if (frame == null) return false;

        foreach (int start in new[] { 1, 0 })
        {
            if (frame.Length < start + BatteryIndex + 1) continue;
            if (frame[start] != CmdConfig) continue;

            // 结构校验：正常配置帧里 DPI / 回报率 / 灯光一堆字段都是非零，
            // 全零缓冲（读失败返回的空数组形态）会在这里被挡掉。
            int nonZero = 0;
            for (int i = 1; i <= 40 && start + i < frame.Length; i++)
                if (frame[start + i] != 0) nonZero++;
            if (nonZero < MinNonZero) continue;

            byte packed = frame[start + BatteryIndex];
            int pct = packed & 0x7F;
            bool isCharging = (packed & ChargingBit) != 0;

            // 0xE4 是"该命令不支持"的错误回显，落不到这里；>100 只可能是错位
            if (pct > 100) continue;

            percent = pct;
            charging = isCharging
                ? (pct >= 100 ? ChargingState.Full : ChargingState.Charging)
                : ChargingState.Discharging;
            return true;
        }
        return false;
    }

    /// <summary>
    /// 解析连接状态应答（payload[2] == 1 表示鼠标在线）。
    /// 只在无线模式下有意义：鼠标深睡时配置帧完全没回包，先探一下能少发一次无效请求。
    /// </summary>
    public static bool TryParseOnline(byte[] frame, out bool online)
    {
        online = false;
        if (frame == null) return false;

        foreach (int start in new[] { 1, 0 })
        {
            if (frame.Length < start + 3) continue;
            if (frame[start] != CmdConnectionStatus) continue;
            online = frame[start + 2] == 1;
            return true;
        }
        return false;
    }
}
