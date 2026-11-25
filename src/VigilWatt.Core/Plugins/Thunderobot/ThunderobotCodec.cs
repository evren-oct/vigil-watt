namespace VigilWatt.Core.Plugins.Thunderobot;

/// <summary>
/// 功 能： 解析 ML903 NearLink Se 官方设备信息中的电量字段。
/// 2026/10/18 14:55:00 KNA7022
/// 电量标签取自官方 DLL，字段边界按此型号实机响应核对，详见 docs/hardware/devices/ak870-ml903.md。
/// </summary>
public static class ThunderobotCodec
{
    public static byte[] CreateQuery()
    {
        var report = new byte[64];
        report[0] = 0xA0;
        report[1] = 0x02;
        return report;
    }

    public static bool TryParse(ReadOnlySpan<byte> report, out int percent)
    {
        percent = 0;
        if (report.Length != 64 || report[0] != 0xA1 || report[1] != 0x02) return false;
        bool found = false;
        for (int offset = 1; offset < report.Length;)
        {
            if (report[offset] == 0)
            {
                foreach (byte value in report[offset..]) if (value != 0) return false;
                return found;
            }
            if (offset + 2 > report.Length) return false;
            int tag = report[offset] << 8 | report[offset + 1];
            int length = tag switch { 0x0204 => 2, 0x0211 => 4, 0x0213 => 6, 0x0225 or 0x0226 => 1, _ => 0 };
            if (length == 0 || offset + 2 + length > report.Length) return false;
            if (tag == 0x0225)
            {
                if (found || report[offset + 2] > 100) return false;
                percent = report[offset + 2];
                found = true;
            }
            offset += 2 + length;
        }
        return found;
    }
}
