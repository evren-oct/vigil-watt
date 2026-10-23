using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Pulsar;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Hecate;

/// <summary>
/// 功 能： HECATE Wireless Receiver（35BB:D3FB）鼠标插件 —— STATUS 判在线、POWER 读电量。
/// 2026/10/23 20:22:34 22469
/// 17 字节帧与派世同族（复用 <see cref="PulsarCodec"/>）；要写两条只读查询，故独立成插件、默认关闭，见 docs/hardware/hecate.md §2。
/// </summary>
public sealed class HecateWirelessPlugin : BatteryPluginBase
{
    public const ushort ReceiverPid = 0xD3FB;
    private const ushort VendorUsagePage = 0xFF02;
    private const int VendorReportLength = 17;

    public override string Name => "HecateWireless";
    public override string DisplayName => "漫步者电竞（查询型）";
    public override string DisplayNameEn => "HECATE (Query)";
    public override DeviceKind Kind => DeviceKind.Mouse;

    /// <summary>两帧查询即出读数，但毕竟是写帧，只能走 SupportsDiscoveryProbe 那条节拍。</summary>
    public override bool SupportsDiscoveryProbe => true;

    protected override TimeSpan ProbeCooldown => TimeSpan.FromSeconds(10);

    /// <summary>鼠标休眠时接口静音是常态，不是离线；与派世同理，自己记账。</summary>
    protected override OfflinePolicy Offline { get; } = new()
    {
        Silence = TimeSpan.Zero,
        CountPollFailures = false,
    };

    /// <summary>同 VID 的 mi_01 其它集合是标准用途，只有 17 字节双向 0xFF02 通道说此协议；D00B 接收器归被动插件。</summary>
    protected override bool IsCandidate(HidDeviceInfo info)
        => info.VendorId == HecatePlugin.HecateVid && info.ProductId == ReceiverPid
           && info.UsagePage == VendorUsagePage
           && info.InputReportLength == VendorReportLength
           && info.OutputReportLength == VendorReportLength;

    /// <summary>先 STATUS 确认鼠标在线，再 POWER 取电量；接收器在位但鼠标没连上时不进设备列表。</summary>
    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
    {
        var status = Query(handle, PulsarCodec.CmdStatus);
        if (status == null)
        {
            Log.Once(LogLevel.Debug, $"{handle.Info.DevicePath}|no-status", "HECATE STATUS 无应答（鼠标可能休眠）");
            return null;
        }
        if (!PulsarCodec.TryParseOnline(status, out bool online) || !online) return null;

        var power = Query(handle, PulsarCodec.CmdPower);
        if (power == null || !PulsarCodec.TryParsePower(power, out int percent, out bool charging, out int mv))
        {
            Log.Once(LogLevel.Debug, $"{handle.Info.DevicePath}|no-power", "HECATE POWER 无有效读数");
            return null;
        }

        Log.ResetOnce($"{handle.Info.DevicePath}|no-status");
        Log.ResetOnce($"{handle.Info.DevicePath}|no-power");
        Log.Info($"HECATE 协议握手成功: 电量={percent}% 充电位={charging} 电压={mv} mV 通道={handle.Info.DevicePath}");
        return ("hecate-17b", percent, charging ? ChargingState.Charging : ChargingState.Discharging);
    }

    /// <summary>轮询只发 POWER；STATUS 只在探测时用来区分"鼠标没连上"和"协议不对"。</summary>
    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev)
    {
        if (!CanWrite(dev)) return null;

        (int? Percent, ChargingState Charging)? read = null;
        var power = Query(dev.Handle, PulsarCodec.CmdPower);
        if (power != null && PulsarCodec.TryParsePower(power, out int percent, out bool charging, out _))
            read = (percent, charging ? ChargingState.Charging : ChargingState.Discharging);

        if (read.HasValue) ReportPollAnswered(dev);
        else ReportPollFailure(dev);
        return read;
    }

    /// <summary>接收器产品串不含鼠标型号，协议里也没找到型号字段；型号来自用户告知，见 hecate.md §2.3。</summary>
    protected override string BuildDisplayName(HidDeviceInfo info) => "HECATE G3M Pro 升级版";

    /// <summary>发一条命令并等对应应答。先排空迟到的旧帧，避免拿上一条应答当本轮结果。</summary>
    private static byte[]? Query(HidDeviceHandle handle, byte command)
    {
        Drain(handle);
        if (!handle.WriteOutput(PulsarCodec.BuildFrame(command), TimeSpan.FromMilliseconds(500))) return null;

        var deadline = DateTime.UtcNow + ReplyWindow;
        while (DateTime.UtcNow < deadline)
        {
            var input = handle.ReadInput(TimeSpan.FromMilliseconds(300));
            if (input == null) continue;
            if (PulsarCodec.TryParse(input, command, out var payload)) return payload;
            if (Log.IsEnabled(LogLevel.Debug)) Log.Debug($"HECATE 无关帧: {Log.Hex(input)}");
        }
        return null;
    }

    /// <summary>最多丢 4 条残留帧；单悬空读实现下，超时读会被下一次调用取走，不会堆积。</summary>
    private static void Drain(HidDeviceHandle handle)
    {
        for (int i = 0; i < 4; i++)
        {
            if (handle.ReadInput(TimeSpan.FromMilliseconds(1)) == null) return;
        }
    }

    /// <summary>应答窗口 1.2 秒：本机实测秒回，鼠标从休眠唤醒时要更久。</summary>
    private static readonly TimeSpan ReplyWindow = TimeSpan.FromMilliseconds(1200);
}
