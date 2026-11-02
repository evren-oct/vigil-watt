using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Protocol;

namespace VigilWatt.Core.Plugins.Mchose;

/// <summary>
/// 功 能： 迈从 Gmouse 主动查询协议（G3 V2）的薄壳——帧与偏移在 specs/mchose.protocols.json，这里只保留接口判据。
/// 2026/10/20 Sunshine
/// 保留 <c>CreateQuery</c>/<c>TryParse</c> 是为了让既有断言继续钉住官方帧，实现已改为调用解释器。见 docs/dev/protocol-json.md。
/// </summary>
public static class MchoseGmouseCodec
{
    /// <summary>厂 ID（迈从新世代）。</summary>
    public const ushort VendorId = 0x3837;

    /// <summary>G3 V2 接收器 PID。</summary>
    public const ushort PidG3V2 = 0x4245;

    /// <summary>命令通道的 usagePage / usage（USB 路径里是 <c>mi_02</c>，无 COL 后缀）。</summary>
    public const ushort UsagePage = 0xFF01;
    public const ushort Usage = 0x0010;

    /// <summary>帧总长（65 = 64 数据 + 1 report ID）。</summary>
    public const int FrameLength = 65;

    private static ProtocolSpec Spec => MchoseSpecs.Get("gmouse");

    /// <summary>接口是不是 Gmouse 命令通道：完整指纹（VID+PID+厂商用法+双向 65 字节），判错会把帧发到别的设备上。</summary>
    public static bool IsSupportedInterface(ushort vendorId, ushort productId,
        ushort usagePage, ushort usage, ushort inputLength, ushort outputLength)
        => vendorId == VendorId
           && productId == PidG3V2
           && usagePage == UsagePage
           && usage == Usage
           && inputLength >= FrameLength
           && outputLength >= FrameLength;

    /// <summary>电量查询帧（65 字节），来自 JSON 的 <c>active.query</c>。</summary>
    public static byte[] CreateQuery() => (byte[])Spec.Active!.Query.Clone();

    /// <summary>解析电量应答：前缀 <c>AA 30</c>，电量与充电位由 JSON 指定。</summary>
    public static bool TryParse(byte[] report, out int percent, out bool charging)
    {
        percent = 0;
        charging = false;
        if (FrameReader.Read(Spec.Active!.Response, report) is not { Percent: { } value } reading) return false;
        percent = value;
        // 满电也报 true：调用方（旧口径）只关心"是否在充电/已充满"，具体状态由解释器给
        charging = reading.Charging is ChargingState.Charging or ChargingState.Full;
        return true;
    }
}
