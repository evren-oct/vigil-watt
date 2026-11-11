using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Pulsar;

/// <summary>
/// 功 能： 派世 (Pulsar) X2 V2 无线鼠标（dongle 3554:F508）插件 —— STATUS 判在线、POWER 读电量与充电位。
/// 2026/10/18 17:24:10 sheng
/// 帧与字段见 <see cref="PulsarCodec"/>；本机实测 20% / 3570 mV，见 docs/hardware/brands/pulsar.md §1。
/// </summary>
public sealed class PulsarPlugin : BatteryPluginBase
{
    public const ushort PulsarVid = 0x3554;

    /// <summary>厂商集合的 usage page（本机 dongle 的 mi_01&amp;col05）。</summary>
    private const ushort VendorUsagePage = 0xFF02;

    private const int VendorReportLength = 17;

    public override string Name => "Pulsar";
    public override string DisplayName => "派世";
    public override string DisplayNameEn => "PULSAR";
    public override DeviceKind Kind => DeviceKind.Mouse;

    /// <summary>两帧查询即出读数，但毕竟是写帧，只能走 SupportsDiscoveryProbe 那条节拍。</summary>
    public override bool SupportsDiscoveryProbe => true;

    /// <summary>10 秒 &lt; 15 秒节拍，保证每一拍都能重探（探测最多 2 帧，代价可忽略）。</summary>
    protected override TimeSpan ProbeCooldown => TimeSpan.FromSeconds(10);

    /// <summary>鼠标休眠时接口静音是常态，不是离线；null 的三义只有插件分得清，故自己记账。</summary>
    protected override OfflinePolicy Offline { get; } = new()
    {
        Silence = TimeSpan.Zero,
        CountPollFailures = false,
    };

    /// <summary>同品牌其它接口（键盘/consumer/system）是标准用途，只有 17 字节双向 vendor 通道说 Pulsar 协议。</summary>
    protected override bool IsCandidate(HidDeviceInfo info)
    {
        if (info.VendorId != PulsarVid) return false;
        if (info.UsagePage != VendorUsagePage && info.Usage != VendorUsagePage) return false;
        return info.InputReportLength == VendorReportLength && info.OutputReportLength == VendorReportLength;
    }

    /// <summary>先 STATUS 确认设备在线，再 POWER 取电量；设备没连上（dongle 在位）时不进设备列表。</summary>
    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
    {
        var status = Query(handle, PulsarCodec.CmdStatus);
        if (status == null)
        {
            Log.Once(LogLevel.Debug, $"{LogKey(handle)}|no-status", "派世 STATUS 无应答（鼠标可能休眠）");
            return null;
        }
        if (!PulsarCodec.TryParseOnline(status, out bool online) || !online) return null;

        var power = Query(handle, PulsarCodec.CmdPower);
        if (power == null || !PulsarCodec.TryParsePower(power, out int percent, out bool charging, out int mv))
        {
            Log.Once(LogLevel.Debug, $"{LogKey(handle)}|no-power", "派世 POWER 无有效读数");
            return null;
        }

        Log.ResetOnce($"{LogKey(handle)}|no-status");
        Log.ResetOnce($"{LogKey(handle)}|no-power");
        Log.Info($"派世 协议握手成功: 电量={percent}% 充电={charging} 电压={mv} mV 通道={handle.Info.DevicePath}");
        return ("pulsar-17b", percent, charging ? ChargingState.Charging : ChargingState.Discharging);
    }

    /// <summary>轮询只发 POWER；STATUS 只在探测时用来区分"设备没连上"和"协议不对"。</summary>
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

    /// <summary>产品名就是型号（X2 V2），加中文品牌前缀即可。</summary>
    protected override string BuildDisplayName(HidDeviceInfo info)
        => string.IsNullOrEmpty(info.Product) ? $"派世 ({info.VidPid})" : $"派世 {info.Product}";

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
            if (Log.IsEnabled(LogLevel.Debug)) Log.Debug($"派世 无关帧: {Log.Hex(input)}");
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

    /// <summary>应答窗口 1.2 秒：实测往返 &lt;30ms，鼠标从休眠唤醒时要更久。</summary>
    private static readonly TimeSpan ReplyWindow = TimeSpan.FromMilliseconds(1200);

    private static string LogKey(HidDeviceHandle handle) => handle.Info.DevicePath;
}
