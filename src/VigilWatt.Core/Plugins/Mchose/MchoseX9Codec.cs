using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Protocol;

namespace VigilWatt.Core.Plugins.Mchose;

/// <summary>
/// 功 能： X9 耳机的薄壳——帧、偏移与充电码映射在 specs/mchose.protocols.json，这里只保留接口判据与回显剔除。
/// 2026/10/20 10:20:00 MOSS
/// 保留 <c>CreateQuery</c>/<c>TryParse</c> 是为了让既有断言继续钉住官方帧，实现已改为调用解释器。
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

    private static ProtocolSpec Spec => MchoseSpecs.Get("headset-x9");

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

    /// <summary>64 字节查询帧：<c>55 65 01</c>，余下 61 字节为 0（来自 JSON 的 <c>active.query</c>）。</summary>
    public static byte[] CreateQuery() => (byte[])Spec.Active!.Query.Clone();

    /// <summary>
    /// 解析一条 input report，成功返回电量（1–100）与充电状态。
    /// 0 与 &gt;100 判无效：耳机休眠/关机时接收器会回 0，与"真的 0%"无法区分。
    /// </summary>
    public static bool TryParse(byte[] report, out int percent, out ChargingState charging)
    {
        percent = 0;
        charging = ChargingState.Unknown;
        // 有的接收器会把主机写入回显到输入队列：查询帧（55 65 01 …）正好能解析成 1%，整帧相等时丢弃
        if (report.AsSpan().SequenceEqual(Spec.Active!.Query)) return false;
        if (FrameReader.Read(Spec.Active!.Response, report) is not { Percent: { } value } reading) return false;
        percent = value;
        charging = reading.Charging;
        return true;
    }
}
