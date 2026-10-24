using System.Collections.Concurrent;
using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
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

    /// <summary>键盘 V1 会话协议（report 0x13）。</summary>
    private const string ProtocolKeyboardV1 = "keyboard-v1";

    /// <summary>鼠标 V2 世代：纯被动 E2 状态帧 + 0x11/0x06 触发上报。</summary>
    private const string ProtocolMouseV2 = "mouse-v2";

    /// <summary>K7 V2 Ultra+：64 字节 output 0x4D，0x0900 查询 + 0x0600 事件（帧与判据见 <see cref="MchoseK7Codec"/>）。</summary>
    private const string ProtocolMouseK7 = "mouse-k7";

    /// <summary>V9 Pro 耳机：64 字节固定查询帧（帧结构与判据见 <see cref="MchoseV9ProCodec"/>）。同品牌另一条链路，与键鼠判据完全独立。</summary>
    private const string ProtocolHeadsetV9 = "headset-v9";

    private const byte QhwReportId = 0x4D;
    private const ushort CmdGetBattery = 0x0904;  // 2308

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
    /// 迈从协议里**根本没有"连接状态"字段**（不像雷柏有 bit7、北通有占位帧），所以失败计数是它
    /// 唯一能判离线的物理依据。
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
        if (info.VendorId == Logitech.LogitechPlugin.LogitechVid) return false;
        if (info.VendorId == Rapoo.RapooPlugin.RapooVid) return false;
        // 北通接收器也有 usage 0xFF00 的 vendor 接口，但它不是迈从协议，勿发 QHW/0x13 帧
        if (info.VendorId == Betop.BetopPlugin.BetopVid) return false;
        // 英菲克接收器也暴露 64 字节 vendor 输出接口；避免误发迈从会话/查询帧
        if (info.VendorId == Inphic.InphicPlugin.InphicVid) return false;
        // HECATE 的厂商集合同样是 64 字节进出，不能当成迈从接口
        if (info.VendorId == Hecate.HecatePlugin.HecateVid) return false;
        // AULA F99 Pro 与迈从 G75 共用 3554:FA09 的 20 字节 0x13 接口，只能靠会话 ACK 的型号密码区分；
        // 这里放行这条精确指纹，认领阶段由 TryKeyboardV1 按型号裁决（aula.md §1.1）
        if (info.VendorId == Aula.AulaPlugin.AulaVid && !IsSharedWithAula(info)) return false;
        // 品牌排除之后，判据全在分类器里（耳机先分流、键鼠白名单、厂商用法、报告长度），有单测覆盖
        if (Classify(info) != MchoseInterfaceKind.None) return true;
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
            ProtocolMouseK7 => DeviceKind.Mouse,
            ProtocolHeadsetV9 => DeviceKind.Headset,
            _ => DeviceKind.Keyboard,
        };

    /// <summary>显示名：接收器产品名常是泛化的「MCHOSE 2.4G Wireless」，识别出型号时用型号替换；
    /// 加「迈从」前缀方便设备列表一眼认品牌。型号来源见 <see cref="KnownModelByPath"/>。</summary>
    protected override string BuildDisplayName(HidDeviceInfo info)
    {
        // 耳机接收器是独立 VID/PID，只对应已验证的 V9 Pro；协议里没有型号回执，型号名只能写死
        if (info.VendorId == MchoseV9ProCodec.VendorId && info.ProductId == MchoseV9ProCodec.ProductId)
            return "迈从 V9 Pro";

        var product = string.IsNullOrEmpty(info.Product) ? "" : info.Product.Replace("MCHOSE", "").Trim();
        var model = KnownModelByPath.TryGetValue(info.DevicePath, out var m) ? m : "";
        var name = model.Length > 0 && product.Contains("2.4G") ? model
                 : product.Length > 0 ? product
                 : model;
        return string.IsNullOrEmpty(name) ? $"迈从 ({info.VidPid})" : $"迈从 {name}";
    }

    /// <summary>
    /// 接口路径 → 已判定协议（进程内学习）。**只在探测成功后写入，失败不抹掉**：
    /// 同一条路径永远是同一个型号，而探测失败最常见的原因是"键盘在睡"，说明不了协议变了。
    /// </summary>
    private static readonly ConcurrentDictionary<string, string> KnownProtocolByPath = new();

    /// <summary>接口路径 → 会话 ACK 密码段识别出的型号名（<see cref="BuildDisplayName"/> 用；识别不出就没有条目）。</summary>
    private static readonly ConcurrentDictionary<string, string> KnownModelByPath = new();

    /// <summary>
    /// 日志去重 key = <c>路径|用途</c>。配合 <see cref="Log.Once"/> / <see cref="Log.ResetOnce"/>
    /// 表达"**状态变化才记**"：发现节拍每 15 秒重探一次，设备睡着时逐次记录会把 app.log 刷满且无新信息；
    /// 恢复正常时复位标记，下次再坏又能看到一条。
    /// </summary>
    private static string LogKey(HidDeviceHandle handle, string what) => $"{handle.Info.DevicePath}|{what}";

    private const string LogKeyQhwSkip = "qhw-skip";          // "这条通道不是 QHW"
    private const string LogKeyQhwNoPermit = "qhw-no-permit"; // "没开写入许可，跳过主动查询"
    private const string LogKeyKbV1Silent = "kbv1-silent";    // "keyboard-v1 问了两次都不应答"
    private const string LogKeyKbV1ModelUnknown = "kbv1-model-unknown"; // "共用指纹但型号不在迈从表，交给 AULA"
    private const string LogKeyMouseSilent = "mouse-silent";  // "鼠标探测没等到 E2 帧"
    private const string LogKeyMousePokeFail = "mouse-poke-fail"; // "触发帧写失败（接口无输出通道）"
    private const string LogKeyV9Silent = "v9-silent"; // "V9 Pro 查询没等到电量响应"
    private const string LogKeyK7NoPermit = "k7-no-permit"; // "没开写入许可，跳过 K7 主动查询"
    private const string LogKeyK7Asleep = "k7-asleep";      // "K7 只回 1 字节 0xFF，鼠标多半在睡"

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

        // 耳机必须排在下面「≥60 字节只走鼠标」之前：它的输出同样是 64 字节，
        // 落到鼠标分支会往耳机发 0x11/0x06 触发帧。判据是完整接口匹配，抢不走键盘/鼠标的通道。
        if (kind == MchoseInterfaceKind.HeadsetV9)
        {
            var headset = TryHeadsetV9(handle);
            if (headset != null) KnownProtocolByPath[path] = ProtocolHeadsetV9;
            return headset;
        }

        // K7 的命令通道同样是 64 字节，但它说的是 0x4D 帧，不是 V2 世代那套 E2 状态帧——
        // 落到下面等于白等两轮再发一条无意义的触发帧。指纹已精确命中，直接交给 K7 协议。
        if (kind == MchoseInterfaceKind.MouseK7)
        {
            var k7 = TryMouseK7(handle);
            if (k7 != null) KnownProtocolByPath[path] = ProtocolMouseK7;
            return k7;
        }

        // ≥60 的接口只可能是鼠标接收器：它不是 0x13 会话通道，在这条通道上发键盘帧没有意义
        if (kind == MchoseInterfaceKind.MouseV2)
        {
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

        var qhw = TryQhw(handle);
        if (qhw != null)
        {
            KnownProtocolByPath[path] = qhw.Value.Protocol;
            return qhw;
        }

        var kbV1 = TryKeyboardV1(handle, IsSharedWithAula(handle.Info));
        if (kbV1.HasValue)
        {
            var (p, c) = kbV1.Value;
            KnownProtocolByPath[path] = ProtocolKeyboardV1;
            return (ProtocolKeyboardV1, p, c);
        }
        return null;
    }

    /// <summary>按已判定的协议只试那一条通道（避免每次探测都先空等 QHW 3 秒）。</summary>
    private static (string Protocol, int? Percent, ChargingState Charging)? TryKnownProtocol(HidDeviceHandle handle, string known)
        => known switch
        {
            "qhw" or "qhw-push" => TryQhw(handle),
            ProtocolKeyboardV1 => TryKeyboardV1(handle, IsSharedWithAula(handle.Info)) is { } kb
                ? (ProtocolKeyboardV1, kb.Percent, kb.Charging)
                : null,
            ProtocolMouseV2 => TryMouseV2(handle),
            ProtocolMouseK7 => TryMouseK7(handle),
            ProtocolHeadsetV9 => TryHeadsetV9(handle),
            _ => null,
        };

    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev)
    {
        // 熔断 / 未开写入 / 纯推送协议：读数交给后台被动监听。这几条都没发帧，故不计失败。
        // mouse-v2 的读数是接收器自发 E2 推送，这里发帧只会干扰鼠标。
        if (!CanWrite(dev) || dev.Protocol is "qhw-push" or ProtocolMouseV2)
            return null;

        // K7 的记账规则与其它协议不同——"只回 1 字节 0xFF"也算应答，故整段交给 PollMouseK7 自己数，
        // 不走下面那套"有读数算应答 / 无读数算失败"的通用规则（理由见该方法注释）。
        if (dev.Protocol == ProtocolMouseK7)
            return PollMouseK7(dev);

        var read = dev.Protocol switch
        {
            "qhw" => PollQhw(dev),
            ProtocolKeyboardV1 => PollKeyboardV1(dev),
            ProtocolHeadsetV9 => PollHeadsetV9(dev),
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

        if (dev.Protocol is "qhw" or "qhw-push")
        {
            var cz = ParseCzPush(input);
            if (cz.HasValue)
                return (cz.Value.Percent,
                    cz.Value.Charging ? ChargingState.Charging : ChargingState.Discharging,
                    Offline: false);
        }

        if (dev.Protocol == ProtocolMouseK7)
        {
            if (!MchoseK7Codec.TryParseStatusEvent(input, out var evt)) return null;

            // 0x0600 事件一次只带一个字段（电量帧 / 充电帧是两条不同的帧），缺的那个沿用当前值，
            // 否则会把另一项冲成 Unknown。连历史电量都没有就没什么可报的，等下一帧。
            int? percent = evt.Percent ?? dev.Status.BatteryPercent;
            if (!percent.HasValue) return null;
            var charging = evt.ChargeStatus is { } raw ? MapK7Charge(raw, percent.Value) : dev.Status.Charging;

            Log.Debug($"迈从 K7 状态事件: 电量={evt.Percent?.ToString() ?? "(未带)"} " +
                      $"充电={evt.ChargeStatus?.ToString() ?? "(未带)"} 原始={Log.Hex(input)}");
            return (percent, charging, Offline: false);
        }

        return null;
    }

    private static byte _seq = 1;

    /// <summary>构造官方 QHW 帧：[0x4D, ver=1, flags=3, len, cmdLo, cmdHi, biz=0, seq, payload…, XOR 校验]，补零到接口输出长度。</summary>
    private static byte[] BuildQhwFrame(HidDeviceHandle handle, ushort command, params byte[] payload)
    {
        int outLen = Math.Max(handle.Info.OutputReportLength, payload.Length + 10);
        var frame = new byte[outLen];
        frame[0] = QhwReportId;
        frame[1] = 0x01;                      // version
        frame[2] = 0x03;                      // flags
        frame[3] = (byte)payload.Length;      // length
        frame[4] = (byte)(command & 0xFF);    // cmd lo
        frame[5] = (byte)(command >> 8);      // cmd hi
        frame[6] = 0x00;                      // bizCode
        frame[7] = _seq++;                    // sequence
        for (int i = 0; i < payload.Length; i++) frame[8 + i] = payload[i];
        byte xor = 0;
        for (int i = 1; i < 8 + payload.Length; i++) xor ^= frame[i]; // 官方: 255 & d[1..] 逐字节异或
        frame[8 + payload.Length] = xor;
        return frame;
    }

    /// <summary>解析 QHW 响应。返回 (充电状态字节, 电量%) 或 null（非电量响应）。</summary>
    private static (byte ChargeRaw, int Percent)? ParseQhwBatteryResponse(byte[] input, ushort command)
    {
        // Windows ReadInput 缓冲 [0]=report ID；兼容无 ID 前缀（[0]=0）两种布局
        foreach (int off in new[] { 0, 1 })
        {
            if (input.Length < off + 10) continue;
            if (input[off] != QhwReportId || input[off + 1] != 0x01) continue;
            if (input[off + 4] != (command & 0xFF) || input[off + 5] != (command >> 8)) continue;
            byte charge = input[off + 8];
            byte pct = input[off + 9];
            if (pct <= InvalidPercentFloor || pct > 100 || charge > 2) return null;   // ≤1 = 休眠/关机占位帧，无效
            return (charge, pct);
        }
        return null;
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

    private static ChargingState MapCharging(byte chargeRaw)
    {
        return chargeRaw switch
        {
            1 => ChargingState.Charging,
            2 => ChargingState.Full,
            0 => ChargingState.Discharging,
            _ => ChargingState.Unknown,
        };
    }

    /// <summary>发送官方握手帧（数据 AA 55 AA 55 AA 55，report 0x4D）。</summary>
    /// <param name="quiet">
    /// 写失败时不记 WARN。**只有"把握手当通道判据"的探测路径该传 true**——非 QHW 通道的描述符里
    /// 没有 report 0x4D，写必然失败，这正是"不是这条通道"的判据；已知 QHW 设备上的握手写失败是真故障。
    /// </param>
    private static bool SendQhwHandshake(HidDeviceHandle handle, bool quiet = false)
    {
        var handshake = new byte[handle.Info.OutputReportLength];
        handshake[0] = QhwReportId;
        byte[] data = { 0xAA, 0x55, 0xAA, 0x55, 0xAA, 0x55 };
        Array.Copy(data, 0, handshake, 1, data.Length);
        return handle.WriteOutput(handshake, TimeSpan.FromMilliseconds(500), quiet);
    }

    /// <summary>
    /// 官方 QHW 电量查询（M HUB WEB 时序：握手 → getBattery(0x0904)）。
    /// 握手放最前：非 QHW dongle 会在握手写入处立即失败并快速退出，不再先空等被动监听。
    /// </summary>
    private static (string Protocol, int? Percent, ChargingState Charging)? TryQhw(HidDeviceHandle handle)
    {
        // 未授权写入时只能被动等推送（正常情况下走不到：插件开关即授权）；保留作纵深防御
        if (!IsWritesEnabled(SettingsKey))
        {
            var passive = ListenCzPush(handle, TimeSpan.FromSeconds(3));
            if (passive.HasValue)
            {
                var (pct0, chg0) = passive.Value;
                return ("qhw-push", pct0, chg0 ? ChargingState.Charging : ChargingState.Discharging);
            }
            // 只在状态变化时记：关掉写入许可后设备永远不会被认领，逐次记录会无限刷屏。
            Log.Once(LogLevel.Info, LogKey(handle, LogKeyQhwNoPermit),
                $"迈从 qhw: 未开启写入许可，跳过主动查询: {handle.Info.DevicePath}");
            return null;
        }

        // 许可已开：清掉"没许可"的记过标记，下次再关掉时会重新记一条
        Log.ResetOnce(LogKey(handle, LogKeyQhwNoPermit));

        // 官方时序：先握手。非 QHW dongle（如本机 G87）握手写会立即以 ERROR_INVALID_PARAMETER 失败，
        // 这里直接返回，省掉旧版"先空等 3 秒被动监听"的浪费（对照实验已确认结果相同）。
        // quiet: true —— 写失败在这里是**预期结果**（"不是 QHW 通道"的判据）；否则发现节拍每 15 秒
        // 就刷一条看着像故障的 WARN。INFO/Debug 也只记第一次，避免设备一直睡着时重复刷屏。
        if (!SendQhwHandshake(handle, quiet: true))
        {
            Log.Once(LogLevel.Debug, LogKey(handle, LogKeyQhwSkip),
                $"迈从 qhw 握手写失败（非 QHW 通道，快速跳过；同路径后续不再重复记录）: {handle.Info.DevicePath}");
            return null;
        }

        // 握手成功说明是 QHW 通道：清掉"不是 QHW"的标记，先给一小段时间收 CZ 自发电量推送
        Log.ResetOnce(LogKey(handle, LogKeyQhwSkip));
        var result = ListenCzPush(handle, TimeSpan.FromSeconds(1));
        if (result.HasValue)
        {
            var (pct, charging) = result.Value;
            return ("qhw-push", pct, charging ? ChargingState.Charging : ChargingState.Discharging);
        }

        var frame = BuildQhwFrame(handle, CmdGetBattery);
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            if (!handle.WriteOutput(frame, TimeSpan.FromMilliseconds(500)))
            {
                Log.Debug($"迈从 qhw 第{attempt}次写失败: {handle.Info.DevicePath}");
                return null;
            }

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            while (DateTime.UtcNow < deadline)
            {
                var input = handle.ReadInput(TimeSpan.FromMilliseconds(300));
                if (input == null) continue;

                var push = ParseCzPush(input);
                if (push.HasValue)
                {
                    Log.Info($"迈从 qhw 收到 CZ 电量推送: 电量={push.Value.Percent} 充电={push.Value.Charging} 原始={Log.Hex(input)}");
                    return ("qhw-push", push.Value.Percent,
                        push.Value.Charging ? ChargingState.Charging : ChargingState.Discharging);
                }

                var parsed = ParseQhwBatteryResponse(input, CmdGetBattery);
                if (parsed.HasValue)
                {
                    Log.Info($"迈从 qhw getBattery 解析: 电量={parsed.Value.Percent} 状态字节={parsed.Value.ChargeRaw} 原始={Log.Hex(input)}");
                    return ("qhw", parsed.Value.Percent, MapCharging(parsed.Value.ChargeRaw));
                }

                Log.Debug($"迈从 qhw 收到非匹配报文: {Log.Hex(input)}");
            }
        }
        Log.Info($"迈从 qhw 无电量响应: {handle.Info.DevicePath}");
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
    private static (string Protocol, int? Percent, ChargingState Charging)? TryMouseV2(HidDeviceHandle handle)
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
    //  K7 V2 Ultra+（3837:1018，output 0x4D 单通道）
    //  帧格式与字段表见 MchoseK7Codec；依据是官方 M HUB 的 i1e / _K / LA，不是猜的。
    // ══════════════════════════════════════════════════════════════════

    /// <summary>K7 查询的应答等待窗口。实测 dongle 回包在 1 秒内，鼠标在睡时会回 1 字节 0xFF。</summary>
    private static readonly TimeSpan K7ReplyWindow = TimeSpan.FromSeconds(1.2);

    /// <summary>
    /// K7 探测：发一条 <c>0x0900</c> 读设备信息，最多 3 次。
    ///
    /// 与其它协议的关键差别：**"只回 0xFF" 也是协议确认成功**。0xFF 的含义是"鼠标在睡 / 2.4G 暂时
    /// 联系不上"，官方 <c>hgt()</c> 对它的处理也正是重试 + 容忍无读数。所以即使这轮拿不到电量，
    /// 只要帧写得进去就返回 <c>("mouse-k7", null, Unknown)</c> 把设备认领下来——读数交给后续轮询与
    /// <c>0x0600</c> 事件帧；认领后 <c>IsOnline</c> 为 false（在位但不可读），鼠标一动就会被事件帧翻回在线。
    ///
    /// 反过来，**写失败就直接返回 null**：这颗 dongle 只有一条 output 通道，写不进去说明不是它。
    /// </summary>
    private static (string Protocol, int? Percent, ChargingState Charging)? TryMouseK7(HidDeviceHandle handle)
    {
        if (handle.Info.OutputReportLength < MchoseK7Codec.FrameLength) return null;

        if (!IsWritesEnabled(SettingsKey))
        {
            Log.Once(LogLevel.Info, LogKey(handle, LogKeyK7NoPermit),
                $"迈从 K7: 未开启写入许可，跳过主动查询: {handle.Info.DevicePath}");
            return null;
        }
        Log.ResetOnce(LogKey(handle, LogKeyK7NoPermit));

        var query = MchoseK7Codec.BuildDeviceInfoQuery();
        bool wrote = false;
        byte[]? lastAnswer = null;

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            if (!handle.WriteOutput(query, TimeSpan.FromMilliseconds(500), quiet: true))
            {
                // quiet：非 K7 通道写失败是预期结果（"不是这条通道"的判据），逐次 WARN 只会掩盖真故障
                Log.Debug($"迈从 K7 第{attempt}次写失败: {handle.Info.DevicePath}");
                return null;
            }
            wrote = true;

            var deadline = DateTime.UtcNow + K7ReplyWindow;
            while (DateTime.UtcNow < deadline)
            {
                var input = handle.ReadInput(TimeSpan.FromMilliseconds(250));
                if (input == null) continue;

                // 这颗 dongle 会把自己的 output 报告原样回显成一条输入帧；回显不是应答，必须跳过
                if (input.AsSpan().SequenceEqual(query)) continue;
                lastAnswer = input;

                if (MchoseK7Codec.TryParseDeviceInfo(input, out int percent, out byte charge, out byte connect))
                {
                    Log.ResetOnce(LogKey(handle, LogKeyK7Asleep));
                    Log.Info($"迈从 K7 设备信息: 电量={percent} 充电状态={charge} 连接状态={connect} " +
                             $"原始={Log.Hex(input)}");
                    return (ProtocolMouseK7, percent, MapK7Charge(charge, percent));
                }
            }
            Thread.Sleep(150);
        }

        if (!wrote) return null;

        Log.Once(LogLevel.Info, LogKey(handle, LogKeyK7Asleep),
            $"迈从 K7 三次都没拿到设备信息（鼠标多半在睡，唤醒后会由 0x0600 事件补上；" +
            $"恢复应答后会再记一条）: {handle.Info.DevicePath} 最后应答={(lastAnswer == null ? "(无)" : Log.Hex(lastAnswer))}");
        return (ProtocolMouseK7, null, ChargingState.Unknown);
    }

    /// <summary>
    /// K7 轮询：一次 <c>0x0900</c>，最多重试 2 次。
    ///
    /// **记账由本方法自己做**，规则是"dongle 回了任何 0x4D 帧就算有应答"，<b>包含</b>只回 1 字节 0xFF 的
    /// "鼠标在睡"。若按普通失败记，睡着的鼠标连续三轮就会被判离线并熔断停写，之后醒了也不会再被问——
    /// 这是这颗 dongle 与其它协议最大的不同，也是它不能走 <see cref="PollDevice"/> 通用记账的原因。
    /// </summary>
    private (int? Percent, ChargingState Charging)? PollMouseK7(TrackedDevice dev)
    {
        var handle = dev.Handle;
        if (handle.Info.OutputReportLength < MchoseK7Codec.FrameLength) return null;

        var query = MchoseK7Codec.BuildDeviceInfoQuery();
        bool answered = false;
        byte[]? lastAnswer = null;

        for (int attempt = 1; attempt <= 2; attempt++)
        {
            if (!handle.WriteOutput(query, TimeSpan.FromMilliseconds(500)))
            {
                Log.Debug($"迈从 K7 轮询第{attempt}次写失败: {handle.Info.DevicePath}");
                continue;
            }

            var deadline = DateTime.UtcNow + K7ReplyWindow;
            while (DateTime.UtcNow < deadline)
            {
                var input = handle.ReadInput(TimeSpan.FromMilliseconds(250));
                if (input == null) continue;
                if (input.AsSpan().SequenceEqual(query)) continue;   // 写回显，不是应答

                if (MchoseK7Codec.LooksLikeReply(input))
                {
                    answered = true;
                    lastAnswer = input;
                }

                if (MchoseK7Codec.TryParseDeviceInfo(input, out int percent, out byte charge, out byte connect))
                {
                    ReportPollAnswered(dev);
                    Log.Info($"迈从 K7 轮询: 电量={percent} 充电状态={charge} 连接状态={connect}");
                    return (percent, MapK7Charge(charge, percent));
                }
            }
        }

        if (answered)
        {
            // dongle 在、只是鼠标在睡：算有应答，只是这轮没读数
            ReportPollAnswered(dev);
            Log.Once(LogLevel.Debug, LogKey(handle, LogKeyK7Asleep),
                $"迈从 K7 轮询只收到拒绝应答（鼠标多半在睡，唤醒后自动恢复；恢复后会再记一条）: " +
                $"{handle.Info.DevicePath} 最后应答={(lastAnswer == null ? "(无)" : Log.Hex(lastAnswer))}");
        }
        else
        {
            ReportPollFailure(dev);
        }
        return null;
    }

    /// <summary>
    /// K7 的充电状态字节：0 未充电 / 1 充电中 / 2 已充满。未充电且 100% 按"已充满"收尾——
    /// 这条收尾规则全项目共用 <see cref="ChargingStateMap.FromChargingFlag"/>。
    /// </summary>
    private static ChargingState MapK7Charge(byte chargeStatus, int percent) => chargeStatus switch
    {
        0 => ChargingStateMap.FromChargingFlag(charging: false, percent),
        1 => ChargingStateMap.FromChargingFlag(charging: true, percent),
        2 => ChargingState.Full,
        _ => ChargingState.Unknown,
    };

    /// <summary>
    /// V9 Pro 耳机探测：发一条固定查询帧（见 <see cref="MchoseV9ProCodec"/>），750 毫秒内解析响应。
    /// 耳机与键鼠共用同一个插件开关——插件开关是**品牌级**的写入授权（`AGENTS.md` §9），不按型号再分一档。
    /// </summary>
    private static (string Protocol, int? Percent, ChargingState Charging)? TryHeadsetV9(HidDeviceHandle handle)
    {
        if (!IsWritesEnabled(SettingsKey)) return null;
        var percent = QueryHeadsetV9(handle);
        return percent.HasValue ? (ProtocolHeadsetV9, percent, ChargingState.Unknown) : null;
    }

    /// <summary>
    /// V9 Pro 轮询：只发一条查询帧。失败记账仍由 <see cref="PollDevice"/> 在发过帧的分支里
    /// 调 <see cref="BatteryPluginBase.ReportPollFailure"/>——本插件把 `CountPollFailures` 置了 false。
    /// </summary>
    private static (int? Percent, ChargingState Charging)? PollHeadsetV9(TrackedDevice dev)
    {
        if (!IsWritesEnabled(SettingsKey)) return null;
        var percent = QueryHeadsetV9(dev.Handle);
        return percent.HasValue ? (percent, ChargingState.Unknown) : null;
    }

    /// <summary>
    /// 写查询帧并等响应。**充电语义未知**，调用方一律给 <see cref="ChargingState.Unknown"/>，不猜。
    /// 无应答只在状态变化时记一条 Debug：耳机不在位时每 15 秒重探一次，逐次记录会把日志刷满。
    /// </summary>
    private static int? QueryHeadsetV9(HidDeviceHandle handle)
    {
        if (!handle.WriteOutput(MchoseV9ProCodec.CreateQuery(), TimeSpan.FromMilliseconds(500)))
            return null;

        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(750);
        while (DateTime.UtcNow < deadline)
        {
            var input = handle.ReadInput(TimeSpan.FromMilliseconds(300));
            if (input == null) continue;
            if (!MchoseV9ProCodec.TryParse(input, out int percent))
            {
                if (Log.IsEnabled(LogLevel.Debug)) Log.Debug($"迈从 V9 Pro 忽略非电量响应: {Log.Hex(input)}");
                continue;
            }
            Log.ResetOnce(LogKey(handle, LogKeyV9Silent));
            Log.Info($"迈从 V9 Pro 电量查询: 电量={percent} 标志=0x{input[3]:X2} 原始={Log.Hex(input)}");
            return percent;
        }

        Log.Once(LogLevel.Debug, LogKey(handle, LogKeyV9Silent),
            $"迈从 V9 Pro 查询无响应（耳机多半在关机/休眠；恢复应答后会再记一条）: {handle.Info.DevicePath}");
        return null;
    }

    /// <summary>
    /// 键盘 V1 协议（本机 dongle 41E4:2001 实测打通，G87 V2 接收器 3837:201B 同协议；与官方 M HUB WEB 抓包一致）。
    /// 帧格式与解析全部在 <see cref="MchoseV1Codec"/>（纯函数，有单测）；帧结构见
    /// docs/hardware/mchose.md §1。会话必须先开，否则 dongle 不响应；被崩溃进程占住会话时
    /// 所有人都会静音，需重新插拔释放。
    ///
    /// "无响应"只在状态变化时记日志：发现节拍每 15 秒重探一次，键盘睡着时逐次记录会把 app.log
    /// 刷满且无新信息。成功一次就 <see cref="Log.ResetOnce"/>。
    /// </summary>
    private static (int? Percent, ChargingState Charging)? TryKeyboardV1(HidDeviceHandle handle)
        => TryKeyboardV1(handle, requireKnownModel: false);

    /// <summary><paramref name="requireKnownModel"/> = true 用于与 AULA 共用的 3554:FA09 指纹：
    /// 只有会话 ACK 的型号密码落在迈从表里才认领，否则把接口留给 AulaPlugin（aula.md §1.1）。</summary>
    private static (int? Percent, ChargingState Charging)? TryKeyboardV1(HidDeviceHandle handle, bool requireKnownModel)
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

    /// <summary>qhw 轮询：握手一次 → getBattery 一条 → 解析（含 CZ 推送兜底）。</summary>
    private static (int? Percent, ChargingState Charging)? PollQhw(TrackedDevice dev)
    {
        if (!IsWritesEnabled(SettingsKey)) return null;

        if (!dev.HandshakeSent)
        {
            if (!SendQhwHandshake(dev.Handle))
            {
                Log.Debug($"迈从 qhw 轮询握手写失败: {dev.Handle.Info.DevicePath}");
                return null;
            }
            dev.HandshakeSent = true;
        }

        var frame = BuildQhwFrame(dev.Handle, CmdGetBattery);
        if (!dev.Handle.WriteOutput(frame, TimeSpan.FromMilliseconds(500)))
        {
            Log.Debug($"迈从 qhw 轮询写失败: {dev.Handle.Info.DevicePath}");
            return null;
        }

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            var input = dev.Handle.ReadInput(TimeSpan.FromMilliseconds(300));
            if (input == null) continue;

            var push = ParseCzPush(input);
            if (push.HasValue)
            {
                Log.Info($"迈从 qhw 轮询收到 CZ 推送: 电量={push.Value.Percent} 充电={push.Value.Charging}");
                return (push.Value.Percent,
                    push.Value.Charging ? ChargingState.Charging : ChargingState.Discharging);
            }

            var parsed = ParseQhwBatteryResponse(input, CmdGetBattery);
            if (parsed.HasValue)
            {
                Log.Info($"迈从 qhw 轮询解析: 电量={parsed.Value.Percent} 状态字节={parsed.Value.ChargeRaw}");
                return (parsed.Value.Percent, MapCharging(parsed.Value.ChargeRaw));
            }
        }
        Log.Debug($"迈从 qhw 轮询无响应: {dev.Handle.Info.DevicePath}");
        return null;
    }
}
