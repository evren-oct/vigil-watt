using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.Razer;

/// <summary>
/// 功 能： 雷蛇 2.4G 鼠标电量协议（OpenRazer 逆向成果的 C# 实现）。
/// 2026/10/24 23:45:00 imx1n
///
/// 帧形态：90 字节 vendor feature report，report ID 0x00；hid.dll 缓冲多 1 字节（共 91）。
/// 偏移、校验口径（XOR，不是 CRC-8）与缩放规则见 docs/hardware/brands/razer.md。纯函数，便于单测。
/// </summary>
public static class RazerCodec
{
    public const ushort RazerVid = 0x1532;

    public const byte ReportId = 0x00;

    /// <summary>厂商 feature 报文净长（不含 report ID）。</summary>
    public const int PayloadLength = 90;

    /// <summary>HidD_SetFeature / HidD_GetFeature 需要的缓冲长度（含 report ID）。</summary>
    public const int BufferLength = PayloadLength + 1;

    public const byte CommandClassBattery = 0x07;
    public const byte CommandGetBattery = 0x80;
    public const byte CommandGetCharging = 0x84;

    private const byte StatusSuccess = 0x02;
    private const byte StatusBusy = 0x01;
    private const byte StatusNoResponse = 0x04;

    /// <summary>电量缩放口径。raw 是设备回报的原始字节，不同世代含义不同。</summary>
    public enum BatteryScale
    {
        /// <summary>启发式：≤100 视为直读百分比，&gt;100 按 0..255 缩放。</summary>
        Auto,
        /// <summary>0..255 线性映射到 0..100（Viper V3 HyperSpeed 真机确认：raw 161 = 63%）。</summary>
        Scaled255,
        /// <summary>直接就是百分比。</summary>
        Direct100,
    }

    private readonly record struct Profile(BatteryScale Scale, byte TransactionId);

    /// <summary>
    /// 已适配的 PID。注意雷蛇把「产品 PID」和「接收器 PID」分开：
    /// 毒蝰 V4 Pro 本体是 <c>0x00E5</c>，接收器是 <c>0x00E6</c>（Web 端配置表里的 dongleId），
    /// **无线使用时枚举出来的是接收器 PID**。两者都收，靠下面的 91 字节判据保证只认经典协议接口。
    /// 新增型号：在这里加一行 + 在 docs/hardware/brands/razer.md 记验证范围。
    /// </summary>
    private static readonly Dictionary<ushort, Profile> Profiles = new()
    {
        // 毒蝰 Viper V4 Pro：真机验证（2026-10-24，接收器 1532:00E6，接口 3，feature 91 字节）
        //   请求 [00 1F 00 00 00 02 07 80 …85 00] → 应答 status=0x02, args[1]=0xFF → 100%
        [0x00E6] = new(BatteryScale.Scaled255, TransactionId: 0x1F),
        // 同一型号的本体 PID（有线直连时枚举）。协议同代，未单独验证。
        [0x00E5] = new(BatteryScale.Scaled255, TransactionId: 0x1F),
    };

    /// <summary>
    /// 认领判据：VID + 已知 PID + feature 报文正好 91 字节。
    /// **91 字节是关键**：同一台设备还有一个 62 字节的厂商接口（usage 0xFF13），
    /// 经典协议只挂在「消费控制」接口（usage page 0x0C）上，靠长度就能分开，不必猜 usage page。
    /// </summary>
    public static bool IsSupported(ushort vendorId, ushort productId, ushort featureReportLength)
        => vendorId == RazerVid
           && featureReportLength == BufferLength
           && Profiles.ContainsKey(productId);

    /// <summary>
    /// 请求帧。校验字节 88 = payload 字节 2..87 **逐字节异或**（OpenRazer 的 razer_report.calculate_crc
    /// 就是这么算的，名字叫 crc 但实际是 XOR，不是 CRC-8）。不含 status / transaction_id。
    /// 已知向量：tx=0x1F 查电量（class 0x07 / id 0x80）时校验值 = 0x85。
    /// </summary>
    public static byte[] BuildRequest(byte commandId, byte transactionId, byte dataSize = 0x02)
    {
        var p = new byte[PayloadLength];
        p[0] = 0x00;              // status: 新命令
        p[1] = transactionId;
        p[2] = 0x00;              // remaining_packets 高字节
        p[3] = 0x00;              // remaining_packets 低字节
        p[4] = 0x00;              // protocol_type
        p[5] = dataSize;
        p[6] = CommandClassBattery;
        p[7] = commandId;
        p[88] = Checksum(p.AsSpan(2, 86));
        p[89] = 0x00;             // reserved
        return p;
    }

    /// <summary>校验字节：payload 字节 2..87 逐字节异或。</summary>
    public static byte Checksum(ReadOnlySpan<byte> data)
    {
        byte sum = 0;
        foreach (byte b in data) sum ^= b;
        return sum;
    }

    /// <summary>
    /// 解析电量响应。<paramref name="buffer"/> 是 HidD_GetFeature 的原始缓冲：
    /// [0]=report id，[1]=status，[2]=transaction id，[7]=class，[8]=command，[9..]=arguments。
    /// 成功时 arguments[1]（整包 byte 10）是电量原始值。
    /// </summary>
    public static bool TryParseBattery(byte[]? buffer, out int raw)
    {
        raw = 0;
        if (!IsValidEcho(buffer, CommandGetBattery)) return false;
        raw = buffer![10];
        return true;
    }

    /// <summary>解析充电状态。部分型号（含毒蝰 V4 Pro）不支持 0x84，返回 false 由调用方退回 Unknown。</summary>
    public static bool TryParseCharging(byte[]? buffer, out ChargingState state)
    {
        state = ChargingState.Unknown;
        if (!IsValidEcho(buffer, CommandGetCharging)) return false;
        state = buffer![10] switch
        {
            0x00 => ChargingState.Discharging,
            0x01 => ChargingState.Charging,
            _ => ChargingState.Unknown,
        };
        return true;
    }

    private static bool IsValidEcho(byte[]? buffer, byte commandId)
    {
        if (buffer == null || buffer.Length < BufferLength) return false;
        // 先看 status：忙 / 无响应时，应答头可能还是上一条命令的 echo，先判它会误读旧值。
        if (buffer[1] == StatusBusy || buffer[1] == StatusNoResponse) return false;
        if (buffer[1] != StatusSuccess) return false;
        return buffer[7] == CommandClassBattery && buffer[8] == commandId;
    }

    /// <summary>把原始值换算成百分比，并对越界值做收敛。</summary>
    public static int ToPercent(ushort productId, int raw)
    {
        var scale = Profiles.TryGetValue(productId, out var p) ? p.Scale : BatteryScale.Auto;
        int percent = scale switch
        {
            BatteryScale.Scaled255 => raw * 100 / 255,
            BatteryScale.Direct100 => raw,
            _ => raw <= 100 ? raw : raw * 100 / 255,
        };
        return Math.Clamp(percent, 0, 100);
    }

    /// <summary>
    /// 事务号候选：先用型号表里的值，不应答再试 0xFF（OpenRazer 对未知型号的默认值）。
    /// 插件侧会把命中的那个记下来，后续轮询只发一遍。
    /// </summary>
    public static IReadOnlyList<byte> TransactionCandidates(ushort productId)
    {
        byte preferred = Profiles.TryGetValue(productId, out var p) ? p.TransactionId : (byte)0x1F;
        return preferred == 0xFF ? new[] { (byte)0xFF, (byte)0x1F } : new[] { preferred, (byte)0xFF };
    }
}
