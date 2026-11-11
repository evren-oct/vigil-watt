using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Mchose;

/// <summary>
/// 功 能： 迈从 A5 V2 Ultra 鼠标插件（反码 Feature `0x11` 查询），与键鼠插件共用一个品牌开关。
/// 2026/10/19 20:38:41 kaniu
/// 帧与真机样本见 docs/hardware/brands/mchose.md §6；会写帧，故默认关闭。
/// </summary>
public sealed class MchoseA5Plugin : BatteryPluginBase
{
    /// <summary>与 <see cref="MchosePlugin"/> 共用品牌开关：设置页按 Name 去重，迈从只出一行（AGENTS.md §9）。</summary>
    public override string Name => "Mchose";

    /// <summary>认领身份必须与主插件分开，否则两个插件对同一接口的互斥与释放会互相踩（CR-145）。</summary>
    protected override string ClaimId => "Mchose#A5";

    public override string DisplayName => "迈从";
    public override string DisplayNameEn => "MCHOSE";
    public override DeviceKind Kind => DeviceKind.Mouse;

    /// <summary>A5 接收器只发一条 Feature 查询，60 秒冷却——不跟着键盘的 15 秒发现节拍增加写入。</summary>
    protected override TimeSpan ProbeCooldown => TimeSpan.FromSeconds(60);

    /// <summary>鼠标休眠时 Feature 读回是命令回显；null 由插件自己报账，不交给基类猜。</summary>
    protected override OfflinePolicy Offline { get; } = new()
    {
        Silence = TimeSpan.Zero,
        CountPollFailures = false,
    };

    /// <summary>协议本身会给出真实的 0%（反码 `0xFF`），不能按「0/1 是占位」拒掉。</summary>
    protected override int MinValidPercent(HidDeviceInfo info) => 0;

    protected override bool IsCandidate(HidDeviceInfo info) => MchoseMouseProtocol.IsCandidate(info);

    protected override async Task<(string Protocol, int? Percent, ChargingState Charging)?> ProbeAsync(
        HidDeviceHandle handle)
    {
        var read = await MchoseMouseProtocol.QueryAsync(handle, WritesEnabled).ConfigureAwait(false);
        return read is { } value
            ? (MchoseMouseProtocol.Name, value.Percent, value.Charging)
            : null;
    }

    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
        => ProbeAsync(handle).GetAwaiter().GetResult();

    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev)
    {
        if (!CanWrite(dev)) return null;

        var read = MchoseMouseProtocol.QueryAsync(dev.Handle, WritesEnabled).GetAwaiter().GetResult();
        if (read.HasValue)
        {
            ReportPollAnswered(dev);
            return (read.Value.Percent, read.Value.Charging);
        }

        ReportPollFailure(dev);
        Log.Debug($"迈从 A5 查询无应答: {dev.Info.DevicePath}");
        return null;
    }

    /// <summary>产品名是泛化的「MCHOSE 2.4G Wireless」，型号只能写死。</summary>
    protected override string BuildDisplayName(HidDeviceInfo info) => "迈从 A5 V2 Ultra";
}
