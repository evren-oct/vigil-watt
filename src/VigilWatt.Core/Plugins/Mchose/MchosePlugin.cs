using System.Collections.Concurrent;
using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Protocol;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Mchose;

/// <summary>
/// 功 能： 迈从 (MCHOSE) 2.4G 插件。
/// 2026/10/06 15:53:36 xliu
/// 帧结构见 <see cref="MchoseV1Codec"/> 与 docs/hardware/mchose.md §1。
/// </summary>
public sealed class MchosePlugin : BatteryPluginBase
{
    /// <summary>插件开关的键，与 <c>AppSettings.PluginEnabled</c> 对应（插件开关即写入授权）。</summary>
    private const string SettingsKey = "Mchose";

    public override string Name => SettingsKey;
    public override string DisplayName => "迈从";
    public override string DisplayNameEn => "MCHOSE";
    /// <summary>一个插件同时管键盘与鼠标（罗技先例）：声明 Unknown，具体图标由 <see cref="ResolveKind"/> 按协议给。</summary>
    public override DeviceKind Kind => DeviceKind.Unknown;

    /// <summary>形态固定的协议（Gmouse / V9 / X9）走解释器；有状态的四套仍在下面用代码实现。</summary>
    private static ProtocolEngine Engine => MchoseSpecs.Engine;

    /// <summary>键盘 V1 会话协议（report 0x13）。</summary>
    private const string ProtocolKeyboardV1 = "keyboard-v1";

    /// <summary>鼠标 V2 世代：纯被动 E2 状态帧 + 0x11/0x06 触发上报。</summary>
    private const string ProtocolMouseV2 = "mouse-v2";

    /// <summary>0x4D 协议（原 qhw 与 mouse-k7 合并）：帧构造与解析全在 <see cref="Mchose0x4DCodec"/>。</summary>
    private const string Protocol0x4D = Mchose0x4DCodec.ProtocolName;

    /// <summary>Gmouse 世代鼠标（G3 V2）：65 字节主动查询帧（55 30 A5…），判据与帧见 <see cref="MchoseGmouseCodec"/>。</summary>
    private const string ProtocolGmouse = "gmouse";

    /// <summary>V9 Pro 耳机：64 字节固定查询帧（帧结构与判据见 <see cref="MchoseV9ProCodec"/>）。同品牌另一条链路，与键鼠判据完全独立。</summary>
    private const string ProtocolHeadsetV9 = "headset-v9";

    /// <summary>X9 耳机：与 V9 Pro 同帧结构（判据见 <see cref="MchoseX9Codec"/>），VID 却与键鼠同号段。</summary>
    private const string ProtocolHeadsetX9 = "headset-x9";

    /// <summary>CZ 分块读（官方 web 驱动对通用 dongle 用的通道，帧在 mchose.protocols.json）。</summary>
    private const string ProtocolCzBlock = "cz-block";

    /// <summary>探测"只拿到被动推送"的内部标记——只在 <see cref="Claim0x4D"/> 内部用，不进设备协议名。</summary>
    private const string ProtocolPushOnly = "push-only";

    /// <summary>无效电量下界（0/1 是设备休眠/关机的占位值），单一来源在 <see cref="MchoseV1Codec"/>。</summary>
    private const byte InvalidPercentFloor = MchoseV1Codec.InvalidPercentFloor;

    /// <summary>认领后后台听 0A/CZ 推送，插拔电不必等 60 秒主动查询。</summary>
    protected override bool SupportsPassiveListen => true;

    /// <summary>
    /// 本插件的离线判定策略（见 <see cref="OfflinePolicy"/>）。
    ///
    /// <b>Silence = 0</b>：0A 推送很稀疏（仅状态变化时才有），沉默毫无信息量。
    /// <b>CountPollFailures = false</b>：轮询**确实会发帧**，但 <c>PollDevice</c> 返回 null 有三种可能
    /// （真的没应答 / 用户关了写入许可 / 熔断停写中），基类分不出来，故改由插件在发过帧的分支
    /// 自己报账（<c>ReportPollFailure</c>）。
    /// 迈从键盘协议里没有"连接状态"字段（不像雷柏有 bit7、北通有占位帧），所以失败计数是它
    /// 唯一能判离线的物理依据。**0x4D 通道例外**：`0x0900` 的 `payload[10]` 就是连接状态，
    /// 但它只用来刷徽标（M2-3 起），**仍不拿它当离线判据**——设备在睡时 dongle 照样回这一帧（mchose.md §3.3、§3.5）。
    /// </summary>
    protected override OfflinePolicy Offline { get; } = new()
    {
        Silence = TimeSpan.Zero,
        CountPollFailures = false,
    };

    /// <summary>
    /// 探测要写帧（会话帧 + 最多 2 条电量查询），不能走 <see cref="SupportsFastDiscovery"/>；
    /// 但**不参与 15 秒发现节拍**的代价是键盘唤醒后最坏要等 60 秒主轮询才被认领。
    /// 故改用本属性参与 15 秒节拍，写入频率由 <see cref="ProbeCooldown"/> 兜住。
    /// </summary>
    public override bool SupportsDiscoveryProbe => true;

    /// <summary>
    /// **10 秒 &lt; 15 秒发现节拍**，保证每一拍都允许重探。探测失败发生在拍点之后约 3.7 秒
    /// （会话 1.2s + 两次查询各 1.2s），若冷却 ≥ 15 秒，下一拍会因"才过了 11.3 秒"被判冷却未过
    /// 而白白跳过，实际重试间隔翻倍成 30 秒。
    /// 写入代价：只有设备不响应时才重探，每次约 3 帧；认领成功后 <c>Attach</c> 对已知设备直接返回，零写入。
    /// </summary>
    protected override TimeSpan ProbeCooldown => TimeSpan.FromSeconds(10);

    protected override bool IsCandidate(HidDeviceInfo info)
    {
        // 判据全在分类器里：它是**正向 VID 白名单**（0x291D / 0x3837 / 0x41E4），别家 VID 必然判 None。
        // 原来这里逐个排除罗技/雷柏/北通/英菲克/HECATE/AULA，与白名单等价、纯冗余，且让本插件硬依赖 6 个别家命名空间。
        // 与 AULA 共用指纹的例外见 docs/hardware/aula.md §1.1。
        if (Classify(info) is var kind and not MchoseInterfaceKind.None)
            // A5 的 Feature 通道归 MchoseA5Plugin（同品牌另一个插件类），主插件不认领
            return kind != MchoseInterfaceKind.MouseA5;
        // 与 AULA 共用的 3554:FA09：分类器按 VID 白名单必然判 None，这里单独放行，归属交给 Probe 按 ACK 型号定
        return IsSharedWithAula(info);
    }

    /// <summary>把 <see cref="HidDeviceInfo"/> 摊成分类器要的字段，避免判据在两处各写一遍。</summary>
    private static MchoseInterfaceKind Classify(HidDeviceInfo info)
        => MchoseInterfaceClassifier.Classify(info.VendorId, info.ProductId, info.DevicePath,
            info.UsagePage, info.Usage, info.InputReportLength, info.OutputReportLength,
            info.FeatureReportLength, info.OutputReportId);

    /// <summary>G75 与 AULA F99 Pro 的接口指纹完全一致（3554:FA09 / 0xFF02:0x0002 / 20 字节），以会话 ACK 的型号密码定归属。</summary>
    private static bool IsSharedWithAula(HidDeviceInfo info)
        => info.VendorId == Aula.AulaPlugin.AulaVid && info.ProductId == Aula.AulaPlugin.F99ProPid
           && info.UsagePage == 0xFF02 && info.Usage == 0x0002
           && info.InputReportLength >= MchoseV1Codec.FrameLength
           && info.OutputReportLength >= MchoseV1Codec.FrameLength;

