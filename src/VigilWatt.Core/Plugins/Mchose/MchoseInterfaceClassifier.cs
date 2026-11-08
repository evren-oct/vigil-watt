namespace VigilWatt.Core.Plugins.Mchose;

/// <summary>迈从家族接口的去向。<see cref="None"/> = 不是本插件的接口。</summary>
public enum MchoseInterfaceKind
{
    /// <summary>不是迈从家族的配置通道。</summary>
    None,

    /// <summary>键盘 V1 会话通道（20 字节 `0x13`，G87 / G87 V2 接收器）。</summary>
    KeyboardV1,

    /// <summary>鼠标 V2 世代厂商接口（≥60 字节，含 report ID）。</summary>
    MouseV2,

    /// <summary>QHW 通道（report `0x4D`）：CZ 族键盘与多数鼠标，含共用 dongle。</summary>
    Qhw,

    /// <summary>0x4D 命令通道（64 字节 output `0x4D`）：`0x1018` 及目录登记的通用 dongle——通道名不是型号名。</summary>
    Mouse0x4D,

    /// <summary>鼠标 Gmouse 主动查询接口（65 字节，usagePage 0xFF01/usage 0x10，3837:4245 等）。</summary>
    Gmouse,

    /// <summary>V9 Pro 耳机（64 字节、reportId `0x55`）。</summary>
    HeadsetV9,

    /// <summary>X9 耳机（同帧结构，VID/PID 为 `3837:6045`，与键鼠同号段）。</summary>
    HeadsetX9,

    /// <summary>A5 V2 Ultra 的厂商 Feature 通道（`3837:100C`）——归 <c>MchoseA5Plugin</c>，主插件必须让路。</summary>
    MouseA5,
}

/// <summary>
/// 功 能： 按接口指纹判定一个 HID 接口属于迈从家族的哪条链路。
/// 2026/10/19 21:10:00 xliu
/// 纯函数、零 I/O：插件分流与单测共用同一份判据，避免"判据写在两处、改一处忘一处"。
/// 帧与判据来源见 docs/hardware/mchose.md §1、§1.6，以及 docs/hardware/mchose-v9-pro.md。
/// </summary>
public static class MchoseInterfaceClassifier
{
    /// <summary>新世代厂 ID（G87 V2 接收器 3837:201B、A7 V2 Pro 接收器 3837:100A）。</summary>
    public const ushort MchoseVid = 0x3837;

    /// <summary>老款 G87 世代（41E4:2001），协议同为 V1 会话。</summary>
    public const ushort MchoseLegacyVid = 0x41E4;

    /// <summary>键盘 V1 会话通道的报告长度（进出各 20 字节）。</summary>
    private const int KeyboardReportLength = 20;

    /// <summary>厂商页报告长度下限：QHW 帧按接口长度补零，只保证不是消费类小接口。</summary>
    private const int MinVendorReportLength = 20;

