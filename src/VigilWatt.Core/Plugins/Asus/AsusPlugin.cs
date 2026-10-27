using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Asus;

/// <summary>
/// 功 能： 读取 ROG Omni 接收器上已核实的键盘电量，以及 0x1AFA 无线耳机的电量与充电位。
/// 2026/10/25 15:31:00 Mzood
/// 2026/10/27 21:33:00 20211（耳机充电位补成每轮两条帧）
/// </summary>
public sealed class AsusPlugin : BatteryPluginBase
{
    public override string Name => "Asus";
    public override string DisplayName => "华硕";
    public override string DisplayNameEn => "ASUS ROG";
    public override DeviceKind Kind => DeviceKind.Keyboard;
    protected override int MinValidPercent(HidDeviceInfo info) => 0;
    protected override OfflinePolicy Offline { get; } = new() { CountPollFailures = false };

    protected override bool IsCandidate(HidDeviceInfo info) =>
        AsusKeyboardCodec.IsKeyboardInterface(info.VendorId, info.ProductId, info.UsagePage,
            info.Usage, info.InputReportLength, info.OutputReportLength, info.OutputReportId, info.DevicePath)
        || AsusHeadsetCodec.IsHeadsetInterface(info);

    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
    {
        if (AsusHeadsetCodec.IsHeadsetInterface(handle.Info)) return ProbeHeadset(handle);
        if (!WritesEnabled || !VerifyPairedModel(handle.Info)) return null;
        var reading = QueryBattery(handle);
        return reading.HasValue ? ("asus-omni-12-01", reading.Value.Percent, reading.Value.Charging) : null;
    }

    /// <summary>耳机与键盘同一品牌开关；耳机是直连 dongle，没有配对列表可核对，靠 PID 白名单过滤。</summary>
    private (string Protocol, int? Percent, ChargingState Charging)? ProbeHeadset(HidDeviceHandle handle)
    {
        if (!WritesEnabled) return null;
        var percent = ReadHeadsetPower(handle);
        if (percent == null)
        {
            Log.Once(LogLevel.Debug, handle.Info.DevicePath + "|asus-hs", "华硕耳机 POWER 无应答（未开机或未连上）");
            return null;
        }
        // 认领只发电量帧：多一条会让"启动约 1 秒认领"退化成 1.7 秒，充电位下一轮补上即可。
        return (AsusHeadsetCodec.Protocol, percent, ChargingState.Unknown);
    }

    protected override DeviceKind ResolveKind(string protocol) =>
        protocol == AsusHeadsetCodec.Protocol ? DeviceKind.Headset : base.ResolveKind(protocol);

    private bool VerifyPairedModel(HidDeviceInfo keyboard)
    {
        string? key = AsusKeyboardCodec.ReceiverKey(keyboard.DevicePath);
        if (key == null) return false;
        var own = HidDeviceScanner.Scan().Where(x =>
            x.VendorId == 0x0B05 && x.ProductId == 0x1ACE
            && x.UsagePage is >= 0xFF00 and <= 0xFF02 && x.Usage == 1
            && x.InputReportLength == 64 && x.OutputReportLength == 64 && x.OutputReportId == 1
            && string.Equals(AsusKeyboardCodec.ReceiverKey(x.DevicePath), key, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (own.Length != 1 || !WritesEnabled) return false;
        using var control = own[0].Open();
        var reply = Query(control, AsusKeyboardCodec.CreatePairedQuery(),
            r => AsusKeyboardCodec.HasScopeKeyboard(r));
        if (reply != null) return true;
        Log.Once(LogLevel.Warn, keyboard.DevicePath + "|asus-pair",
            "ROG Omni 未确认当前通道配对的是 Scope II 96，跳过电量查询");
        return false;
    }

    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev)
    {
        if (!CanWrite(dev)) return null;
        if (dev.Protocol == AsusHeadsetCodec.Protocol) return PollHeadset(dev);
        var reading = QueryBattery(dev.Handle);
        if (reading.HasValue) ReportPollAnswered(dev);
        else ReportPollFailure(dev);
        return reading;
    }

    /// <summary>电量帧必需、充电帧可选：充电拿不到只丢充电位，不能让它停更电量或累计熔断（safety.md §3）。</summary>
    private (int? Percent, ChargingState Charging)? PollHeadset(TrackedDevice dev)
    {
        var percent = ReadHeadsetPower(dev.Handle);
        if (percent == null)
        {
            ReportPollFailure(dev);
            return null;
        }
        ReportPollAnswered(dev);
        var charging = ReadHeadsetCharging(dev.Handle, percent.Value) ?? dev.Status.Charging;
        return (percent, charging);
    }

    /// <summary>只发 0x07 一条帧取电量；充电位由 <see cref="ReadHeadsetCharging"/> 单独补。</summary>
    private int? ReadHeadsetPower(HidDeviceHandle handle)
    {
        var command = AsusHeadsetCodec.CmdPower;
        var reply = Query(handle, AsusHeadsetCodec.BuildRead(command),
            r => AsusHeadsetCodec.TryParse(r, command, out _));
        if (reply == null || !AsusHeadsetCodec.TryParse(reply, command, out var data)) return null;
        if (!AsusHeadsetCodec.TryParsePower(data, out int percent, out _, out _)) return null;
        return percent;
    }

    /// <summary>0x08 无应答返回 null 交给调用方沿用旧值——它不计入熔断，充电位丢了不该把设备判离线。</summary>
    private ChargingState? ReadHeadsetCharging(HidDeviceHandle handle, int percent)
    {
        var command = AsusHeadsetCodec.CmdCharging;
        var reply = Query(handle, AsusHeadsetCodec.BuildRead(command),
            r => AsusHeadsetCodec.TryParse(r, command, out _));
        if (reply == null || !AsusHeadsetCodec.TryParse(reply, command, out var data)) return null;
        if (!AsusHeadsetCodec.TryParseCharging(data, out bool charging)) return null;
        return ChargingStateMap.FromChargingFlag(charging, percent);
    }

    private (int? Percent, ChargingState Charging)? QueryBattery(HidDeviceHandle handle)
    {
        var reply = Query(handle, AsusKeyboardCodec.CreateBatteryQuery(), r =>
            AsusKeyboardCodec.TryParseBattery(r, out _, out _));
        if (reply == null || !AsusKeyboardCodec.TryParseBattery(reply, out int percent, out bool charging))
            return null;
        return (percent, ChargingStateMap.FromChargingFlag(charging, percent));
    }

    private byte[]? Query(HidDeviceHandle handle, byte[] request, Func<byte[], bool> matches)
    {
        if (!WritesEnabled) return null;
        for (int i = 0; i < 8; i++)
            if (handle.ReadInput(TimeSpan.FromMilliseconds(1)) == null) break;
        if (!WritesEnabled || !handle.WriteOutput(request, TimeSpan.FromMilliseconds(500))) return null;
        var until = DateTime.UtcNow.AddMilliseconds(700);
        while (DateTime.UtcNow < until)
        {
            var reply = handle.ReadInput(TimeSpan.FromMilliseconds(100));
            if (reply != null && matches(reply)) return reply;
        }
        return null;
    }

    protected override string BuildDisplayName(HidDeviceInfo info) =>
        AsusHeadsetCodec.IsHeadsetInterface(info)
            ? string.IsNullOrEmpty(info.Product) ? "华硕 ROG 耳机" : $"华硕 {info.Product}"
            : "ROG Strix Scope II 96 Wireless";
}
