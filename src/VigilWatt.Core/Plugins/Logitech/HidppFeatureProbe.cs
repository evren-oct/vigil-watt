namespace VigilWatt.Core.Plugins.Logitech;

/// <summary>
/// 功 能： feature 下标探测的重试口径：解析回 0 先留 null 下轮再试，连续 MaxMisses 次才认定"不支持"。
/// 2026/11/16 13:50:00 xliu
/// 发现期一次超时直接写 0 会把该电量路径永久禁用，直到接收器重插（CR-165）。
/// </summary>
internal static class HidppFeatureProbe
{
    /// <summary>连续这么多次解析回 0 才认定设备不支持该 feature。</summary>
    public const int MaxMisses = 3;

    /// <summary>
    /// 下一次该缓存的值：&gt;0 = 下标、0 = 确认不支持、null = 还没确认（下轮再试）。
    /// 已缓存的值原样返回，不再计数。
    /// </summary>
    public static byte? Next(byte? cached, byte resolvedIndex, ref int misses)
    {
        if (cached is { } index) return index;

        if (resolvedIndex != 0)
        {
            misses = 0;
            return resolvedIndex;
        }

        misses++;
        return misses >= MaxMisses ? (byte)0 : null;
    }
}