    /// <summary>
    /// 判定接口去向。<b>耳机必须排在最前</b>：它也是 64 字节输出，落到鼠标分支会收到 `0x11/0x06` 触发帧。
    /// 品牌排除（罗技/雷柏/北通/英菲克/HECATE/AULA 的 VID）留在插件里做纵深防御，这里只管迈从自己的指纹。
    /// </summary>
    public static MchoseInterfaceKind Classify(ushort vendorId, ushort productId, string path,
        ushort usagePage, ushort usage, ushort inputLength, ushort outputLength,
        ushort featureLength, byte? outputReportId)
    {
        // 耳机是独立 VID/PID，判据与键鼠完全独立；同 VID 的其它 PID 一律不认（不做型号推断）
        if (vendorId == MchoseV9ProCodec.VendorId)
            return MchoseV9ProCodec.IsSupportedInterface(vendorId, productId, path, usagePage, usage,
                inputLength, outputLength, featureLength, outputReportId)
                ? MchoseInterfaceKind.HeadsetV9
                : MchoseInterfaceKind.None;

        if (vendorId != MchoseVid && vendorId != MchoseLegacyVid) return MchoseInterfaceKind.None;

        // X9 的 VID 与键鼠同号段，必须排在鼠标分支之前：它也是 64 字节输出，
        // 落到鼠标分支会往耳机发 0x11/0x06 触发帧。
        if (MchoseX9Codec.IsSupportedInterface(vendorId, productId, path, usagePage, usage,
                inputLength, outputLength, featureLength, outputReportId))
            return MchoseInterfaceKind.HeadsetX9;

        // consumer/system/标准键盘接口即使产品名含 MCHOSE 也不是配置通道，必须是厂商用法 + 可用 I/O
        if (usagePage < 0xFF00 && usage < 0xFF00) return MchoseInterfaceKind.None;

        // A5 靠**身份**归属（不是靠形态猜）：它走厂商 Feature 通道，归 MchoseA5Plugin。
        // 主插件必须让路，否则会把 feature-only 集合当成鼠标接口、往上写 0x4D 握手帧。
        if (MchoseDeviceCatalog.TryGet(vendorId, productId, out var a5) && a5.Protocol == "mouse-a5")
            return featureLength >= MchoseA5Codec.PayloadLength + 1
                ? MchoseInterfaceKind.MouseA5
                : MchoseInterfaceKind.None;

        // 目录优先：VID/PID 精确命中时直接给协议族，不必靠形态猜（新设备靠这张表免适配）。
        // 耳机由上面的精确判据负责，这里跳过，避免同一条接口被重复认领。
        if (MchoseDeviceCatalog.TryGet(vendorId, productId, out var entry)
            && KindFromProtocol(entry.Protocol) is var catalogKind
            && catalogKind is not (MchoseInterfaceKind.None
                or MchoseInterfaceKind.HeadsetV9 or MchoseInterfaceKind.HeadsetX9)
            && MatchesShape(catalogKind, usagePage, usage, inputLength, outputLength))
            return catalogKind;

        if (outputLength == KeyboardReportLength && inputLength == KeyboardReportLength)
            return MchoseInterfaceKind.KeyboardV1;

        // 0x4D 通道必须排在下面的"≥60 字节只走鼠标"之前：它正好也是 64/64，
        // 落到 V2 分支会被当成"等 E2 状态帧"的接收器，白等两轮、一帧读不到。
        // ① `0x1018`：唯一有真机指纹的一颗，按完整指纹收窄（同 dongle 的 61 字节旁路接口不会被误认）。
        if (Mchose0x4DCodec.IsSupportedInterface(vendorId, productId, usagePage, usage, inputLength, outputLength))
            return MchoseInterfaceKind.Mouse0x4D;

        // ② 目录登记的其它 0x4D 传输通道（0x1014 / 0x1016 / 0x101C / 0x101D）：没有真机指纹，
        //    用「厂商页 + 双向 64 字节」的宽松判据。宁可漏认（退回 V2 兜底）也不按 VID 泛化。
        //    `0x1018` 不参与这条：它只认精确指纹，否则放宽后同 dongle 的其它 64/64 集合也会被当命令通道。
        if (productId != Mchose0x4DCodec.PidDongle8K
            && MchoseDeviceCatalog.TryGetTransport(vendorId, productId, out var transport)
            && transport.Protocol == Mchose0x4DCodec.ProtocolName
            && Mchose0x4DCodec.IsVendorTransportInterface(usagePage, usage, inputLength, outputLength))
            return MchoseInterfaceKind.Mouse0x4D;

        // Gmouse 主动查询接口：同样是 ≥60 字节，但 usagePage=0xFF01/usage=0x10，与 V2 被动 E2 链路
        // 靠完整指纹区分。必须排在「≥60 字节只走鼠标」之前，否则 G3 V2 会被当成等 E2 帧的接收器白等。
        if (MchoseGmouseCodec.IsSupportedInterface(vendorId, productId, usagePage, usage, inputLength, outputLength))
            return MchoseInterfaceKind.Gmouse;

        // 鼠标接收器厂商接口 65 = 64 数据 + 1 report ID；V2 世代长度差异大，按 ≥60 收口
        if (outputLength >= 60 && inputLength >= 60) return MchoseInterfaceKind.MouseV2;

        return MchoseInterfaceKind.None;
    }

    /// <summary>目录里的协议族名 → 枚举；未知协议返回 None，由调用方回落形态判定。</summary>
    private static MchoseInterfaceKind KindFromProtocol(string protocol) => protocol switch
    {
        "keyboard-v1" => MchoseInterfaceKind.KeyboardV1,
        "qhw" => MchoseInterfaceKind.Qhw,
        "mchose-0x4d" => MchoseInterfaceKind.Mouse0x4D,
        "gmouse" => MchoseInterfaceKind.Gmouse,
        "headset-v9" => MchoseInterfaceKind.HeadsetV9,
        "headset-x9" => MchoseInterfaceKind.HeadsetX9,
        _ => MchoseInterfaceKind.None,
    };

    /// <summary>
    /// 目录给的协议族是否与接口形态相容。**必须校验**：同型号还有旁路接口（如 K7 的 `0xFF0B` 61 字节），
    /// 不校验会把它们也认成命令通道、重复认领。
    /// </summary>
    private static bool MatchesShape(MchoseInterfaceKind kind,
        ushort usagePage, ushort usage, ushort inputLength, ushort outputLength) => kind switch
    {
        MchoseInterfaceKind.KeyboardV1
            => outputLength == KeyboardReportLength && inputLength == KeyboardReportLength,
        MchoseInterfaceKind.Qhw
            => outputLength >= MinVendorReportLength && inputLength >= MinVendorReportLength,
        MchoseInterfaceKind.Mouse0x4D
            => usagePage == Mchose0x4DCodec.UsagePage && usage == Mchose0x4DCodec.Usage
               && inputLength >= Mchose0x4DCodec.FrameLength && outputLength >= Mchose0x4DCodec.FrameLength,
        MchoseInterfaceKind.Gmouse
            => usagePage == MchoseGmouseCodec.UsagePage && usage == MchoseGmouseCodec.Usage
               && inputLength >= MchoseGmouseCodec.FrameLength && outputLength >= MchoseGmouseCodec.FrameLength,
        _ => false,
    };
}
