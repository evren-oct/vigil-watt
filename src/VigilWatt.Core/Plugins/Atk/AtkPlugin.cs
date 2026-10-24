using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Atk;

/// <summary>
/// 功 能： 艾泰克 (ATK) 2.4G 插件，一套开关管鼠标与键盘两条链路。
/// 2026/10/20 10:20:00 MOSS
/// 帧与判据见 <see cref="AtkCodec"/> / <see cref="AtkViaCodec"/>，验证范围见 docs/hardware/atk.md。
/// </summary>
public sealed class AtkPlugin : BatteryPluginBase
{
    /// <summary>插件开关的键，与 <c>AppSettings.PluginEnabled</c> 对应（插件开关即写入授权）。</summary>
    private const string SettingsKey = "Atk";

    public override string Name => SettingsKey;
    public override string DisplayName => "艾泰克";
    public override string DisplayNameEn => "ATK";

    /// <summary>一套插件管鼠标和键盘，声明 Unknown，具体图标由 <see cref="ResolveKind"/> 按链路给。</summary>
    public override DeviceKind Kind => DeviceKind.Unknown;

    /// <summary>鼠标链路：16 字节 op 帧（<see cref="AtkCodec"/>），page 0xFF02 / report 0x08。</summary>
    private const string ProtocolMouseOp = "atk-mouse-op04";

    /// <summary>键盘链路：VIA 读键盘值（<see cref="AtkViaCodec"/>），page 0xFF60 / report 0x00。</summary>
    private const string ProtocolKeyboardVia = "atk-keyboard-via";

    /// <summary>
    /// 离线判定：ATK 协议里没有"连接状态"字段，失败计数是唯一物理依据；
    /// 读数靠主动查询、设备不主动推帧，沉默没有信息量，故 Silence = 0。
    /// </summary>
    protected override OfflinePolicy Offline { get; } = new()
    {
        Silence = TimeSpan.Zero,
        CountPollFailures = false,
    };

    /// <summary>探测要发一条查询帧，不能占用 <see cref="SupportsFastDiscovery"/> 的纯被动名额。</summary>
    public override bool SupportsDiscoveryProbe => true;

    /// <summary>10 秒 &lt; 15 秒发现节拍，保证每一拍都允许重探；认领成功后零写入。</summary>
    protected override TimeSpan ProbeCooldown => TimeSpan.FromSeconds(10);

    /// <summary>
    /// 异常读数持续多久采信。ATK op 帧自带校验和、VIA 响应自带命令回显，
    /// 不会有"读成随机字节"的伪信号；基类默认 5 分钟对"鼠标充到 100%"这种真实跳变太慢。
    /// </summary>
    protected override TimeSpan AnomalyPersistWindow => TimeSpan.FromMinutes(1);

    protected override bool IsCandidate(HidDeviceInfo info)
        => AtkViaCodec.IsSupportedInterface(info.VendorId, info.UsagePage,
               info.InputReportLength, info.OutputReportLength, info.OutputReportId)
           || AtkCodec.IsSupportedInterface(info.VendorId, info.UsagePage,
               info.InputReportLength, info.OutputReportLength, info.OutputReportId);

    protected override DeviceKind ResolveKind(string protocol)
        => protocol switch
        {
            ProtocolMouseOp => DeviceKind.Mouse,
            ProtocolKeyboardVia => DeviceKind.Keyboard,
            _ => DeviceKind.Unknown,
        };

    /// <summary>
    /// 显示名按 PID 定型号：产品串是泛化名，且 11E4 的产品串里不含 "mouse"（"ATK F1 ULTIMATE 2.0"），
    /// 靠字符串判断会把它认成键盘。
    /// </summary>
    protected override string BuildDisplayName(HidDeviceInfo info)
        => KnownModelByPid.TryGetValue(info.ProductId, out var model)
            ? model
            : string.IsNullOrEmpty(info.Product) ? $"ATK ({info.VidPid})" : info.Product;

