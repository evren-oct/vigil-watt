namespace VigilWatt.Core.Plugins.Mchose;

/// <summary>回报率发布判定；只有 <see cref="Publish"/> 允许写 <c>ReportRateSettingHz</c>。</summary>
public enum MchoseRateDecision
{
    /// <summary>可以发布：两个 index 一致、型号查得到表、index 在表内。</summary>
    Publish = 0,

    /// <summary>两个 index 不一致：当前语义不足以确定设置值，保持 Unknown。**这不是协议错误**。</summary>
    IndicesDiffer,

    /// <summary>型号 / USB 枚举 PID 在目录里查不到对应的表。</summary>
    NoTable,

    /// <summary>index 超出该表档位数（1K 只有 3 档）。</summary>
    IndexOutOfRange,
}

/// <summary>
/// 功 能： 回报率"该不该发布"的唯一判定点（纯函数，可单测）。
/// 2026/11/09 19:40:00 xliu
/// 三条都是"不猜"：不一致不猜、查不到表不猜、越界不猜。规则见 docs/hardware/brands/mchose.md §3.4.2。
/// </summary>
public static class MchoseRatePolicy
{
    /// <summary>
    /// 判定并给出 Hz 与表名。两个 index 必须一致才发布——不一致说明我们还不知道哪套在生效。
    /// </summary>
    public static MchoseRateDecision Decide(ushort modelPid, ushort usbPid,
        byte usbIndex, byte gRateIndex, out int hz, out string tableName)
    {
        hz = 0;
        tableName = "";

        if (usbIndex != gRateIndex) return MchoseRateDecision.IndicesDiffer;
        if (!MchoseDeviceCatalog.TryGetRateTable(modelPid, usbPid, out tableName, out var table))
            return MchoseRateDecision.NoTable;
        if (usbIndex >= table.Count) return MchoseRateDecision.IndexOutOfRange;

        hz = table[usbIndex];
        return MchoseRateDecision.Publish;
    }
}