    protected override DeviceKind ResolveKind(string protocol)
        => protocol switch
        {
            ProtocolMouseV2 => DeviceKind.Mouse,
            Protocol0x4D => DeviceKind.Mouse,
            ProtocolGmouse => DeviceKind.Mouse,
            ProtocolHeadsetV9 or ProtocolHeadsetX9 => DeviceKind.Headset,
            _ => DeviceKind.Keyboard,
        };

    /// <summary>
    /// 认领落地用探测窗口那一帧的连接方式（否则徽标先按来源显示成 2.4G）。
    /// 只影响徽标：不参与认领判据，也不参与在线判定。
    /// </summary>
    protected override ConnectionKind ConnectionOfProbe(HidDeviceInfo info)
        => KnownProbeConnection.TryGetValue(info.DevicePath, out var connection)
            ? connection
            : ConnectionKind.Unknown;

    /// <summary>
    /// 能力只来自目录里写过的传输通道（当前仅 `3837:1018` 有真机读数）；其余一律 Unverified，
    /// 不从 0x4D 协议族外推。纯配置查询，不开句柄不发帧。
    /// </summary>
    protected override CapabilitySet CapabilitiesOf(HidDeviceInfo info)
    {
        var transport = MchoseDeviceCatalog.CapabilitiesOf(info);
        return transport.IsEmpty ? Engine.CapabilitiesOf(info) : transport;
    }

    /// <summary>显示名：接收器产品名常是泛化的「MCHOSE 2.4G Wireless」，识别出型号时用型号替换；
    /// 加「迈从」前缀方便设备列表一眼认品牌。型号来源见 <see cref="KnownModelByPath"/>。</summary>
    protected override string BuildDisplayName(HidDeviceInfo info)
    {
        // 耳机接收器是独立 VID/PID，只对应已验证的 V9 Pro；协议里没有型号回执，型号名只能写死
        if (info.VendorId == MchoseX9Codec.VendorId && info.ProductId == MchoseX9Codec.ProductId)
            return "迈从 X9";
        if (info.VendorId == MchoseV9ProCodec.VendorId && info.ProductId == MchoseV9ProCodec.ProductId)
            return "迈从 V9 Pro";

        var product = string.IsNullOrEmpty(info.Product) ? "" : info.Product.Replace("MCHOSE", "").Trim();
        var model = KnownModelByPath.TryGetValue(info.DevicePath, out var m) ? m : "";
        // 0x4D 设备自报的配对身份最可靠（USB 枚举到的只是 dongle），优先于目录里按 dongle PID 查到的空值
        if (model.Length == 0 && KnownIdentityByPath.TryGetValue(info.DevicePath, out var identity))
            model = identity.Model;
        // CZ 族没有会话密码，型号名只能来自设备目录；目录也没有就退回产品名
        if (model.Length == 0 && MchoseDeviceCatalog.TryGet(info.VendorId, info.ProductId, out var catalogEntry))
            model = catalogEntry.Model;
        var name = model.Length > 0 && product.Contains("2.4G") ? model
                 : product.Length > 0 ? product
                 : model;
        return string.IsNullOrEmpty(name) ? $"迈从 ({info.VidPid})" : $"迈从 {name}";
    }

    /// <summary>
    /// 接口路径 → 已判定协议（进程内学习）。**只在探测成功后写入，失败不抹掉**：
    /// 同一条路径永远是同一个型号，而探测失败最常见的原因是"键盘在睡"，说明不了协议变了。
    /// </summary>
    private readonly ConcurrentDictionary<string, string> KnownProtocolByPath = new();

    /// <summary>接口路径 → 会话 ACK 密码段识别出的型号名（<see cref="BuildDisplayName"/> 用；识别不出就没有条目）。</summary>
    private readonly ConcurrentDictionary<string, string> KnownModelByPath = new();

    /// <summary>接口路径 → 0x4D 设备自报的身份（USB 与协议身份分开存，见 <see cref="MchoseDeviceIdentity"/>）。</summary>
    private readonly ConcurrentDictionary<string, MchoseDeviceIdentity> KnownIdentityByPath = new();

    /// <summary>接口路径 → 探测窗口里 `0x0900` 读到的连接方式，只供认领落地用；无效应答不写入。</summary>
    private readonly ConcurrentDictionary<string, ConnectionKind> KnownProbeConnection = new();

    /// <summary>
    /// 日志去重 key = <c>路径|用途</c>。配合 <see cref="Log.Once"/> / <see cref="Log.ResetOnce"/>
    /// 表达"**状态变化才记**"：发现节拍每 15 秒重探一次，设备睡着时逐次记录会把 app.log 刷满且无新信息；
    /// 恢复正常时复位标记，下次再坏又能看到一条。
    /// </summary>
    private static string LogKey(HidDeviceHandle handle, string what) => $"{handle.Info.DevicePath}|{what}";

    /// <summary>0x4D 认领：协议名统一记 <c>mchose-0x4d</c>，"只能被动听"另记在 <see cref="_pushOnly"/>。</summary>
    private (string Protocol, int? Percent, ChargingState Charging)? Claim0x4D(
        string path, (string Protocol, int? Percent, ChargingState Charging)? read)
    {
        if (read is not { } r) return null;
        KnownProtocolByPath[path] = Protocol0x4D;
        if (r.Protocol == ProtocolPushOnly) _pushOnly[path] = true;
        else _pushOnly.TryRemove(path, out _);
        return (Protocol0x4D, r.Percent, r.Charging);
    }

    /// <summary>协议层拿不到 <see cref="TrackedDevice"/>（protected 嵌套类型），只能摊成值。</summary>
    private static DeviceRef RefOf(TrackedDevice dev)
        => new(dev.Status.Id, dev.Info.DevicePath, dev.Info.VendorId, dev.Info.ProductId);

