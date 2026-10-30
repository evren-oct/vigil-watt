using System.Text;
using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.Inphic;

/// <summary>
/// 功 能： 英菲克 IN9 的 feature 0x0C 快照帧解析与刷新请求帧（纯逻辑，零 Win32）。
/// 2026/10/29 22:35:11 bitbad
/// 帧布局与"写请求 → 读快照"机制见 docs/hardware/inphic.md §2，改字节偏移前先读那一节。
/// </summary>
internal static class InphicIn9Codec
{
    public const ushort VendorId = 0x248A;

    /// <summary>2.4G 接收器。电量只从它读得到。</summary>
    public const ushort DonglePid = 0x5B2F;

    /// <summary>插充电线后鼠标本体自己的 USB 身份（在不在位 = 是否在充电）。</summary>
    public const ushort WiredPid = 0x5B2E;

    public const byte ReportId = 0x0C;
    public const int ReportLength = 33;
    public const int ModelOffset = 8;
    public const int ModelLength = 5;
    public const int PercentOffset = 18;
    public const int ChargeFlagOffset = 21;

    /// <summary>充电位在帧里的偏移（inphic.md §2.2）：0x01 = 插着充电器。</summary>
    public const int ChargingOffset = 19;

    /// <summary>充电位取值。</summary>
    public const byte ChargingPlugged = 0x01;

    /// <summary>序号在帧里的偏移（inphic.md §2.2）。</summary>
    public const int CommandOffset = 4;

    public const byte FlagDischarging = 0x01;
    public const byte FlagCharging = 0x03;

    public const string Protocol = "inphic-in9-feature0c";

    /// <summary>
    /// 刷新帧载荷区：<c>01 20 00 &lt;序号&gt;</c> + 28 个 0；读取前必须先发，不发只有旧快照或全 0（inphic.md §2.2）。
    /// </summary>
    public static byte[] BuildRefreshPayload(byte command)
    {
        var payload = new byte[ReportLength - 1];
        payload[0] = 0x01;
        payload[1] = 0x20;

        // 帧 [4] 是递增序号，不是回显：重复值会被设备当重复请求丢掉（inphic.md §2.2）。
        payload[CommandOffset - 1] = command;
        return payload;
    }

    /// <summary>序号映射区（1..255、跳过 0）：官方驱动每次启动都从 01 重来，首值必须是 1（inphic.md §2.2）。</summary>
    public static byte CommandFromCounter(uint counter) => (byte)((counter - 1) % 0xFF + 1);

    /// <summary>
    /// 解析一条 feature 0x0C 响应。<b>全 0 必须判无效</b>：有线本体（5B2E）与"发错刷新帧清空了寄存器"
    /// 都会返回全 0，采信就会显示 0%。
    /// </summary>
    public static bool TryParse(byte[]? frame, out int percent, out byte chargeFlag)
    {
        percent = 0;
        chargeFlag = 0;

        // 截断帧（长度不够到标志位）一律丢弃：偏移是按真机 33 字节帧固定的。
        if (frame == null || frame.Length <= ChargeFlagOffset) return false;
        if (frame[0] != ReportId) return false;
        if (IsAllZero(frame)) return false;
        // [18] == 0 是"还没回填"的占位值，不是真的 0%（刷新后几毫秒内就是这个，真机 2026-10-29 复现）。
        // 判成合法会把 0% 写进列表、还会触发低电量提醒，所以这里一律当无效，交给调用方重读。
        if (frame[PercentOffset] is 0 or > 100) return false;

        percent = frame[PercentOffset];
        chargeFlag = frame[ChargeFlagOffset];
        return true;
    }

    /// <summary>充电判据区（inphic.md §2.3）：[19]=0x01 表示插着充电器，此时电量不可信。</summary>
    public static bool IsCharging(byte[]? frame)
        => frame != null && frame.Length > ChargingOffset && frame[ChargingOffset] == ChargingPlugged;

    /// <summary>取型号串（真机为 <c>M9094</c>），只用于日志与显示名排错；解析不出不算失败。</summary>
    public static bool TryParseModel(byte[] frame, out string model)
    {
        model = string.Empty;
        if (frame.Length < ModelOffset + ModelLength) return false;

        var bytes = new byte[ModelLength];
        Array.Copy(frame, ModelOffset, bytes, 0, ModelLength);
        foreach (var b in bytes)
            if (b < 0x20 || b > 0x7E) return false;

        model = Encoding.ASCII.GetString(bytes).Trim();
        return model.Length > 0;
    }

    public static bool IsAllZero(byte[] frame)
    {
        foreach (var b in frame)
            if (b != 0) return false;
        return true;
    }

    /// <summary>协议自报的充放电标志；真机充电态偏移采到过 <c>0x03</c> 但未复现，实际以本体在位判定。</summary>
    public static ChargingState ToChargingState(byte flag) => flag switch
    {
        FlagDischarging => ChargingState.Discharging,
        FlagCharging => ChargingState.Charging,
        _ => ChargingState.Unknown,
    };
}
