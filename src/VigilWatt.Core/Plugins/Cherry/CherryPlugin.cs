using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Cherry;

/// <summary>
/// 功 能： CHERRY PIXIU75 无线键盘（2.4G dongle 046A:01E4）插件 —— 读厂商块 0x1A 取电量，0x1D 仅补充设备信息。
/// 2026/10/18 17:24:10 sheng
/// 帧格式见 <see cref="CherryCodec"/>；电量 48% 与官方软件 UI 交叉验证过，见 docs/hardware/brands/cherry.md §1。
/// </summary>
public sealed class CherryPlugin : BatteryPluginBase
{
    public const ushort CherryVid = 0x046A;

    /// <summary>厂商集合的 usage page（本机 dongle 的 mi_01&amp;col04，64 字节双向）。</summary>
    private const ushort VendorUsagePage = 0xFF1C;

    private const int VendorReportLength = 64;

    public override string Name => "Cherry";
    public override string DisplayName => "樱桃";
    public override string DisplayNameEn => "CHERRY";
    public override DeviceKind Kind => DeviceKind.Keyboard;

    /// <summary>读数全靠发帧，故不能走零写入的快速发现；但参与 15 秒节拍能让键盘开机的发现从最坏 60 秒缩到 15 秒。</summary>
    public override bool SupportsDiscoveryProbe => true;

    /// <summary>10 秒 &lt; 15 秒节拍，保证每一拍都能重探（同迈从，理由见 MchosePlugin）。</summary>
    protected override TimeSpan ProbeCooldown => TimeSpan.FromSeconds(10);

    /// <summary>协议里没有"连接状态"字段，也没有自发推送；null 的三义（无应答/未开写入/熔断中）只有插件分得清，故自己记账。</summary>
    protected override OfflinePolicy Offline { get; } = new()
    {
        Silence = TimeSpan.Zero,
        CountPollFailures = false,
    };

    /// <summary>同一个品牌的键盘、鼠标、灯控接口都有 vendor usage，必须收窄到 64 字节双向通道才是块通道。</summary>
    protected override bool IsCandidate(HidDeviceInfo info)
    {
        if (info.VendorId != CherryVid) return false;
        if (info.UsagePage != VendorUsagePage && info.Usage != VendorUsagePage) return false;
        return info.OutputReportLength == VendorReportLength && info.InputReportLength == VendorReportLength;
    }

    /// <summary>先读电量块（认设备的必要条件），设备信息块只作附加校验：真机上它只在会话刚建立时给真值，之后长期回全零。</summary>
    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
    {
        int? pid = null;
        if (RequestBlock(handle, CherryCodec.BlockDeviceInfo, 0x18, out var infoPayload) &&
            CherryCodec.TryParseDeviceInfo(infoPayload, CherryVid, out ushort id))
            pid = id;

        if (!RequestBlock(handle, CherryCodec.BlockBattery, 4, out var batteryPayload) ||
            !CherryCodec.TryParseBattery(batteryPayload, out int percent, out bool charging))
        {
            Log.Once(LogLevel.Debug, $"{LogKey(handle)}|no-battery", "CHERRY 电量块无有效读数（键盘可能休眠）");
            return null;
        }

        Log.ResetOnce($"{LogKey(handle)}|no-battery");
        Log.Info($"CHERRY 块协议握手成功: PID={(pid.HasValue ? $"0x{pid:X4}" : "未公布")} 电量={percent}% 充电={charging} 通道={handle.Info.DevicePath}");
        return ("cherry-block", percent, ToChargingState(charging));
    }

    /// <summary>轮询只读电量块；发过帧才自己记账，未发帧（熔断/退避）不计失败。</summary>
    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev)
    {
        if (!CanWrite(dev)) return null;

        (int? Percent, ChargingState Charging)? read = null;
        if (RequestBlock(dev.Handle, CherryCodec.BlockBattery, 4, out var payload) &&
            CherryCodec.TryParseBattery(payload, out int percent, out bool charging))
            read = (percent, ToChargingState(charging));

        if (read.HasValue) ReportPollAnswered(dev);
        else ReportPollFailure(dev);
        return read;
    }

    /// <summary>产品名是 dongle 的 "PIXIU75 Dongle"，去掉修饰词比直接显示 dongle 名更像用户认知里的键盘。</summary>
    protected override string BuildDisplayName(HidDeviceInfo info)
    {
        string product = info.Product.Replace("Dongle", "", StringComparison.OrdinalIgnoreCase).Trim();
        return string.IsNullOrEmpty(product) ? $"CHERRY ({info.VidPid})" : $"CHERRY {product}";
    }

    /// <summary>
    /// 写一条读块请求并等它的应答。先按块号筛帧：同一接口上还可能挂着上一条请求的迟到应答，
    /// 令牌未验证过（恒为 0），块号是唯一可靠的关联依据。
    /// </summary>
    private static bool RequestBlock(HidDeviceHandle handle, byte block, byte length, out byte[] payload)
    {
        payload = Array.Empty<byte>();
        var request = CherryCodec.BuildRequest(block, length);
        if (!handle.WriteOutput(request, TimeSpan.FromMilliseconds(500))) return false;

        var deadline = DateTime.UtcNow + ReplyWindow;
        while (DateTime.UtcNow < deadline)
        {
            var input = handle.ReadInput(TimeSpan.FromMilliseconds(300));
            if (input == null) continue;
            if (CherryCodec.TryParse(input, block, out payload)) return true;
            if (Log.IsEnabled(LogLevel.Debug)) Log.Debug($"CHERRY 无关帧: {Log.Hex(input)}");
        }
        return false;
    }

    /// <summary>应答窗口 1.2 秒：实机往返 &lt;50ms，留足键盘唤醒时的重传。</summary>
    private static readonly TimeSpan ReplyWindow = TimeSpan.FromMilliseconds(1200);

    private static string LogKey(HidDeviceHandle handle) => handle.Info.DevicePath;

    /// <summary>充电位只在插线时置位，没置位就是放电；协议里没有"满电"状态，100% 也照实报充电或放电。</summary>
    private static ChargingState ToChargingState(bool charging) =>
        charging ? ChargingState.Charging : ChargingState.Discharging;
}