    /// <summary>
    /// 每轮扫描前清掉已经不在的接口的探测缓存。**空扫描当枚举失败**，不动任何缓存——
    /// 否则一次 API 抖动会让所有设备重探、把写入量放大一倍（与基类的口径一致）。
    /// </summary>
    public override async Task<IReadOnlyList<DeviceStatus>> AttachAsync(
        IReadOnlyList<HidDeviceInfo> availableDevices, DeviceClaimRegistry claims)
    {
        if (availableDevices.Count > 0)
        {
            var alive = availableDevices.Select(d => d.DevicePath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            PruneKeys(KnownProtocolByPath, alive);
            PruneKeys(KnownModelByPath, alive);
            PruneIdentities(alive);
            PruneFlags(_pushOnly, alive);
            PruneConnections(alive);
            PruneFlags(PointerProbeAttempts, alive);
            Engine.Prune(availableDevices);
        }
        return await base.AttachAsync(availableDevices, claims).ConfigureAwait(false);
    }

    private static void PruneFlags<T>(ConcurrentDictionary<string, T> map, HashSet<string> alive)
    {
        foreach (var key in map.Keys.Where(k => !alive.Contains(k)).ToList())
            map.TryRemove(key, out _);
    }

    private static void PruneKeys(ConcurrentDictionary<string, string> map, HashSet<string> alive)
    {
        foreach (var key in map.Keys.Where(k => !alive.Contains(k)).ToList())
            map.TryRemove(key, out _);
    }

    private void PruneIdentities(HashSet<string> alive)
    {
        foreach (var key in KnownIdentityByPath.Keys.Where(k => !alive.Contains(k)).ToList())
            KnownIdentityByPath.TryRemove(key, out _);
    }

    private void PruneConnections(HashSet<string> alive)
    {
        foreach (var key in KnownProbeConnection.Keys.Where(k => !alive.Contains(k)).ToList())
            KnownProbeConnection.TryRemove(key, out _);
    }

    /// <summary>插件被禁用/卸载时清空全部按设备保存的状态（缓存是实例字段，但插件可能被反复启用）。</summary>
    public override void Detach()
    {
        KnownProtocolByPath.Clear();
        KnownModelByPath.Clear();
        KnownIdentityByPath.Clear();
        KnownProbeConnection.Clear();
        PointerProbeAttempts.Clear();
        _pushOnly.Clear();
        Engine.Reset();
        base.Detach();
    }

    private const string LogKey0x4DNoPermit = "0x4d-no-permit"; // "没开写入许可，跳过主动查询"
    private const string LogKey0x4DSilent = "0x4d-silent";      // "写成功但没有有效读数，交还后续判据"
    private const string LogKeyKbV1Silent = "kbv1-silent";    // "keyboard-v1 问了两次都不应答"
    private const string LogKeyKbV1ModelUnknown = "kbv1-model-unknown"; // "共用指纹但型号不在迈从表，交给 AULA"
    private const string LogKeyMouseSilent = "mouse-silent";  // "鼠标探测没等到 E2 帧"
    private const string LogKeyMousePokeFail = "mouse-poke-fail"; // "触发帧写失败（接口无输出通道）"

    private const string LogKey0x4DAsleep = "0x4d-asleep";  // "只回 1 字节 0xFF，鼠标多半在睡"

    /// <summary>协议探测：共用指纹（AULA/G75）→ 鼠标 V2（被动等 E2）→ 官方 QHW（新世代）→ 键盘 V1 会话协议（G87 / G87 V2）。</summary>
    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
    {
        // consumer/system 等小接口没有可用的输出通道，直接排除（避免握手帧越界）
        if (handle.Info.OutputReportLength < 20 || handle.Info.InputReportLength < 20) return null;

        var path = handle.Info.DevicePath;

        // 共用指纹：分类器不认识这个 VID，绝不能往下走 QHW（会往键盘接口写 0x4D），只能走 V1 会话
        if (IsSharedWithAula(handle.Info))
        {
            var shared = TryKeyboardV1(handle, requireKnownModel: true);
            if (shared == null) return null;
            KnownProtocolByPath[path] = ProtocolKeyboardV1;
            return (ProtocolKeyboardV1, shared.Value.Percent, shared.Value.Charging);
        }

        var kind = MchoseInterfaceClassifier.Classify(handle.Info.VendorId, handle.Info.ProductId, path,
            handle.Info.UsagePage, handle.Info.Usage, handle.Info.InputReportLength,
            handle.Info.OutputReportLength, handle.Info.FeatureReportLength, handle.Info.OutputReportId);

        // A5 的 Feature 通道归 MchoseA5Plugin；主插件在这里就退出，绝不往上写 0x4D 帧
        if (kind == MchoseInterfaceKind.MouseA5) return null;

        // 耳机与 Gmouse 的帧模板、窗口、重试次数都在 JSON 里，交给解释器；抢在下面「≥60 字节只走鼠标」之前。
        if (kind is MchoseInterfaceKind.HeadsetV9 or MchoseInterfaceKind.HeadsetX9 or MchoseInterfaceKind.Gmouse)
        {
            var spec = TrySpec(handle);
            if (spec != null) KnownProtocolByPath[path] = spec.Value.Protocol;
            return spec;
        }

        // 0x4D 通道同样是 64 字节，但它说的是 0x4D 帧，不是 V2 世代那套 E2 状态帧——
        // 落到下面等于白等两轮再发一条无意义的触发帧。
        // 已知通道（精确指纹或目录登记的通用 dongle）**写成功即认领**：鼠标睡着时 dongle 仍回 1 字节 FF，
        // 那是"有应答无读数"，不该因此丢掉设备（原 K7 行为，保留）。
        if (kind == MchoseInterfaceKind.Mouse0x4D)
        {
            return Claim0x4D(path, Try0x4D(handle, requireReading: false));
        }

        // 目录命中 QHW 的设备（CZ 族键盘、共用 dongle）：直接走 0x4D，不必先等 E2 帧。
        // 这条通道的把握不如上一条，**必须真的解析出读数才认领**（原 QHW 行为，保留）。
        if (kind == MchoseInterfaceKind.Qhw)
        {
            return Claim0x4D(path, Try0x4D(handle, requireReading: true));
        }

        // ≥60 的接口只可能是鼠标接收器：它不是 0x13 会话通道，在这条通道上发键盘帧没有意义。
        // **必须先试 0x4D**：它与 V2 同为 ≥60 字节、接口形态区分不了，而 0x4D 写失败会立即返回、
        // 不会拖慢 E2 路径；反过来先等 E2 会把 0x4D 设备永久卡在这条分支（2026-11-01 修）。
        if (kind == MchoseInterfaceKind.MouseV2)
        {
            var first = Claim0x4D(path, Try0x4D(handle, requireReading: true));
            if (first != null) return first;

            // 官方通用 dongle（0x3837:0x3033）对 0x4D 静默，走的是 CZ 分块读。只有接口指纹命中 JSON 规格
            // 才会发帧，其余接收器在这里拿不到规格、一个字节都不写。见 hardware/mchose.md §2.4。
            if (TrySpec(handle) is { } cz)
            {
                KnownProtocolByPath[path] = cz.Protocol;
                return cz;
            }

            var mouse = TryMouseV2(handle);
            if (mouse != null) KnownProtocolByPath[path] = ProtocolMouseV2;
            return mouse;
        }

        // 已知协议：只试对应那条通道，**失败就返回、不清记忆**。
        //
        // 2026-10-08 实修：旧代码在这里 `TryRemove` 后回落完整探测，代价是每 15 秒的发现节拍
        // 都白写一次 QHW 握手（本机 G87 不是 QHW 通道，必失败 → 每 15 秒一条 WARN 刷屏）
        // 再重试一遍 keyboard-v1，写入量翻倍。缓存只在**探测成功**时写入，本身就是可靠判据，
        // 没有理由因为"设备睡着了"把它丢掉；真要重置，重启应用即可（缓存是进程内的）。
        if (KnownProtocolByPath.TryGetValue(path, out var known))
            return TryKnownProtocol(handle, known);

        var first0x4D = Claim0x4D(path, Try0x4D(handle, requireReading: true));
        if (first0x4D != null) return first0x4D;

        var kbV1 = TryKeyboardV1(handle, IsSharedWithAula(handle.Info));
        if (kbV1.HasValue)
        {
            var (p, c) = kbV1.Value;
            KnownProtocolByPath[path] = ProtocolKeyboardV1;
            return (ProtocolKeyboardV1, p, c);
        }
        return null;
    }

    /// <summary>按已判定的协议只试那一条通道（避免每次探测都先空等 0x4D 窗口）。</summary>
    private (string Protocol, int? Percent, ChargingState Charging)? TryKnownProtocol(
        HidDeviceHandle handle, string known)
        => known switch
        {
            Protocol0x4D => Claim0x4D(handle.Info.DevicePath, Try0x4D(handle, requireReading: false)),
            ProtocolKeyboardV1 => TryKeyboardV1(handle, IsSharedWithAula(handle.Info)) is { } kb
                ? (ProtocolKeyboardV1, kb.Percent, kb.Charging)
                : null,
            ProtocolMouseV2 => TryMouseV2(handle),
            ProtocolGmouse or ProtocolHeadsetV9 or ProtocolHeadsetX9 or ProtocolCzBlock => TrySpec(handle),
            _ => null,
        };

    /// <summary>形态固定的协议交给解释器：帧模板、窗口、重试次数全在 JSON 里，这里只剩一次调用。</summary>
    private (string Protocol, int? Percent, ChargingState Charging)? TrySpec(HidDeviceHandle handle)
    {
        var reading = Engine.ProbeAsync(handle, IsWritesEnabled(SettingsKey)).GetAwaiter().GetResult();
        if (reading is not { } r || r.Protocol != ProtocolCzBlock) return reading;

        var (percent, charging) = MchoseSpecs.ResolveFull(r.Percent, r.Charging);
        return (r.Protocol, percent, charging);
    }

    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev)
    {
        // 熔断 / 未开写入 / 纯推送协议：读数交给后台被动监听。这几条都没发帧，故不计失败。
        // mouse-v2 的读数是接收器自发 E2 推送，这里发帧只会干扰鼠标。
        if (!CanWrite(dev) || _pushOnly.ContainsKey(dev.Info.DevicePath) || dev.Protocol == ProtocolMouseV2)
            return null;

        // 0x4D 的记账规则与其它协议不同——"只回 1 字节 0xFF"也算应答，故整段交给 Poll0x4D 自己数，
        // 不走下面那套"有读数算应答 / 无读数算失败"的通用规则（理由见该方法注释）。
        if (dev.Protocol == Protocol0x4D)
            return Poll0x4D(dev);

        // 形态固定的协议由解释器发帧与解析；"没发帧"与"发了没应答"必须分开，否则会被误记成失败
        if (dev.Protocol is ProtocolGmouse or ProtocolHeadsetV9 or ProtocolHeadsetX9 or ProtocolCzBlock)
        {
            var outcome = Engine.Poll(dev.Handle, RefOf(dev), dev.Protocol, IsWritesEnabled(SettingsKey));
            if (outcome.Attempt == PollAttempt.NotAttempted) return null;
            if (outcome.Attempt == PollAttempt.Answered) ReportPollAnswered(dev);
            else ReportPollFailure(dev);
            if (outcome.Attempt != PollAttempt.Answered) return null;

            return dev.Protocol == ProtocolCzBlock
                ? MchoseSpecs.ResolveFull(outcome.Percent, outcome.Charging)
                : (outcome.Percent, outcome.Charging);
        }

        var read = dev.Protocol switch
        {
            ProtocolKeyboardV1 => PollKeyboardV1(dev),
            _ => null,
        };

        // 发过帧才由自己报账：有应答 → 清零，无应答 → 记账（满 3 次标离线 + 熔断停写）。
        // 必须自己数——本插件把 CountPollFailures 置了 false（null 有歧义），基类不会替我们数。
        if (read.HasValue) ReportPollAnswered(dev);
        else ReportPollFailure(dev);

        return read;
    }

