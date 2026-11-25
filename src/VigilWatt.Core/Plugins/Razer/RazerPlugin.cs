using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Razer;

/// <summary>
/// 功 能： 查询雷蛇已适配鼠标和耳机的电量。
/// 2026/10/24 23:45:00 imx1n
/// 2026/10/25 16:21:49 Mzood（扩展 SE 鼠标与 PA 耳机）
/// </summary>
public sealed class RazerPlugin : BatteryPluginBase
{
    public const ushort RazerVid = RazerCodec.RazerVid;
    private static readonly ConcurrentDictionary<ushort, byte> LearnedTransaction = new();
    private readonly ConditionalWeakTable<HidDeviceHandle, RazerReadingState> _readings = new();

    public override string Name => "Razer";
    public override string DisplayName => "雷蛇";
    public override string DisplayNameEn => "RAZER";
    public override DeviceKind Kind => DeviceKind.Mouse;
    public override bool SupportsDiscoveryProbe => true;
    protected override TimeSpan ProbeCooldown => TimeSpan.FromSeconds(8);
    protected override bool FeatureOnly(HidDeviceInfo info) => info.ProductId == 0x00DF;
    protected override int MinValidPercent(HidDeviceInfo info) => IsLegacyMouse(info) ? 2 : 0;
    protected override OfflinePolicy Offline { get; } = new() { Confirm = TimeSpan.FromSeconds(6), CountPollFailures = false };