    /// <summary>已验证型号表：键盘只到 1152，其余 ATK 接收器都按鼠标算。</summary>
    private static readonly Dictionary<ushort, string> KnownModelByPid = new()
    {
        [0x1152] = "ATK A98 Pro",
        [0x11D9] = "ATK F1 Ultimate 2.0",
        [0x11E4] = "ATK F1 Ultimate 2.0",
    };

    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
    {
        if (!IsCandidate(handle.Info)) return null;

        // 键盘链路：page 0xFF60 走 VIA。必须排在鼠标前面——同一接收器 VID 相同，
        // 靠 usage page 分流，反过来会把 VIA 查询帧发给鼠标。
        if (AtkViaCodec.IsSupportedInterface(handle.Info.VendorId, handle.Info.UsagePage,
                handle.Info.InputReportLength, handle.Info.OutputReportLength, handle.Info.OutputReportId))
        {
            var kb = QueryVia(handle);
            return kb.HasValue
                ? (ProtocolKeyboardVia, kb.Value.Percent,
                   kb.Value.Charging ? ChargingState.Charging : ChargingState.Discharging)
                : null;
        }

        var mouse = QueryMouse(handle);
        return mouse.HasValue ? (ProtocolMouseOp, mouse, ChargingState.Unknown) : null;
    }

    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev)
    {
        if (!CanWrite(dev)) return null;

        var read = dev.Protocol switch
        {
            ProtocolKeyboardVia => QueryVia(dev.Handle) is { } kb
                ? (kb.Percent, kb.Charging ? ChargingState.Charging : ChargingState.Discharging)
                : ((int? Percent, ChargingState Charging)?)null,
            ProtocolMouseOp => QueryMouse(dev.Handle) is { } pct
                ? (pct, ChargingState.Unknown)
                : ((int? Percent, ChargingState Charging)?)null,
            _ => null,
        };

        // 自己报账：本插件 CountPollFailures = false（null 有歧义），基类不替我们数。
        // 鼠标失败**不报账**：2.4G 鼠标空闲即睡，查询无应答是常态；报账会触发退避（30 秒→5 分钟）
        // 把读数冻住，用户看到的就是"刷新了也不更新"（本机实测过）。
        if (read.HasValue) ReportPollAnswered(dev);
        else if (dev.Protocol == ProtocolKeyboardVia) ReportPollFailure(dev);
        return read;
    }

    /// <summary>鼠标查询：发一条 op 帧，1 秒内解析响应。</summary>
    private static int? QueryMouse(HidDeviceHandle handle)
    {
        if (!IsWritesEnabled(SettingsKey)) return null;
        if (!handle.WriteOutput(AtkCodec.CreateQuery(), TimeSpan.FromMilliseconds(500))) return null;

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(1);
        while (DateTime.UtcNow < deadline)
        {
            var input = handle.ReadInput(TimeSpan.FromMilliseconds(300));
            if (input == null) continue;
            if (!AtkCodec.TryParse(input, out int percent))
            {
                if (Log.IsEnabled(LogLevel.Debug)) Log.Debug($"ATK 鼠标忽略非电量响应: {Log.Hex(input)}");
                continue;
            }
            Log.Info($"ATK 鼠标电量查询: {percent}% 原始={Log.Hex(input)}");
            return percent;
        }
        Log.Once(LogLevel.Debug, $"atk-mouse-silent:{handle.Info.DevicePath}",
            $"ATK 鼠标查询无响应（多半在睡，恢复应答后会再记一条）: {handle.Info.DevicePath}");
        return null;
    }

    /// <summary>键盘查询：发一条 VIA 读值帧，1 秒内解析响应。</summary>
    private static (int Percent, bool Charging)? QueryVia(HidDeviceHandle handle)
    {
        if (!IsWritesEnabled(SettingsKey)) return null;
        var query = AtkViaCodec.CreateQuery(handle.Info.OutputReportLength);
        if (!handle.WriteOutput(query, TimeSpan.FromMilliseconds(500))) return null;

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(1);
        while (DateTime.UtcNow < deadline)
        {
            var input = handle.ReadInput(TimeSpan.FromMilliseconds(300));
            if (input == null) continue;
            if (!AtkViaCodec.TryParse(input, out int percent, out bool charging))
            {
                if (Log.IsEnabled(LogLevel.Debug)) Log.Debug($"ATK 键盘忽略非电量响应: {Log.Hex(input)}");
                continue;
            }
            Log.Info($"ATK 键盘 VIA 电量查询: {percent}% 充电={charging} 原始={Log.Hex(input)}");
            return (percent, charging);
        }
        Log.Once(LogLevel.Debug, $"atk-kb-silent:{handle.Info.DevicePath}",
            $"ATK 键盘查询无响应（多半在睡，恢复应答后会再记一条）: {handle.Info.DevicePath}");
        return null;
    }
}