    /// <summary>
    /// 被动解析：keyboard-v1 的 0A 电量推送，或 QHW CZ 通道 FA FB 07 推送。
    /// 查询应答（4A / QHW getBattery）留给 PollDevice 的写读会话，这里不抢。
    /// </summary>
    protected override (int? Percent, ChargingState Charging, bool Offline)? TryParsePassiveInput(
        TrackedDevice dev, byte[] input)
    {
        if (dev.Protocol == ProtocolMouseV2)
        {
            var kind = MchoseMouseCodec.ParseStatus(input, out int pct, out bool chg);
            if (kind == MouseV2PushKind.Status)
                return (pct, chg ? ChargingState.Charging : ChargingState.Discharging, Offline: false);
            // 关机/休眠占位帧很常见，记 Debug 即可：每帧一条 WARN 会把日志刷满
            if (kind == MouseV2PushKind.StatusInvalid)
                Log.Debug($"迈从 鼠标 E2 帧电量越界（={pct}）: {Log.Hex(input)}");
            return null;
        }

        if (dev.Protocol == ProtocolKeyboardV1)
        {
            var push = MchoseV1Codec.ParsePush(input);
            if (push.Kind == V1PushKind.Battery)
                return (push.Percent, ParseKeyboardV1Charge(push.ChargeFlag, push.Percent), Offline: false);
            if (push.Kind == V1PushKind.StateNotify)
            {
                // payload 0x00/0x01 语义**至今未定论**（全天交替，且每次通知后设备仍能正常应答查询），
                // 绝不能当离线证据；但可以当**触发器**：让基类立刻补一次核实查询，把"键盘真关机了"
                // 的发现从分钟级压到秒级。只认 0x00——选哪个值只影响触发时机，不影响判定结果。
                if (push.Payload == MchoseV1Codec.StatePayloadZero)
                {
                    Log.Info($"迈从 被动收到设备状态通知 payload=0x00，触发一次核实查询: {Log.Hex(input)}");
                    RequestConfirm(dev);
                }
                else
                {
                    Log.Debug($"迈从 被动收到设备状态通知（payload=0x{push.Payload:X2}，仅记录）: {Log.Hex(input)}");
                }
            }
            return null;
        }

        if (dev.Protocol == Protocol0x4D)
        {
            // 设备自报的 0x0600 电源事件优先（带的是设备真状态）；CZ 的 FA FB 07 推送只是 dongle 侧兜底。
            if (Mchose0x4DCodec.TryParseStatusEvent(input, out var evt))
            {
                // 档位与电源是两类事件：先各自落地，纯档位事件不能因为缺电源字段被整条丢掉
                if (evt.DpiIndex is { } stage) ApplyDpiStage(dev, stage, input);
                if (evt.Percent is null && evt.ChargeStatus is null) return null;   // 纯档位事件不碰电量/充电

                // 事件一次只带一个字段（电量帧 / 充电帧是两条不同的帧），缺的那个沿用当前值，
                // 否则会把另一项冲成 Unknown。连历史电量都没有就没什么可报的，等下一帧。
                int? percent = evt.Percent ?? dev.Status.BatteryPercent;
                if (!percent.HasValue) return null;
                var charging = evt.ChargeStatus is { } raw
                    ? Mchose0x4DCodec.ToChargingState(raw, percent.Value)
                    : dev.Status.Charging;

                Log.Debug($"迈从 0x4D 状态事件: 电量={evt.Percent?.ToString() ?? "(未带)"} " +
                          $"充电={evt.ChargeStatus?.ToString() ?? "(未带)"} " +
                          $"档位={evt.DpiIndex?.ToString() ?? "(未带)"} 原始={Log.Hex(input)}");
                return (percent, charging, Offline: false);
            }

            var cz = ParseCzPush(input);
            if (cz.HasValue)
                return (cz.Value.Percent,
                    cz.Value.Charging ? ChargingState.Charging : ChargingState.Discharging,
                    Offline: false);

            return null;
        }

        return null;
    }

    /// <summary>只拿到被动推送的接口（按路径）：这类设备不发查询帧，读数交给后台监听。</summary>
    private readonly ConcurrentDictionary<string, bool> _pushOnly = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>等设备应答的窗口。官方 <c>waitCommand</c> 默认 3 s；真机 6.5 小时长跑没丢过帧，1.2 s 够用且不拖慢发现节拍。</summary>
    private static readonly TimeSpan ReplyWindow = TimeSpan.FromSeconds(1.2);

