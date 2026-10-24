using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.Mchose;

/// <summary>
/// 功 能： X9 耳机固定电量查询的报文构造与解析。
/// 2026/10/20 10:20:00 MOSS
/// 协议与验证范围见 docs/hardware/mchose-x9.md。
/// </summary>
public static class MchoseX9Codec
{
    /// <summary>X9 接收器的厂 ID。**与键鼠同号段**（分类器里必须排在鼠标分支之前）。</summary>
    public const ushort VendorId = 0x3837;

    /// <summary>X9 接收器的产品 ID。</summary>
    public const ushort ProductId = 0x6045;

    /// <summary>输入/输出报告长度，**已含 report ID，不加前置零**。</summary>
    public const int ReportLength = 64;

    /// <summary>查询与响应共用的 report ID。</summary>
    public const byte ReportId = 0x55;

    /// <summary>响应里的命令回显字节（与查询帧 `[1]` 相同）。</summary>
    private const byte CommandEcho = 0x65;

    /// <summary>响应里的状态字节偏移。</summary>
    private const int StatusOffset = 3;

    /// <summary>状态字节：放电。充电语义来自官方 M HUB 前端，见 mchose-x9.md §3。</summary>
    private const byte StatusDischarging = 0x02;

    /// <summary>状态字节：充电中。</summary>
    private const byte StatusCharging = 0x03;

    /// <summary>状态字节：已充满。</summary>
    private const byte StatusFull = 0x04;

    /// <summary>状态字节：休眠。此时电量仍有效，但与"真的低电量"难以区分，一律采信。</summary>
    private const byte StatusSleep = 0x1A;

    /// <summary>查询帧的固定形态，供 <see cref="TryParse"/> 识别回显。</summary>
    private static readonly byte[] Query = BuildQuery();

    /// <summary>
    /// 电量集合的接口判据。同接收器上还有 0xFF82 的 64 字节集合（col06），一律不认；
    /// 它落不到这里，但也不能放宽成"只看长度"。
    /// </summary>
    public static bool IsSupportedInterface(ushort vendorId, ushort productId, string path,
        ushort usagePage, ushort usage, ushort inputLength, ushort outputLength,
        ushort featureLength, byte? reportId)
        => vendorId == VendorId && productId == ProductId
           && path.Contains("&mi_00&col05#", StringComparison.OrdinalIgnoreCase)
           && usagePage == 0xFF90 && usage == 0x0001
           && inputLength == ReportLength && outputLength == ReportLength
           && featureLength == 0 && reportId == ReportId;

    /// <summary>64 字节查询帧：`55 65 01`，余下 61 字节为 0。</summary>
    public static byte[] CreateQuery() => (byte[])Query.Clone();

    /// <summary>
    /// 解析一条 input report，成功返回电量（1–100）与充电状态。
    /// 0 与 &gt;100 判无效：耳机休眠/关机时接收器会回 0，与"真的 0%"无法区分。
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> report, out int percent, out ChargingState charging)
    {
        percent = 0;
        charging = ChargingState.Unknown;
        if (report.Length != ReportLength || report[0] != ReportId
            || report[1] != CommandEcho || report[2] is 0 or > 100)
            return false;

        // 有的接收器会把主机写入回显到输入队列：查询帧（55 65 01 …）正好能解析成 1%，整帧相等时丢弃
        if (report.SequenceEqual(Query)) return false;

        percent = report[2];
        charging = report.Length > StatusOffset ? MapCharging(report[StatusOffset]) : ChargingState.Unknown;
        return true;
    }

    /// <summary>状态字节 → 充电状态。未收录的取值一律 Unknown，不猜。</summary>
    public static ChargingState MapCharging(byte status) => status switch
    {
        StatusDischarging => ChargingState.Discharging,
        StatusCharging => ChargingState.Charging,
        StatusFull => ChargingState.Full,
        StatusSleep => ChargingState.Discharging,
        _ => ChargingState.Unknown,
    };

    private static byte[] BuildQuery()
    {
        var report = new byte[ReportLength];
        report[0] = ReportId;
        report[1] = CommandEcho;
        report[2] = 0x01;
        return report;
    }
}
