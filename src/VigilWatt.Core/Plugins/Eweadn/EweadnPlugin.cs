using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Eweadn;

/// <summary>
/// 功 能： 前行者 (EWEADN) 2.4G 外设插件——X87 键盘发 33 字节查询帧，S9 鼠标走 feature 0x03 邮箱读写。
/// 2026/10/18 13:40:00 Admin
/// 2026/10/21 由 EweadnKeyboardPlugin / EweadnMousePlugin 合并为单插件，品牌只占一行开关（AGENTS.md §9）。
/// 两套帧的实测依据见 docs/hardware/brands/eweadn.md §1.1 / §1.2。
/// </summary>
public sealed class EweadnPlugin : BatteryPluginBase
{
    /// <summary>插件开关的键，与 <c>AppSettings.PluginEnabled</c> 对应（插件开关即写入授权）。</summary>
    public const string SettingsKey = "Eweadn";

    /// <summary>X87 键盘的厂商 ID（官方 config.xml 的 2.4G 模式）。</summary>
    private const ushort KeyboardVid = 0x0C45;

    private const ushort KeyboardPid = 0xFEFE;

    /// <summary>键盘厂商通道：官方只认 MI_03，实测 caps 为 0xFF60/0x0061。</summary>
    private const ushort KeyboardUsagePage = 0xFF60;

    private const ushort KeyboardUsage = 0x0061;

    /// <summary>S9 / S9 Pro 鼠标的厂商 ID。</summary>
    private const ushort MouseVid = 0x372E;

    private const ushort MousePid = 0x1026;

    /// <summary>鼠标厂商通道：官方 by.json 只认这一对，实测 IF2 命中。</summary>
    private const ushort MouseUsagePage = 0xFF00;

    private const ushort MouseUsage = 0x0001;

    /// <summary>协议名（电量通道本身；在线/离线由 IsOnline + 电量是否为空表达）。</summary>
    private const string ProtocolKeyboard = "eweadn-x87-20";

    private const string ProtocolMouse = "eweadn-s9-feature03";

    public override string Name => SettingsKey;

    public override string DisplayName => "前行者";

    public override string DisplayNameEn => "EWEADN";

    /// <summary>键盘与鼠标图标不同，声明 Unknown 后由 <see cref="ResolveKind"/> 按协议给（罗技、迈从同例）。</summary>
    public override DeviceKind Kind => DeviceKind.Unknown;

    /// <summary>键盘休眠时查询无应答，只探一次会永久漏掉；鼠标搭同一节拍重探，代价是每分钟多一次 feature 读。</summary>
    public override bool SupportsDiscoveryProbe => true;

    /// <summary>12 秒 &lt; 15 秒发现节拍，保证每拍都能重探（基类要求冷却小于节拍）。</summary>
    protected override TimeSpan ProbeCooldown => TimeSpan.FromSeconds(12);

    /// <summary>休眠无应答是形态不是失联：两套协议都靠连续失败 + 熔断表达离线，不加沉默窗口。</summary>
    protected override OfflinePolicy Offline { get; } = new()
    {
        Silence = TimeSpan.Zero,
        CountPollFailures = false,
    };

    /// <summary>两套接口指纹：键盘靠输出报文长度分 MI_03/MI_04，鼠标只认带 feature 0x03 的厂商集合。</summary>
    protected override bool IsCandidate(HidDeviceInfo info)
    {
        if (info.VendorId == KeyboardVid && info.ProductId == KeyboardPid
            && info.UsagePage == KeyboardUsagePage && info.Usage == KeyboardUsage)
            return info.OutputReportLength == EweadnKeyboardCodec.FrameLength;

        return info.VendorId == MouseVid && info.ProductId == MousePid
            && info.UsagePage == MouseUsagePage && info.Usage == MouseUsage;
    }

    /// <summary>探测按 VID 分流；收不到应答返回 null 进冷却。</summary>
    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
    {
        if (handle.Info.VendorId == KeyboardVid)
        {
            var percent = QueryKeyboard(handle, TimeSpan.FromSeconds(2));
            if (percent == null) return null;
            // 官方驱动把充电位固定传 0（不做充电判定），这里跟随官方，充电态交给收尾规则。
            return (ProtocolKeyboard, percent, ChargingStateMap.FromChargingFlag(false, percent.Value));
        }

        var read = QueryMouse(handle);
        if (read == null) return null;
        return (ProtocolMouse, read.Value.Percent,
            ChargingStateMap.FromChargingFlag(read.Value.Charging, read.Value.Percent));
    }