    /// <summary>
    /// 0x4D 探测/轮询的公共读写会话：发一条 <c>0x0900</c>，在窗口内收应答。
    /// <paramref name="requireReading"/> = false 时**写成功即算认领**（返回 null 读数），用于已知 0x4D 通道：
    /// 鼠标睡着时 dongle 仍回 1 字节 FF，那是"有应答无读数"，不该把设备丢掉。
    /// </summary>
    private (string Protocol, int? Percent, ChargingState Charging)? Try0x4D(
        HidDeviceHandle handle, bool requireReading)
    {
        // 输出通道装不下 64 字节帧：写进去只会越界，直接判"不是这条通道"
        if (handle.Info.OutputReportLength < Mchose0x4DCodec.FrameLength) return null;

        // 未授权写入时只能被动等推送（正常情况下走不到：插件开关即授权）；保留作纵深防御
        if (!IsWritesEnabled(SettingsKey))
        {
            var passive = ListenCzPush(handle, TimeSpan.FromSeconds(3));
            if (passive.HasValue)
            {
                var (pct0, chg0) = passive.Value;
                return (ProtocolPushOnly, pct0, chg0 ? ChargingState.Charging : ChargingState.Discharging);
            }
            Log.Once(LogLevel.Info, LogKey(handle, LogKey0x4DNoPermit),
                $"迈从 0x4D: 未开启写入许可，跳过主动查询: {handle.Info.DevicePath}");
            return null;
        }
        Log.ResetOnce(LogKey(handle, LogKey0x4DNoPermit));

        var query = Mchose0x4DCodec.BuildDeviceInfoQuery();
        bool wrote = false;
        byte[]? lastAnswer = null;

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            // quiet：非 0x4D 通道的描述符里没有 report 0x4D，写失败正是"不是这条通道"的判据
            if (!handle.WriteOutput(query, TimeSpan.FromMilliseconds(500), quiet: true))
            {
                Log.Debug($"迈从 0x4D 第{attempt}次写失败: {handle.Info.DevicePath}");
                return null;
            }
            wrote = true;

            var deadline = DateTime.UtcNow + ReplyWindow;
            while (DateTime.UtcNow < deadline)
            {
                var input = handle.ReadInput(TimeSpan.FromMilliseconds(250));
                if (input == null) continue;

                // 这颗 dongle 会把自己的 output 报告原样回显成一条输入帧；回显不是应答，必须跳过
                if (input.AsSpan().SequenceEqual(query)) continue;
                lastAnswer = input;

                if (Mchose0x4DCodec.TryParseDeviceInfo(input, out int percent, out byte charge, out byte connect))
                {
                    Log.ResetOnce(LogKey(handle, LogKey0x4DAsleep));
                    Log.Info($"迈从 0x4D 设备信息: 电量={percent} 充电状态={charge} 连接状态={connect} " +
                             $"原始={Log.Hex(input)}");
                    RememberIdentity(handle, input);
                    // 认领落地要用这一帧：否则徽标会先按来源显示成 2.4G，等第一条轮询才纠正
                    KnownProbeConnection[handle.Info.DevicePath] = Mchose0x4DCodec.ToConnectionKind(connect);
                    return (Protocol0x4D, percent, Mchose0x4DCodec.ToChargingState(charge, percent));
                }

                // dongle 侧兜底：CZ 电量推送（FA FB 07）也可能在这条通道上出现
                var push = ParseCzPush(input);
                if (push.HasValue)
                {
                    Log.Info($"迈从 0x4D 收到 CZ 电量推送: 电量={push.Value.Percent} 充电={push.Value.Charging} " +
                             $"原始={Log.Hex(input)}");
                    return (ProtocolPushOnly, push.Value.Percent,
                        push.Value.Charging ? ChargingState.Charging : ChargingState.Discharging);
                }
            }
            Thread.Sleep(150);
        }

        if (!wrote) return null;

        // 没读到有效应答：把握不足的通道要交还给后续判据（CZ 分块读 / E2），不能占着不放
        if (requireReading)
        {
            Log.Once(LogLevel.Debug, LogKey(handle, LogKey0x4DSilent),
                $"迈从 0x4D 无有效读数（可能是别的通道，回落到后续判据）: {handle.Info.DevicePath} " +
                $"最后应答={(lastAnswer == null ? "(无)" : Log.Hex(lastAnswer))}");
            return null;
        }

