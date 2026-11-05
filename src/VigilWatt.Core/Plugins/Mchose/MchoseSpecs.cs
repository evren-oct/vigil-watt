using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Protocol;

namespace VigilWatt.Core.Plugins.Mchose;

/// <summary>
/// 功 能： 迈从协议规格的唯一入口——解释器与目录只加载一次，插件与 codec 薄壳共用。
/// 2026/11/01 23:40:00 xliu
/// 规格在 specs/mchose.protocols.json，写法见 docs/dev/protocol-json.md。
/// </summary>
internal static class MchoseSpecs
{
    public const string ProtocolsResource = "VigilWatt.Core.Plugins.Mchose.specs.mchose.protocols.json";
    public const string DevicesResource = "VigilWatt.Core.Plugins.Mchose.mchose-catalog.json";

    /// <summary>全插件共用一个实例：去抖状态按设备路径隔离，不需要按插件分家。</summary>
    public static readonly ProtocolEngine Engine = ProtocolEngine.Load(ProtocolsResource, DevicesResource);

    /// <summary>取一套协议；名字拼错会在第一次用到时炸，而不是静默不认领。</summary>
    public static ProtocolSpec Get(string protocol)
        => Engine.SpecOf(protocol)
           ?? throw new InvalidOperationException($"mchose.protocols.json 里没有协议 \"{protocol}\"");

    /// <summary>满电补判：100% 且设备明确报未充电才判 Full（官方 resolveMCChargeStatus 同口径），充电中与未知态都不猜。</summary>
    public static (int? Percent, ChargingState Charging) ResolveFull(int? percent, ChargingState charging)
        => percent >= 100 && charging == ChargingState.Discharging
            ? (percent, ChargingState.Full)
            : (percent, charging);
}