    /// <summary>轮询同探测分流；写入被开关或熔断挡住时返回 null（不再写）。</summary>
    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev)
    {
        if (!CanWrite(dev)) return null;

        if (dev.Handle.Info.VendorId == KeyboardVid)
        {
            var percent = QueryKeyboard(dev.Handle, TimeSpan.FromSeconds(1.5));
            if (percent == null) return null;
            return (percent, ChargingStateMap.FromChargingFlag(false, percent.Value));
        }

        var read = QueryMouse(dev.Handle);
        if (read == null) return null;
        return (read.Value.Percent, ChargingStateMap.FromChargingFlag(read.Value.Charging, read.Value.Percent));
    }

    /// <summary>图标按协议定：X87 是键盘，S9 是鼠标。</summary>
    protected override DeviceKind ResolveKind(string protocol) => protocol switch
    {
        ProtocolMouse => DeviceKind.Mouse,
        _ => DeviceKind.Keyboard,
    };

    /// <summary>显示名按 VID 定：键盘产品串是「2.4G Dongle」，鼠标串带厂商前缀，都不适合直接展示。</summary>
    protected override string BuildDisplayName(HidDeviceInfo info)
    {
        if (info.VendorId == KeyboardVid) return "前行者 X87";

        var product = info.Product;
        if (string.IsNullOrWhiteSpace(product)) return "前行者 S9";
        var name = product.Replace("EWEADN", "", StringComparison.OrdinalIgnoreCase).Trim();
        return string.IsNullOrEmpty(name) ? "前行者 S9" : $"前行者 {name}";
    }

    /// <summary>
    /// X87：写入长度必须等于接口声明的输出长度（33 = MI_03 的 caps），
    /// 且该接口走 overlapped 异步 IO，同步读会永久阻塞。
    /// </summary>
    private static int? QueryKeyboard(HidDeviceHandle handle, TimeSpan window)
    {
        int length = handle.Info.OutputReportLength;
        if (length < EweadnKeyboardCodec.FrameLength)
        {
            Log.Info($"前行者键盘 输出报文长度异常: {length}，跳过: {handle.Info.DevicePath}");
            return null;
        }

        var frame = EweadnKeyboardCodec.BuildQuery();
        if (!handle.WriteOutput(frame, TimeSpan.FromMilliseconds(500)))
        {
            Log.Info($"前行者键盘 查询写失败: {handle.Info.DevicePath}");
            return null;
        }

        var deadline = DateTime.UtcNow + window;
        int logged = 0;
        while (DateTime.UtcNow < deadline)
        {
            var input = handle.ReadInput(TimeSpan.FromMilliseconds(300));
            if (input == null) continue;

            if (logged++ == 0)
                Log.Debug($"前行者键盘 收到报文: {Log.Hex(input)}");

            var percent = EweadnKeyboardCodec.ParseBattery(input);
            if (percent != null) return percent;
        }
        return null;
    }

    /// <summary>
    /// S9：该通道是「先写 feature、再读 feature」的邮箱式交互，两步缺一不可（实测写后读到 16 字节应答）。
    /// </summary>
    private static (int Percent, bool Charging)? QueryMouse(HidDeviceHandle handle)
    {
        if (handle.Info.FeatureReportLength < EweadnMouseCodec.FrameLength + 1)
        {
            Log.Info($"前行者鼠标 feature 报文长度异常: {handle.Info.FeatureReportLength}，跳过: {handle.Info.DevicePath}");
            return null;
        }

        var frame = EweadnMouseCodec.BuildBasicsQuery();
        if (!handle.SetFeature(EweadnMouseCodec.FeatureReportId, frame))
        {
            Log.Info($"前行者鼠标 查询写失败: {handle.Info.DevicePath}");
            return null;
        }

        // 设备侧处理需要几毫秒；官方 Hub 也留了延迟再读（transport delay）。
        Thread.Sleep(20);
        var response = handle.GetFeature(EweadnMouseCodec.FeatureReportId);
        if (response == null)
        {
            Log.Info($"前行者鼠标 读 feature 失败: {handle.Info.DevicePath}");
            return null;
        }

        var parsed = EweadnMouseCodec.ParseBasics(response);
        if (parsed == null)
        {
            Log.Debug($"前行者鼠标 响应无法解析: {Log.Hex(response)}");
            return null;
        }
        return parsed;
    }
}