        Log.Once(LogLevel.Info, LogKey(handle, LogKey0x4DAsleep),
            $"迈从 0x4D 三次都没拿到设备信息（鼠标多半在睡，唤醒后会由 0x0600 事件补上；" +
            $"恢复应答后会再记一条）: {handle.Info.DevicePath} 最后应答={(lastAnswer == null ? "(无)" : Log.Hex(lastAnswer))}");
        return (Protocol0x4D, null, ChargingState.Unknown);
    }

    /// <summary>解析 CZ 通道电量推送 [0xFA, 0xFB, 0x07, 电量%, 充电标志]（官方 handleMessage 分支）。</summary>
    private static (int Percent, bool Charging)? ParseCzPush(byte[] input)
    {
        foreach (int off in new[] { 0, 1 })
        {
            if (input.Length < off + 5) continue;
            if (input[off] != 0xFA || input[off + 1] != 0xFB || input[off + 2] != 0x07) continue;
            byte pct = input[off + 3];
            byte chg = input[off + 4];
            if (pct <= InvalidPercentFloor || pct > 100 || chg > 2) return null;   // ≤1 = 休眠/关机占位帧，无效
            return (pct, chg == 1);
        }
        return null;
    }

    /// <summary>纯被动监听 CZ 电量推送，不写任何字节。</summary>
    private static (int Percent, bool Charging)? ListenCzPush(HidDeviceHandle handle, TimeSpan window)
    {
        var deadline = DateTime.UtcNow + window;
        while (DateTime.UtcNow < deadline)
        {
            var input = handle.ReadInput(TimeSpan.FromMilliseconds(300));
            if (input == null) continue;
            var push = ParseCzPush(input);
            if (push.HasValue)
            {
                Log.Info($"迈从 CZ 推送: 电量={push.Value.Percent} 充电={push.Value.Charging} 原始={Log.Hex(input)}");
                return push;
            }
        }
        return null;
    }

    /// <summary>
    /// 鼠标 V2 探测：先纯被动等 E2 状态帧（接收器约 10 秒推一次）；等不到才发一次触发帧
    /// （0x11/0x06，官方驱动同款）。触发帧构造见 <see cref="MchoseMouseCodec.BuildPokeFrame"/>。
    /// </summary>
    private (string Protocol, int? Percent, ChargingState Charging)? TryMouseV2(HidDeviceHandle handle)
    {
        if (!IsWritesEnabled(SettingsKey)) return null;

        var push = ListenE2(handle, TimeSpan.FromSeconds(3));
        if (!push.HasValue)
        {
            // 被动等不到才写：多数时候 E2 自己会来，写帧只是唤醒手段
            var poke = MchoseMouseCodec.BuildPokeFrame(handle.Info.OutputReportLength);
            if (!handle.WriteOutput(poke, TimeSpan.FromMilliseconds(500), quiet: true))
            {
                // quiet：非鼠标接口（或鼠标睡着）写失败是预期结果，逐次记 WARN 只会掩盖真故障
                Log.Once(LogLevel.Debug, LogKey(handle, LogKeyMousePokeFail),
                    $"迈从 鼠标触发帧写失败（此接口可能无输出通道）: {handle.Info.DevicePath}");
                return null;
            }
            Log.ResetOnce(LogKey(handle, LogKeyMousePokeFail));
            push = ListenE2(handle, TimeSpan.FromSeconds(3));
        }

        if (push.HasValue)
            return (ProtocolMouseV2, push.Value.Percent,
                push.Value.Charging ? ChargingState.Charging : ChargingState.Discharging);

        Log.Once(LogLevel.Debug, LogKey(handle, LogKeyMouseSilent),
            $"迈从 鼠标探测无响应（鼠标可能在睡；收到 E2 推送后会再记）: {handle.Info.DevicePath}");
        return null;
    }

    /// <summary>纯被动监听 E2 状态帧，不写任何字节。</summary>
    private static (int Percent, bool Charging)? ListenE2(HidDeviceHandle handle, TimeSpan window)
    {
        var deadline = DateTime.UtcNow + window;
        while (DateTime.UtcNow < deadline)
        {
            var input = handle.ReadInput(TimeSpan.FromMilliseconds(300));
            if (input == null) continue;
            var kind = MchoseMouseCodec.ParseStatus(input, out int pct, out bool chg);
            if (kind != MouseV2PushKind.Status) continue;
            Log.Info($"迈从 鼠标 E2 状态帧: 电量={pct} 充电={chg} 原始={Log.Hex(input)}");
            return (pct, chg);
        }
        return null;
    }

    // ══════════════════════════════════════════════════════════════════
    //  0x4D（原 qhw / mouse-k7 合并）：帧格式与字段表见 Mchose0x4DCodec
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 0x4D 轮询：一条 <c>0x0900</c>，最多重试 2 次。
    ///
    /// **记账由本方法自己做**，规则是"dongle 回了任何 0x4D 帧就算有应答"，<b>包含</b>只回 1 字节 0xFF 的
    /// "鼠标在睡"。若按普通失败记，睡着的鼠标连续三轮就会被判离线并熔断停写，之后醒了也不会再被问——
    /// 这是 0x4D 通道与其它协议最大的不同，也是它不能走 <see cref="PollDevice"/> 通用记账的原因。
    /// </summary>
    private (int? Percent, ChargingState Charging)? Poll0x4D(TrackedDevice dev)
    {
        var handle = dev.Handle;
        if (handle.Info.OutputReportLength < Mchose0x4DCodec.FrameLength) return null;

        var query = Mchose0x4DCodec.BuildDeviceInfoQuery();
        bool answered = false;
        byte[]? lastAnswer = null;

        for (int attempt = 1; attempt <= 2; attempt++)
        {
            if (!handle.WriteOutput(query, TimeSpan.FromMilliseconds(500)))
            {
                Log.Debug($"迈从 0x4D 轮询第{attempt}次写失败: {handle.Info.DevicePath}");
                continue;
            }

            var deadline = DateTime.UtcNow + ReplyWindow;
            while (DateTime.UtcNow < deadline)
            {
                var input = handle.ReadInput(TimeSpan.FromMilliseconds(250));
                if (input == null) continue;
                if (input.AsSpan().SequenceEqual(query)) continue;   // 写回显，不是应答

                if (Mchose0x4DCodec.LooksLikeReply(input))
                {
                    answered = true;
                    lastAnswer = input;
                }

                if (Mchose0x4DCodec.TryParseDeviceInfo(input, out int percent, out byte charge, out byte connect))
                {
                    ReportPollAnswered(dev);
                    Log.Info($"迈从 0x4D 轮询: 电量={percent} 充电状态={charge} 连接状态={connect}");
                    RememberIdentity(handle, input);
                    // 连接方式只刷新徽标：Unknown 不覆盖已知值，也不参与在线判定（在线仍只看有无应答）
                    SetConnection(dev, Mchose0x4DCodec.ToConnectionKind(connect));
                    ReadPointerOnce(dev);   // 回报率发布一次；DPI 配置只记日志
                    return (percent, Mchose0x4DCodec.ToChargingState(charge, percent));
                }
            }
        }

        if (answered)
        {
            // dongle 在、只是鼠标在睡：算有应答，只是这轮没读数
            ReportPollAnswered(dev);
            Log.Once(LogLevel.Debug, LogKey(handle, LogKey0x4DAsleep),
                $"迈从 0x4D 轮询只收到拒绝应答（鼠标多半在睡，唤醒后自动恢复；恢复后会再记一条）: " +
                $"{handle.Info.DevicePath} 最后应答={(lastAnswer == null ? "(无)" : Log.Hex(lastAnswer))}");
        }
        else
        {
            ReportPollFailure(dev);
        }
        return null;
    }

    /// <summary>
    /// 记下 `0x0900` 自报的配对身份（只读解析，**不改认领**）。型号只来自目录，目录没有就留空——
    /// dongle PID 被多款共用，不能反推型号（mchose.md §3.3、mchose-catalog.md §1.3）。
    /// </summary>
    private void RememberIdentity(HidDeviceHandle handle, byte[] frame)
    {
        if (!Mchose0x4DCodec.TryParseDeviceIdentity(frame, out ushort vid, out ushort pid)) return;

        var model = MchoseDeviceCatalog.TryGet(vid, pid, out var entry) ? entry.Model : "";
        var identity = MchoseDeviceIdentity.FromUsb(handle.Info).WithReported(vid, pid, model);
        var path = handle.Info.DevicePath;

        // 每轮都解析，只在身份变化时落库与记日志（轮询 60 秒一次，否则日志会被同一行刷满）
        if (KnownIdentityByPath.TryGetValue(path, out var known) && known == identity) return;
        KnownIdentityByPath[path] = identity;

        var transport = MchoseDeviceCatalog.TryGetTransport(handle.Info.VendorId, handle.Info.ProductId, out var t)
            ? t.Role
            : "(未登记)";
        Log.Info($"迈从身份: 自报={vid:X4}:{pid:X4} 型号={(model.Length == 0 ? "(目录未收录)" : model)} " +
                 $"通道={transport} 枚举={handle.Info.VidPid}");
    }

    /// <summary>
    /// 把 <c>0x0600</c> bit0 的活动档位落到指针遥测。**存协议原值（0 基）**——官方 UI 显示 index+1 是它的展示选择。
    /// </summary>
    private void ApplyDpiStage(TrackedDevice dev, byte stage, byte[] raw)
    {
        Log.Info($"迈从 0x4D 档位事件: stage={stage} 设备={dev.Info.VidPid} 原始={Log.Hex(raw)}");
        SetPointerTelemetry(dev, new PointerTelemetry
        {
            DpiLevel = TelemetryValue<int>.Reported(stage, DateTimeOffset.Now),
        });
    }

    /// <summary>接口路径 → 取证状态：≥0 是已尝试轮数，<see cref="PointerProbeDone"/> 表示已成功收工。</summary>
    private readonly ConcurrentDictionary<string, int> PointerProbeAttempts = new(StringComparer.OrdinalIgnoreCase);

    private const int PointerProbeDone = -1;
    private const int PointerProbeMaxAttempts = 3;

    /// <summary>
    /// 认领后读回报率与 DPI 配置。**最多 3 轮**、两项都成功才收工——睡着的设备不该因为第一次没应答就永久放弃。
    /// </summary>
    private void ReadPointerOnce(TrackedDevice dev)
    {
        var handle = dev.Handle;
        var path = handle.Info.DevicePath;

        int attempts = PointerProbeAttempts.GetOrAdd(path, 0);
        if (attempts < 0 || attempts >= PointerProbeMaxAttempts) return;
        PointerProbeAttempts[path] = attempts + 1;
        bool lastChance = attempts + 1 >= PointerProbeMaxAttempts;

        bool rateOk = TryReadPerformance(handle, out var rate, out var rateRaw);
        if (rateOk) ReportRate(dev, rate, rateRaw);
        else LogProbeFailure(lastChance,
            $"迈从 0x4D 性能配置读取无应答（命令 0x0002，设备 {handle.Info.VidPid}）: {path}");

        bool dpiOk = TryReadDpi(handle, 0, Mchose0x4DCodec.DirectionX, out var xAxis, out var xRaw);
        if (!dpiOk)
        {
            LogProbeFailure(lastChance,
                $"迈从 0x4D DPI 读取无应答（命令 0x0003 档 0 方向 X，设备 {handle.Info.VidPid}）: {path}");
        }
        else
        {
            Log.Info($"迈从 0x4D DPI(X): 设备={handle.Info.VidPid} 档={xAxis.ProfileIndex} " +
                     $"档数={xAxis.DpiSum} 当前档={xAxis.ActiveIndex} 有Y={xAxis.HasY} " +
                     $"值=[{string.Join(",", xAxis.Values)}] 原始={Log.Hex(xRaw)}");

            // 只有 X 解析成功且设备声明有独立 Y 轴才发第二条；Y 失败只丢 Y，不影响 X
            if (Mchose0x4DCodec.ShouldReadYAxis(xParsed: true, xAxis.HasY))
            {
                if (TryReadDpi(handle, xAxis.ProfileIndex, Mchose0x4DCodec.DirectionY, out var yAxis, out var yRaw))
                    Log.Info($"迈从 0x4D DPI(Y): 设备={handle.Info.VidPid} 档={yAxis.ProfileIndex} " +
                             $"值=[{string.Join(",", yAxis.Values)}] 原始={Log.Hex(yRaw)}");
                else
                    Log.Debug($"迈从 0x4D DPI(Y) 读取无应答，X 结果保留: {path}");
            }
        }

        if (rateOk && dpiOk) PointerProbeAttempts[path] = PointerProbeDone;
    }

    /// <summary>失败日志：最后一轮记 Info（取证要看得见），中间轮记 Debug（别刷屏）。</summary>
    private static void LogProbeFailure(bool lastChance, string message)
    {
        if (lastChance) Log.Info(message);
        else Log.Debug(message);
    }

    /// <summary>
    /// 记 <c>0x0002</c> 取证行并按判定发布；不发布时已有值原样保留，<c>ReportRateMeasuredHz</c> 永远不填。
    /// </summary>
    private void ReportRate(TrackedDevice dev, MchoseRateIndices indices, byte[] raw)
    {
        var identity = KnownIdentityByPath.TryGetValue(dev.Info.DevicePath, out var known) ? known : default;

        if (!identity.HasReported)
        {
            Log.Info($"迈从 0x4D 回报率: 设备={dev.Info.VidPid} 自报=(未知) 命令=0x0002 " +
                     $"profile={indices.ProfileIndex} usb={indices.UsbRateIndex} g={indices.GRateIndex} " +
                     $"dpi={indices.DpiIndex} 不发布=型号不明 原始={Log.Hex(raw)}");
            return;
        }

        var decision = MchoseRatePolicy.Decide(identity.ReportedProductId!.Value, dev.Info.ProductId,
            indices.UsbRateIndex, indices.GRateIndex, out int hz, out string table);

        string outcome = decision switch
        {
            MchoseRateDecision.Publish => $"发布={hz}Hz 表={table}",
            MchoseRateDecision.IndicesDiffer => "不发布=两个 index 不一致（语义未确认）",
            MchoseRateDecision.NoTable => "不发布=目录里没有这张表",
            _ => $"不发布=index {indices.UsbRateIndex} 超出表 {table} 范围",
        };

        Log.Info($"迈从 0x4D 回报率: 设备={dev.Info.VidPid} 自报={identity.ReportedProductId:X4} 命令=0x0002 " +
                 $"profile={indices.ProfileIndex} usb={indices.UsbRateIndex} g={indices.GRateIndex} " +
                 $"dpi={indices.DpiIndex} {outcome} 原始={Log.Hex(raw)}");

        if (decision != MchoseRateDecision.Publish) return;

        SetPointerTelemetry(dev, new PointerTelemetry
        {
            ReportRateSettingHz = TelemetryValue<int>.Inferred(hz, DateTimeOffset.Now),
        });
    }

    /// <summary>发一条 <c>0x0002</c> 并等应答。**只读**，且只认这个命令的应答。</summary>
    private static bool TryReadPerformance(HidDeviceHandle handle, out MchoseRateIndices indices, out byte[] raw)
    {
        indices = default;
        raw = [];
        if (handle.Info.OutputReportLength < Mchose0x4DCodec.FrameLength) return false;

        var query = Mchose0x4DCodec.BuildPerformanceQuery();
        if (!handle.WriteOutput(query, TimeSpan.FromMilliseconds(500), quiet: true)) return false;

        var deadline = DateTime.UtcNow + ReplyWindow;
        while (DateTime.UtcNow < deadline)
        {
            var input = handle.ReadInput(TimeSpan.FromMilliseconds(250));
            if (input == null) continue;
            if (input.AsSpan().SequenceEqual(query)) continue;   // 写回显，不是应答

            if (Mchose0x4DCodec.TryParsePerformance(input, out indices))
            {
                raw = input;
                return true;
            }
        }
        return false;
    }

    /// <summary>发一条 <c>0x0003</c> 并等应答。**只读**：绝不发 <c>0x01xx</c> 写方向。</summary>
    private static bool TryReadDpi(HidDeviceHandle handle, byte profile, byte direction,
        out MchoseDpiReading reading, out byte[] raw)
    {
        reading = default;
        raw = [];
        if (handle.Info.OutputReportLength < Mchose0x4DCodec.FrameLength) return false;

        var query = Mchose0x4DCodec.BuildDpiQuery(profile, direction);
        if (!handle.WriteOutput(query, TimeSpan.FromMilliseconds(500), quiet: true)) return false;

        var deadline = DateTime.UtcNow + ReplyWindow;
        while (DateTime.UtcNow < deadline)
        {
            var input = handle.ReadInput(TimeSpan.FromMilliseconds(250));
            if (input == null) continue;
            if (input.AsSpan().SequenceEqual(query)) continue;   // 写回显，不是应答

            if (Mchose0x4DCodec.TryParseDpi(input, out reading))
            {
                raw = input;
                return true;
            }
        }
        return false;
    }

    // Gmouse / V9 / X9 三套主动查询的帧模板、窗口与重试次数都在 specs/mchose.protocols.json，
    // 由 Plugins/Protocol/ 的解释器执行（原 QueryGmouse / QueryHeadsetV9 / QueryHeadsetX9 已删）。

    /// <summary>
    /// 键盘 V1 协议（本机 dongle 41E4:2001 实测打通，G87 V2 接收器 3837:201B 同协议；与官方 M HUB WEB 抓包一致）。
    /// 帧格式与解析全部在 <see cref="MchoseV1Codec"/>（纯函数，有单测）；帧结构见
    /// docs/hardware/mchose.md §1。会话必须先开，否则 dongle 不响应；被崩溃进程占住会话时
    /// 所有人都会静音，需重新插拔释放。
    ///
    /// "无响应"只在状态变化时记日志：发现节拍每 15 秒重探一次，键盘睡着时逐次记录会把 app.log
    /// 刷满且无新信息。成功一次就 <see cref="Log.ResetOnce"/>。
    /// </summary>
    private (int? Percent, ChargingState Charging)? TryKeyboardV1(HidDeviceHandle handle)
        => TryKeyboardV1(handle, requireKnownModel: false);

    /// <summary><paramref name="requireKnownModel"/> = true 用于与 AULA 共用的 3554:FA09 指纹：
    /// 只有会话 ACK 的型号密码落在迈从表里才认领，否则把接口留给 AulaPlugin（aula.md §1.1）。</summary>
    private (int? Percent, ChargingState Charging)? TryKeyboardV1(HidDeviceHandle handle, bool requireKnownModel)
    {
        if (!IsWritesEnabled(SettingsKey)) return null;

        // 开会话（官方时序），失败不影响下一步——实测 dongle 记住会话状态
        SendKeyboardV1Frame(handle, MchoseV1Codec.SessionCmd, 0x01);
        (int? Percent, ChargingState Charging)? sessionRead = null;
        bool modelKnown = false;
        var deadline0 = DateTime.UtcNow + TimeSpan.FromSeconds(1.2);
        while (DateTime.UtcNow < deadline0)
        {
            var input = handle.ReadInput(TimeSpan.FromMilliseconds(300));
            if (input == null) continue;

            var read = InterpretV1Input(input, out bool stop);
            if (stop) sessionRead ??= read;

            // 会话 ACK 的密码段就是接收器型号（官方 getPassWord 同源）：帧本来就要收，顺手记下零成本
            var model = MchoseMouseCodec.ModelFromSessionAck(input);
            if (model != null)
            {
                KnownModelByPath[handle.Info.DevicePath] = model;
                modelKnown = true;
                Log.Info($"迈从 会话 ACK 识别型号: {model} 原始={Log.Hex(input)}");
            }
            if (Log.IsEnabled(LogLevel.Debug)) Log.Debug($"迈从 会话ACK: {Log.Hex(input)}");

            if (sessionRead != null && (!requireKnownModel || modelKnown))
            {
                Log.Info($"迈从 会话期间即收到电量推送，直接采用");
                Log.ResetOnce(LogKey(handle, LogKeyKbV1Silent));
                return sessionRead;
            }
        }

        if (requireKnownModel && !modelKnown)
        {
            Log.Once(LogLevel.Debug, LogKey(handle, LogKeyKbV1ModelUnknown),
                $"迈从 共用指纹的接收器型号不在迈从表里，接口留给 AULA 插件: {handle.Info.DevicePath}");
            return null;
        }
        if (sessionRead != null) return sessionRead;

        // 电量查询，最多重试 2 次（键盘休眠时先醒来）
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            if (!SendKeyboardV1Frame(handle, MchoseV1Codec.BatteryQueryCmd, 0x01))
            {
                Log.Debug($"迈从 keyboard-v1 第{attempt}次写失败: {handle.Info.DevicePath}");
                continue;
            }

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(1.2);
            while (DateTime.UtcNow < deadline)
            {
                var input = handle.ReadInput(TimeSpan.FromMilliseconds(300));
                if (input == null) continue;

                var read = InterpretV1Input(input, out bool stop);
                if (stop)
                {
                    Log.ResetOnce(LogKey(handle, LogKeyKbV1Silent));
                    return read;
                }
            }
        }

        Log.Once(LogLevel.Debug, LogKey(handle, LogKeyKbV1Silent),
            $"迈从 keyboard-v1 两次询问无响应（键盘多半在睡；恢复应答后会再记一条）: {handle.Info.DevicePath}");
        return null;
    }

    /// <summary>构造并发送 V1 帧：[0x13, cmd, param, 0×16, sum(前19字节)]。</summary>
    private static bool SendKeyboardV1Frame(HidDeviceHandle handle, byte cmd, byte param)
    {
        if (handle.Info.OutputReportLength < MchoseV1Codec.FrameLength) return false;
        return handle.WriteOutput(MchoseV1Codec.BuildFrame(cmd, param), TimeSpan.FromMilliseconds(500));
    }

    /// <summary>
    /// 解读一条输入帧（0A 推送或 4A 响应），并负责日志。
    /// <paramref name="stop"/> = true 表示本轮不必再等（已拿到读数）。
    /// </summary>
    private static (int? Percent, ChargingState Charging)? InterpretV1Input(byte[] input, out bool stop)
    {
        stop = false;

        var push = MchoseV1Codec.ParsePush(input);
        switch (push.Kind)
        {
            case V1PushKind.Battery:
                stop = true;
                // 必须打日志：这条路径原来静默，于是"轮询到底跑没跑、有没有拿到读数"在日志里看不见。
                Log.Info($"迈从 0A 电量推送: 电量={push.Percent} 标志=0x{push.ChargeFlag:X2} 原始={Log.Hex(input)}");
                return (push.Percent, ParseKeyboardV1Charge(push.ChargeFlag, push.Percent));

            case V1PushKind.StateNotify:
                // 0x02 通知不当休眠/关机处理（2026-10-06 复测推翻旧结论）：payload 0x00/0x01 全天交替，
                // 且每次通知后设备仍能正常应答查询，按休眠标离线只会造成在线↔离线闪烁。
                // 真正的失联靠 PollDevice 里的失败记账（ReportPollFailure）——本插件 CountPollFailures=false，
                // 基类不会替我们数，必须自己报账。
                // 这里不触发核实：本方法服务于主动路径，查询刚发过，立刻再查没有新信息。
                Log.Debug($"迈从 收到设备状态通知（payload=0x{push.Payload:X2}，语义未明仅记录）: {Log.Hex(input)}");
                return null;

            case V1PushKind.BatteryInvalid:
                Log.Debug($"迈从 忽略异常电量帧（电量={push.Percent}）: {Log.Hex(input)}");
                return null;

            case V1PushKind.Other:
                Log.Debug($"迈从 忽略非电量推送帧（类型=0x{push.Type:X2}）: {Log.Hex(input)}");
                return null;
        }

        switch (MchoseV1Codec.ParseBatteryResponse(input, out int pct, out byte flag))
        {
            case V1ResponseKind.Battery:
                Log.Info($"迈从 keyboard-v1 解析: 电量={pct} 状态字节=0x{flag:X2} 原始={Log.Hex(input)}");
                stop = true;
                return (pct, ParseKeyboardV1Charge(flag, pct));

            case V1ResponseKind.InvalidBattery:
                Log.Debug($"迈从 keyboard-v1 电量占位/越界: {Log.Hex(input)}");
                return null;

            default:
                return null;
        }
    }

    /// <summary>充电标志：0x10 位=充电中；实测 35% 放电时标志=0x01（语义待插线校准）。
    /// 收尾规则（未充电且 100% ⇒ 已充满）见 <see cref="ChargingStateMap"/>。</summary>
    private static ChargingState ParseKeyboardV1Charge(byte flag, int percent)
        => ChargingStateMap.FromChargingFlag((flag & 0x10) != 0, percent);

    /// <summary>keyboard-v1 轮询：只发电量查询（会话已建立），解析 4A 响应和 0A 推送。</summary>
    private static (int? Percent, ChargingState Charging)? PollKeyboardV1(TrackedDevice dev)
    {
        if (!IsWritesEnabled(SettingsKey)) return null;

        if (!SendKeyboardV1Frame(dev.Handle, MchoseV1Codec.BatteryQueryCmd, 0x01)) return null;
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

}
