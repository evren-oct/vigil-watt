using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Langtu;

/// <summary>
/// 功 能： 狼途 T87 键盘 2.4G 插件：发一帧 GET_DEVICE_INFO，读电量与充电位。
/// 2026/11/17 16:40:00 meish
/// 与 T98（零写入被动）共用 "Langtu" 开关、ClaimId 用 #T87 区分；协议见 docs/hardware/devices/langtu.md §2。
/// </summary>
public sealed class LangtuT87Plugin : BatteryPluginBase
{
    /// <summary>接收器 VID/PID（产品串 "Langtu T87-2.4G"）。</summary>
    public const ushort VendorId = 0x0C45;
    public const ushort ProductId = 0xFEFE;

    /// <summary>厂商通道：MI_03 实测 0xFF60/0x0061，进出各 33 字节；MI_04（0xFF59）是 65 字节陷阱接口。</summary>
    public const ushort ConfigUsagePage = 0xFF60;
    public const ushort ConfigUsage = 0x0061;

    public const string ProtocolName = "langtu-t87-get-device-info";

    public override string Name => "Langtu";

    public override string DisplayName => "狼途";

    public override string DisplayNameEn => "LANGTU";

    public override DeviceKind Kind => DeviceKind.Keyboard;

    /// <summary>T87 与 T98 共用一个 "Langtu" 开关，但认领互斥身份要各给一个（基类注释）。</summary>
    protected override string ClaimId => "Langtu#T87";

    /// <summary>探测失败后 30 秒再试；不进 15 秒发现节拍（会写帧的插件保持默认）。</summary>
    protected override TimeSpan ProbeCooldown => TimeSpan.FromSeconds(30);

    /// <summary>休眠即静默：不记失败不熔断，否则退避会把唤醒检测挡到 5 分钟（langtu.md §3）。</summary>
    protected override OfflinePolicy Offline { get; } = new()
    {
        Silence = TimeSpan.Zero,
        CountPollFailures = false,
    };

    /// <summary>认领的是 2.4G 接收器的厂商接口，有线数据不会经过它，链路可推定 2.4G；否则徽标先显示「未知」。</summary>
    protected override TelemetryValue<ConnectionKind> ConnectionOfProbe(HidDeviceInfo info)
        => TelemetryValue<ConnectionKind>.Inferred(ConnectionKind.Wireless24G, DateTimeOffset.Now);

    /// <summary>0C45:FEFE 与前行者 X87 同指纹，HID 层只能按产品名分品牌（langtu.md §1.1）。</summary>
    protected override bool IsCandidate(HidDeviceInfo info)
    {
        if (info.VendorId != VendorId || info.ProductId != ProductId) return false;
        if (info.UsagePage != ConfigUsagePage || info.Usage != ConfigUsage) return false;
        if (info.OutputReportLength < 1 + LangtuT87Codec.HeaderLength + LangtuT87Codec.MinPayloadLength)
            return false;
        return LangtuT87Codec.LooksLikeLangtu(info.Product);
    }

    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
    {
        var read = Query(handle, TimeSpan.FromSeconds(2));
        return read is { } value ? (ProtocolName, value.Percent, LangtuT87Codec.MapCharging(value)) : null;
    }

    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev)
    {
        if (!CanWrite(dev)) return null;

        var read = Query(dev.Handle, TimeSpan.FromSeconds(1.5));
        if (read is not { } value) return null;
        ReportPollAnswered(dev);
        return (value.Percent, LangtuT87Codec.MapCharging(value));
    }

    /// <summary>显示名：产品串里的型号（"Langtu T87-2.4G" → "狼途 T87"）；拿不到就只写品牌，不猜型号。</summary>
    protected override string BuildDisplayName(HidDeviceInfo info)
    {
        var model = CleanModel(info.Product);
        return string.IsNullOrEmpty(model) ? "狼途" : $"狼途 {model}";
    }

    /// <summary>发首包查询并在窗口内等 0x55 应答；键盘休眠时无应答或全零，都返回 null。</summary>
    private static LangtuT87Codec.Reading? Query(HidDeviceHandle handle, TimeSpan window)
    {
        int outputLength = handle.Info.OutputReportLength;
        var frame = LangtuT87Codec.BuildInfoRequest(outputLength);

        if (!handle.WriteOutput(frame, TimeSpan.FromMilliseconds(500)))
        {
            Log.Info($"狼途 查询写失败（len={outputLength}）: {handle.Info.DevicePath}");
            return null;
        }

        var deadline = DateTime.UtcNow + window;
        while (DateTime.UtcNow < deadline)
        {
            var remaining = deadline - DateTime.UtcNow;
            var slice = remaining < TimeSpan.FromMilliseconds(300) ? remaining : TimeSpan.FromMilliseconds(300);
            if (slice <= TimeSpan.Zero) break;

            var input = handle.ReadInput(slice);
            if (input == null) continue;
            if (!LangtuT87Codec.TryParse(input, out var reading)) continue;

            Log.Info($"狼途 电量查询: {reading.Percent}% 充电={reading.Charging} workMode={reading.WorkMode} " +
                     $"自报PID=0x{reading.ReportedPid:X4} 原始={Log.Hex(input)}");
            return reading;
        }

        Log.Debug($"狼途 窗口内无有效应答（键盘可能已休眠）: {handle.Info.DevicePath}");
        return null;
    }

    /// <summary>产品串 <c>"Langtu T87-2.4G"</c> → <c>"T87"</c>，避免 UI 里出现两遍品牌名。</summary>
    private static string CleanModel(string? product)
    {
        if (string.IsNullOrWhiteSpace(product)) return "";
        var s = product.Trim();
        foreach (var vendor in new[] { "LANGTU", "Langtu", "langtu", "狼途" })
        {
            if (!s.StartsWith(vendor, StringComparison.OrdinalIgnoreCase)) continue;
            s = s[vendor.Length..].TrimStart(' ', '-', '_');
            break;
        }
        foreach (var suffix in new[] { "-2.4G", " 2.4G", "-2.4GHz", " 2.4GHz" })
        {
            if (!s.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
            s = s[..^suffix.Length].TrimEnd();
            break;
        }
        return s.Trim();
    }
}
