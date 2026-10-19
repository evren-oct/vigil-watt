using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Mchose;

/// <summary>
/// 功 能： 迈从 (MCHOSE) 鼠标插件（RealTek 0xFF01 通道，0B AA 触发 0xE2 状态包）。
/// 2026/10/19 19:50:00 Bamlax
/// 设备示例 3837:100B；帧与字段见 docs/hardware/protocols.md §7。
/// </summary>
public sealed class MchoseMousePlugin : BatteryPluginBase
{
    /// <summary>插件开关的键，与 <c>AppSettings.PluginEnabled</c> 对应（插件开关即写入授权）。</summary>
    private const string SettingsKey = "MchoseMouse";

    private const ushort MchoseVid = 0x3837;
    private const string ProtocolName = "mchose-mouse-v2";

    public override string Name => SettingsKey;
    public override string DisplayName => "迈从鼠标";
    public override DeviceKind Kind => DeviceKind.Mouse;

    protected override bool SupportsPassiveListen => true;

    /// <summary>0xE2 只在连接/配置变化时自发推送，沉默没有信息量；失联靠主动查询失败计数。</summary>
    protected override OfflinePolicy Offline { get; } = new()
    {
        Silence = TimeSpan.Zero,
        CountPollFailures = false,
    };

    public override bool SupportsDiscoveryProbe => true;
    protected override TimeSpan ProbeCooldown => TimeSpan.FromSeconds(10);

    protected override bool IsCandidate(HidDeviceInfo info)
    {
        if (info.VendorId != MchoseVid) return false;
        // 0xFF0B 是 RealTek 通用 60 字节通道，不是配置/电量通道；只认 0xFF01 的 20/64 事件接口
        if (info.UsagePage != 0xFF01) return false;
        return info.InputReportLength >= 60 && info.FeatureReportLength >= MchoseMouseCodec.CmdTotalLength;
    }

    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
    {
        var status = Query(handle);
        return status is { } s
            ? (ProtocolName, s.Percent, MapCharge(s.ChargeFlag, s.Percent))
            : null;
    }

    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev)
    {
        if (!CanWrite(dev)) return null;

        var status = Query(dev.Handle);
        if (status is { } s)
        {
            ReportPollAnswered(dev);
            return (s.Percent, MapCharge(s.ChargeFlag, s.Percent));
        }

        ReportPollFailure(dev);
        return null;
    }

    protected override (int? Percent, ChargingState Charging, bool Offline)? TryParsePassiveInput(
        TrackedDevice dev, byte[] input)
    {
        if (!MchoseMouseCodec.TryParseStatus(input, out var status)) return null;

        Log.Info($"迈从鼠标 被动 0xE2: 电量={status.Percent} 充电=0x{status.ChargeFlag:X2} 名字={status.Name}");
        return (status.Percent, MapCharge(status.ChargeFlag, status.Percent), Offline: false);
    }

    /// <summary>发送 0B AA，等 0xE2（实测约 300ms；给 2 秒余量）。</summary>
    private static MouseStatus? Query(HidDeviceHandle handle)
    {
        if (!IsWritesEnabled(SettingsKey)) return null;

        var payload = MchoseMouseCodec.BuildStatusQueryPayload();
        if (!handle.SetFeature(MchoseMouseCodec.CmdReportId, payload, MchoseMouseCodec.CmdTotalLength))
        {
            Log.Debug($"迈从鼠标 写 feature 0x11 失败: {handle.Info.DevicePath}");
            return null;
        }

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            var input = handle.ReadInput(TimeSpan.FromMilliseconds(300));
            if (input == null) continue;

            if (MchoseMouseCodec.TryParseStatus(input, out var status))
            {
                Log.Info($"迈从鼠标 0xE2 状态: 电量={status.Percent} 充电=0x{status.ChargeFlag:X2} " +
                         $"回报率={MchoseMouseCodec.PollingRate(status.PollingIndex)?.ToString() ?? "?"}Hz 原始={Log.Hex(input)}");
                return status;
            }

            if (Log.IsEnabled(LogLevel.Debug)) Log.Debug($"迈从鼠标 忽略报文: {Log.Hex(input)}");
        }

        Log.Debug($"迈从鼠标 2 秒内未收到 0xE2（鼠标多半在睡）: {handle.Info.DevicePath}");
        return null;
    }

    /// <summary>0xE2 [3]：0x01 = 充电底座/充电中，0x00 = 放电；收尾规则见 <see cref="ChargingStateMap"/>。</summary>
    private static ChargingState MapCharge(byte chargeFlag, int percent)
        => ChargingStateMap.FromChargingFlag(chargeFlag == MchoseMouseCodec.ChargeDocked, percent);
}
