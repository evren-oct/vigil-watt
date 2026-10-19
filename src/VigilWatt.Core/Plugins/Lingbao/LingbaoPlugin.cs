using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Compx;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Lingbao;

/// <summary>
/// 功 能： 凌豹 (LINGBAO) K98 2.4G 插件，复用 Compx V1 帧协议读取电量。
/// 2026/10/19 19:30:00 Bamlax
/// 设备 3554:FA09；帧与时序见 docs/hardware/protocols.md §1.6。
/// </summary>
public sealed class LingbaoPlugin : BatteryPluginBase
{
    /// <summary>插件开关的键，与 <c>AppSettings.PluginEnabled</c> 对应（插件开关即写入授权）。</summary>
    private const string SettingsKey = "Lingbao";

    private const ushort LingbaoVid = 0x3554;
    private const ushort K98Pid = 0xFA09;
    private const string ProtocolName = "compx-v1";

    public override string Name => SettingsKey;
    public override string DisplayName => "凌豹";
    public override DeviceKind Kind => DeviceKind.Keyboard;

    /// <summary>认领后被动听 0A 推送，插拔电不必等 60 秒主动查询。</summary>
    protected override bool SupportsPassiveListen => true;

    /// <summary>协议里没有连接状态字段，失联只能靠主动查询失败计数。</summary>
    protected override OfflinePolicy Offline { get; } = new()
    {
        Silence = TimeSpan.Zero,
        CountPollFailures = false,
    };

    public override bool SupportsDiscoveryProbe => true;

    /// <summary>探测要写会话帧 + 电量查询，冷却必须小于 15 秒发现节拍。</summary>
    protected override TimeSpan ProbeCooldown => TimeSpan.FromSeconds(10);

    protected override bool IsCandidate(HidDeviceInfo info)
    {
        if (info.VendorId != LingbaoVid || info.ProductId != K98Pid) return false;
        bool vendor = info.UsagePage >= 0xFF00 || info.Usage >= 0xFF00;
        if (!vendor) return false;
        return info.OutputReportLength == CompxV1Codec.FrameLength
            && info.InputReportLength == CompxV1Codec.FrameLength;
    }