    protected override bool IsCandidate(HidDeviceInfo info) =>
        IsLegacyMouse(info) || RazerDeviceProfile.Matches(info.VendorId, info.ProductId, info.UsagePage,
            info.Usage, info.FeatureReportLength, info.DevicePath)
        || RazerDeviceProfile.MatchesHeadset(info.VendorId, info.ProductId, info.UsagePage,
            info.Usage, info.InputReportLength, info.OutputReportLength, info.OutputReportId, info.DevicePath);

    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
    {
        var reading = Query(handle);
        string protocol = IsLegacyMouse(handle.Info) ? "razer-hid"
            : IsHeadset(handle.Info) ? "razer-pa" : "razer-90b";
        return reading.HasValue ? (protocol, reading.Value.Percent, reading.Value.Charging) : null;
    }

    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev)
    {
        if (!CanWrite(dev)) return null;
        var reading = Query(dev.Handle);
        if (reading.HasValue) ReportPollAnswered(dev);
        else ReportPollFailure(dev);
        return reading;
    }

    private (int? Percent, ChargingState Charging)? Query(HidDeviceHandle handle)
    {
        if (!WritesEnabled) return null;
        if (IsLegacyMouse(handle.Info))
        {
            var legacy = QueryLegacy(handle, handle.Info.ProductId);
            return legacy.HasValue ? (legacy.Value.Percent, legacy.Value.Charging) : null;
        }
        var state = _readings.GetValue(handle, h => new RazerReadingState(IsHeadset(h.Info)));
        bool success = IsHeadset(handle.Info) ? QueryHeadset(handle, state) : QueryMouse(handle, state);
        if (success)
        {
            Log.ResetOnce(handle.Info.DevicePath + "|razer-query");
            return (state.Percent, state.Charging);
        }
        state.RecordFailure();
        Log.Once(LogLevel.Warn, handle.Info.DevicePath + "|razer-query",
            "雷蛇查询无有效响应（设备可能休眠或接口不可用）");
        return null;
    }

    private bool QueryMouse(HidDeviceHandle handle, RazerReadingState state)
    {
        if (!WritesEnabled || !handle.SetFeature(0, RazerViperSeCodec.CreateQuery(state.NextCommand))) return false;
        Thread.Sleep(60);
        var report = handle.GetFeature(0);
        return report != null && state.TryApply(report);
    }

    private bool QueryHeadset(HidDeviceHandle handle, RazerReadingState state)
    {
        bool authorized = WritesEnabled;
        if (!authorized) return false;
        for (int i = 0; i < 8; i++)
            if (handle.ReadInput(TimeSpan.FromMilliseconds(1)) == null) break;

        // 一笔有界会话取得授权后须在 finally 归还远程模式；见 docs/hardware/devices/razer-headset.md。
        bool Write(byte[] frame) => authorized && handle.WriteOutput(frame, TimeSpan.FromMilliseconds(500));
        bool opened = false;
        try
        {
            opened = true;
            for (int i = 0; i < 2; i++)
            {
                if (!Write(RazerHeadsetCodec.CreateRemoteMode(true))) return false;
                Thread.Sleep(35);
            }
            if (!Write(RazerHeadsetCodec.CreateQuery(state.NextCommand))) return false;
            var until = DateTime.UtcNow.AddMilliseconds(700);
            while (DateTime.UtcNow < until)
            {
                var report = handle.ReadInput(TimeSpan.FromMilliseconds(100));
                if (report == null) continue;
                bool valid = state.NextCommand == RazerHeadsetCodec.BatteryCommand
                    ? RazerHeadsetCodec.TryParseBattery(report, out _)
                    : RazerHeadsetCodec.TryParseCharging(report, out _);
                if (valid) return state.TryApply(report);
            }
            return false;
        }
        finally
        {
            if (opened && !Write(RazerHeadsetCodec.CreateRemoteMode(false)))
                Log.Warn("雷蛇耳机远程会话释放失败");
        }
    }

    private static bool IsLegacyMouse(HidDeviceInfo info) =>
        RazerCodec.IsSupported(info.VendorId, info.ProductId, info.FeatureReportLength);

    private static bool IsHeadset(HidDeviceInfo info) => info.ProductId == 0x0555;
    protected override DeviceKind ResolveKind(string protocol) =>
        protocol == "razer-pa" ? DeviceKind.Headset : DeviceKind.Mouse;
    protected override string BuildDisplayName(HidDeviceInfo info) => IsHeadset(info)
        ? "雷蛇 BlackShark V2 Pro" : IsLegacyMouse(info) ? base.BuildDisplayName(info) : "雷蛇 Viper V3 Pro SE";

    public override void Detach()
    {
        base.Detach();
        _readings.Clear();
    }

    private (string Protocol, int? Percent, ChargingState Charging)? QueryLegacy(HidDeviceHandle handle, ushort pid)
    {
        var candidates = LearnedTransaction.TryGetValue(pid, out byte learned)
            ? new[] { learned }
            : RazerCodec.TransactionCandidates(pid);

        for (int attempt = 0; attempt < 2; attempt++)
        {
            foreach (byte txn in candidates)
            {
                if (!WritesEnabled || !handle.SetFeature(RazerCodec.ReportId, RazerCodec.BuildRequest(RazerCodec.CommandGetBattery, txn)))
                    continue;

                var response = handle.GetFeature(RazerCodec.ReportId);
                if (!RazerCodec.TryParseBattery(response, out int raw))
                    continue;

                LearnedTransaction[pid] = txn;
                int percent = RazerCodec.ToPercent(pid, raw);

                var charging = ChargingState.Unknown;
                if (WritesEnabled && handle.SetFeature(RazerCodec.ReportId, RazerCodec.BuildRequest(RazerCodec.CommandGetCharging, txn)))
                {
                    var chargeResponse = handle.GetFeature(RazerCodec.ReportId);
                    if (RazerCodec.TryParseCharging(chargeResponse, out var state))
                        charging = state;
                }

                // 100% 且仍报充电中，对用户来说就是"已充满"。
                if (percent >= 100 && charging == ChargingState.Charging)
                    charging = ChargingState.Full;

                return ("razer-hid", percent, charging);
            }

            if (attempt == 0)
                Thread.Sleep(40);
        }

        return null;
    }
}
