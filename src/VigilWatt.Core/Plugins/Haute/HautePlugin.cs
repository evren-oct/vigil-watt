using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Haute;

/// <summary>
/// 功 能： Haute Adapter 4DS 连接 DualSense 时的纯读取电量插件。
/// 2026/10/19 20:38:47 kaniu
/// </summary>
public sealed class HautePlugin : BatteryPluginBase
{
    public override string Name => "Haute";
    public override string DisplayName => "Haute";
    public override string DisplayNameEn => "HAUTE";
    public override DeviceKind Kind => DeviceKind.Gamepad;
    protected override int MinValidPercent => 0;
    public override bool SupportsFastDiscovery => true;
    protected override bool WritesEnabled => false;
    protected override bool ProbeWrites => false;
    protected override TimeSpan ProbeCooldown => TimeSpan.FromSeconds(5);

    protected override string BuildDeviceId(HidDeviceHandle handle) => ReadDeviceId(handle);

    private static string ReadDeviceId(HidDeviceHandle handle)
    {
        var report = handle.GetFeature(HauteCodec.DeviceInfoReportId, out int error);
        if (report != null && HauteCodec.TryParseDeviceId(report, out var id)) return id;
        throw new InvalidDataException($"Haute 芯片标识读取失败: Win32={error}");
    }

    protected override bool IsCandidate(HidDeviceInfo info)
        => info.VendorId == 0x054C && info.ProductId == 0x0CE6
           && info.UsagePage == 0x0001 && info.Usage == 0x0005
           && info.FeatureReportLength >= 14;

    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
    {
        var firmware = handle.GetFeature(HauteCodec.FirmwareReportId);
        if (firmware == null || !HauteCodec.IsHauteFirmware(firmware)) return null;
        return ReadStatus(handle) is { } read ? ("haute4ds-feature-f9", read.Percent, read.Charging) : null;
    }

    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev)
    {
        var read = ReadStatus(dev.Handle);
        if (!read.HasValue)
            read = RecoverStatus(dev);
        if (read.HasValue)
        {
            ReportPollAnswered(dev);
            if (!read.Value.Percent.HasValue) RequestOffline(dev);
        }
        return read;
    }

    private static (int? Percent, ChargingState Charging)? ReadStatus(HidDeviceHandle handle)
    {
        try
        {
            var input = handle.GetFeature(HauteCodec.StatusReportId, out int error);
            if (input != null && HauteCodec.TryParse(input, out var percent, out var charging))
                return (percent, charging);
            Log.Warn($"Haute 状态读取失败: Win32={error}，报文长度={input?.Length ?? 0}");
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            Log.Warn($"Haute 状态句柄不可用: {ex.Message}");
        }
        return null;
    }

    private static (int? Percent, ChargingState Charging)? RecoverStatus(TrackedDevice dev)
    {
        HidDeviceHandle? replacement = null;
        try
        {
            // 相同设备路径可能保留失效句柄；只重开已认领接口，不把一次通信失败当作手柄断开。
            replacement = dev.Info.Open();
            var firmware = replacement.GetFeature(HauteCodec.FirmwareReportId);
            if (firmware == null || !HauteCodec.IsHauteFirmware(firmware)) return null;
            if (ReadDeviceId(replacement) != dev.Status.Id)
            {
                Log.Warn("Haute 重开的接口属于另一接收器，保留原设备离线状态");
                RequestReprobe(dev);
                return null;
            }
            var read = ReadStatus(replacement);
            if (!read.HasValue) return null;
            var previous = dev.Handle;
            dev.Handle = replacement;
            replacement = null;
            previous.Dispose();
            Log.Info("Haute 已重开状态接口，恢复电量读取");
            return read;
        }
        catch (Exception ex)
        {
            Log.Warn($"Haute 状态接口重开失败: {ex.Message}");
            return null;
        }
        finally { replacement?.Dispose(); }
    }

    protected override string BuildDisplayName(HidDeviceInfo info) => "DualSense (Haute Adapter 4DS)";
}