    /// <summary>产品字符串只有 "2.4G Wireless Receiver"，用固定型号名代替。</summary>
    protected override string BuildDisplayName(HidDeviceInfo info) => "凌豹 K98";

    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
    {
        if (handle.Info.OutputReportLength < CompxV1Codec.FrameLength) return null;
        return TryKeyboardV1(handle) is { } kb ? (ProtocolName, kb.Percent, kb.Charging) : null;
    }

    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev)
    {
        if (!CanWrite(dev)) return null;

        var read = PollKeyboardV1(dev);
        // 发过帧才自己报账：基类 CountPollFailures=false，不会替我们数。
        if (read.HasValue) ReportPollAnswered(dev);
        else ReportPollFailure(dev);
        return read;
    }

    protected override (int? Percent, ChargingState Charging, bool Offline)? TryParsePassiveInput(
        TrackedDevice dev, byte[] input)
    {
        var push = CompxV1Codec.ParsePush(input);
        if (push.Kind == V1PushKind.Battery)
            return (push.Percent, ParseCharge(push.ChargeFlag, push.Percent), Offline: false);

        if (push.Kind == V1PushKind.StateNotify && push.Payload == CompxV1Codec.StatePayloadZero)
        {
            // 语义未定论，不能当离线证据；只用来把"键盘真关机"的核实时机提前。
            Log.Info($"凌豹 被动收到设备状态通知 payload=0x00，触发一次核实查询: {Log.Hex(input)}");
            RequestConfirm(dev);
        }
        return null;
    }

    /// <summary>开会话（13 05 01）→ 最多两次电量查询（13 4A 01），会话期间收到 0A 推送就直接采用。</summary>
    private static (int? Percent, ChargingState Charging)? TryKeyboardV1(HidDeviceHandle handle)
    {
        if (!IsWritesEnabled(SettingsKey)) return null;

        SendKeyboardV1Frame(handle, CompxV1Codec.SessionCmd, 0x01);
        var sessionDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(1.2);
        while (DateTime.UtcNow < sessionDeadline)
        {
            var input = handle.ReadInput(TimeSpan.FromMilliseconds(300));
            if (input == null) continue;

            var read = InterpretV1Input(input, out bool stop);
            if (stop)
            {
                Log.Info("凌豹 会话期间即收到电量推送，直接采用");
                return read;
            }
            if (Log.IsEnabled(LogLevel.Debug)) Log.Debug($"凌豹 会话ACK: {Log.Hex(input)}");
        }

        for (int attempt = 1; attempt <= 2; attempt++)
        {
            if (!SendKeyboardV1Frame(handle, CompxV1Codec.BatteryQueryCmd, 0x01))
            {
                Log.Debug($"凌豹 V1 第{attempt}次写失败: {handle.Info.DevicePath}");
                continue;
            }

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(1.2);
            while (DateTime.UtcNow < deadline)
            {
                var input = handle.ReadInput(TimeSpan.FromMilliseconds(300));
                if (input == null) continue;

                var read = InterpretV1Input(input, out bool stop);
                if (stop) return read;
            }
        }

        Log.Debug($"凌豹 V1 两次询问无响应（键盘多半在睡；恢复应答后会再记一条）: {handle.Info.DevicePath}");
        return null;
    }

    private static (int? Percent, ChargingState Charging)? PollKeyboardV1(TrackedDevice dev)
    {
        if (!IsWritesEnabled(SettingsKey)) return null;
        if (!SendKeyboardV1Frame(dev.Handle, CompxV1Codec.BatteryQueryCmd, 0x01)) return null;

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(1.2);
        while (DateTime.UtcNow < deadline)
        {
            var input = dev.Handle.ReadInput(TimeSpan.FromMilliseconds(300));
            if (input == null) continue;

            var read = InterpretV1Input(input, out bool stop);
            if (stop) return read;
        }
        return null;
    }

    /// <summary>构造并发送 V1 帧：[0x13, cmd, param, 0×16, sum(前19字节)]。</summary>
    private static bool SendKeyboardV1Frame(HidDeviceHandle handle, byte cmd, byte param)
    {
        if (handle.Info.OutputReportLength < CompxV1Codec.FrameLength) return false;
        return handle.WriteOutput(CompxV1Codec.BuildFrame(cmd, param), TimeSpan.FromMilliseconds(500));
    }

    private static (int? Percent, ChargingState Charging)? InterpretV1Input(byte[] input, out bool stop)
    {
        stop = false;

        var push = CompxV1Codec.ParsePush(input);
        switch (push.Kind)
        {
            case V1PushKind.Battery:
                stop = true;
                Log.Info($"凌豹 0A 电量推送: 电量={push.Percent} 标志=0x{push.ChargeFlag:X2} 原始={Log.Hex(input)}");
                return (push.Percent, ParseCharge(push.ChargeFlag, push.Percent));

            case V1PushKind.StateNotify:
                Log.Debug($"凌豹 收到设备状态通知（payload=0x{push.Payload:X2}，语义未明仅记录）: {Log.Hex(input)}");
                return null;

            case V1PushKind.BatteryInvalid:
                Log.Debug($"凌豹 忽略异常电量帧（电量={push.Percent}）: {Log.Hex(input)}");
                return null;

            case V1PushKind.Other:
                Log.Debug($"凌豹 忽略非电量推送帧（类型=0x{push.Type:X2}）: {Log.Hex(input)}");
                return null;
        }

        switch (CompxV1Codec.ParseBatteryResponse(input, out int pct, out byte flag))
        {
            case V1ResponseKind.Battery:
                Log.Info($"凌豹 4A 电量解析: 电量={pct} 状态字节=0x{flag:X2} 原始={Log.Hex(input)}");
                stop = true;
                return (pct, ParseCharge(flag, pct));

            case V1ResponseKind.InvalidBattery:
                Log.Debug($"凌豹 电量占位/越界: {Log.Hex(input)}");
                return null;

            default:
                return null;
        }
    }

    /// <summary>充电标志：0x10 位=充电中；收尾规则见 <see cref="ChargingStateMap"/>。</summary>
    private static ChargingState ParseCharge(byte flag, int percent)
        => ChargingStateMap.FromChargingFlag((flag & 0x10) != 0, percent);
}
