namespace VigilWatt.Core.Plugins.Ajazz;

/// <summary>
/// 功 能： 解析黑爵 AK870 的 ROYUAN 接收器电量状态。
/// 2026/10/18 14:45:00 KNA7022
/// 协议来源和实机帧见 docs/hardware/devices/ak870-ml903.md。
/// </summary>
public static class AjazzCodec
{
    public static byte[] CreateStatusQuery()
    {
        var payload = new byte[64];
        payload[0] = 0xF7;
        payload[7] = 0x08;
        return payload;
    }

    public static bool TryParse(ReadOnlySpan<byte> report, out int percent, out bool online)
    {
        percent = 0;
        online = false;
        if (report.Length is not (64 or 65) || report[0] != 0 || report[1] > 1
            || report[2] > 100 || report[4] > 1 || report[5] > 1 || report[6] > 1
            || report[7] is not (1 or 3)) return false;
        percent = report[2];
        online = report[4] == 0;
        return true;
    }
}
