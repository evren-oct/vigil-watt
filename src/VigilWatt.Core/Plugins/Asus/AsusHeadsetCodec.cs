using VigilWatt.Core.Hid;

namespace VigilWatt.Core.Plugins.Asus;

/// <summary>
/// 功 能： 华硕 ROG 无线耳机 0xCC 帧的构造与解析（group 0x12 读，数据从 [5] 起）。
/// 2026/10/24 01:30:00 Leejangho
/// 帧与字段见 docs/hardware/asus-rog.md 耳机一节；本机 ROG DELTA II 实测。
/// </summary>
public static class AsusHeadsetCodec
{
    public const byte ReportId = 0xCC;
    public const int ReportLength = 64;
    public const byte GroupRead = 0x12;

    /// <summary>本协议的协议名，同时用于 ResolveKind 区分耳机与键盘。</summary>
    public const string Protocol = "rog-0xCC";

    /// <summary>读电源：data[0]=睡眠分钟 data[1]=电量 data[2]=低电提醒值 data[3]=语音开关。</summary>
    public const byte CmdPower = 0x07;

    /// <summary>读充电状态：data[0] == 1 表示正在充电。</summary>
    public const byte CmdCharging = 0x08;

    /// <summary>0xFE 是"电量未就绪"占位值，不是百分比（与 Pelta 逆向文档一致）。</summary>
    private const byte BatteryUnavailable = 0xFE;

    /// <summary>
    /// 只认已证实过协议的型号。少滤可能碰到别家硬件，加型号必须有真机证据（safety.md）。
    /// </summary>
    private static readonly ushort[] KnownProductIds = [0x1AFA];

    public static bool IsHeadsetInterface(HidDeviceInfo info) =>
        info.VendorId == 0x0B05
        && Array.IndexOf(KnownProductIds, info.ProductId) >= 0
        && info.UsagePage == 0xFF00 && info.Usage == 0x0001
        && info.InputReportLength == ReportLength && info.OutputReportLength == ReportLength
        && info.OutputReportId == ReportId;

    public static byte[] BuildRead(byte command, byte sub1 = 0, byte sub2 = 0)
    {
        var frame = new byte[ReportLength];
        frame[0] = ReportId;
        frame[1] = GroupRead;
        frame[2] = command;
        frame[3] = sub1;
        frame[4] = sub2;
        return frame;
    }

    /// <summary>应答必须回显 group + command，否则是上一条命令的迟到帧。</summary>
    public static bool TryParse(byte[] input, byte command, out byte[] data)
    {
        data = Array.Empty<byte>();
        if (input.Length < 6) return false;
        if (input[0] != ReportId || input[1] != GroupRead || input[2] != command) return false;
        data = input[5..];
        return true;
    }

    public static bool TryParsePower(byte[] data, out int percent, out int lowLevel, out bool lowVoice)
    {
        percent = 0;
        lowLevel = 0;
        lowVoice = false;
        if (data.Length < 4) return false;

        int value = data[1];
        if (value == BatteryUnavailable || value > 100) return false;

        percent = value;
        lowLevel = data[2];
        lowVoice = data[3] != 0;
        return true;
    }

    public static bool TryParseCharging(byte[] data, out bool charging)
    {
        charging = false;
        if (data.Length < 1) return false;
        charging = data[0] == 1;
        return true;
    }
}
