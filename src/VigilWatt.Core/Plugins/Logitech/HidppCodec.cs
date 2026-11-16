using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.Logitech;

/// <summary>毫伏→百分比锚点（降序）。</summary>
internal readonly record struct VoltagePoint(int Millivolts, int Percent);

/// <summary>短/长 HID++ 接口偏好。Bolt 必须走长；Unifying 常只在短接口应答。</summary>
internal enum HidppReportPreference
{
    /// <summary>有长接口时跳过同 PID 短接口（Bolt / Lightspeed 默认）。</summary>
    PreferLong,

    /// <summary>保留短接口；有短接口时跳过同 PID 长接口（Unifying / Nano）。</summary>
    PreferShort,

    /// <summary>长短都试，不因同伴接口跳过。</summary>
    Both,
}

/// <summary>
/// 功 能： HID++ 响应解析（纯逻辑，零 I/O，可单测）。
/// 2026/10/08 11:27:03 xliu
/// 报文层与各特征编码见 docs/hardware/brands/logitech.md。
/// </summary>
internal static class HidppCodec
{
    /// <summary>短报文 report ID（7 字节接口）。</summary>
    public const byte ShortReportId = 0x10;

    /// <summary>长报文 report ID（20 字节接口，Bolt / Unifying 长通道）。</summary>
    public const byte LongReportId = 0x11;

    /// <summary>长报文的最小输出缓冲长度（含 report ID）。</summary>
    public const int LongReportLength = 20;

    /// <summary>HID++ 2.0 Battery Voltage 特征。</summary>
    public const ushort FeatureBatteryVoltage = 0x1001;

    /// <summary>HID++ 2.0 ADC Measurement 特征（G 系耳机等）。</summary>
    public const ushort FeatureAdcMeasurement = 0x1F20;

    /// <summary>HID++ 1.0 长寄存器 0xB5（RECEIVER_INFO）的"接收器信息"子寄存器：序列号 + 配对槽位容量。</summary>
    public const byte ReceiverInfoSubRegister = 0x03;

    /// <summary>按接口元数据选 report ID：caps 的非 0 OutputReportId 优先；否则按输出长度推断（≥20 → 0x11，否则 0x10）。</summary>
    public static byte SelectReportId(ushort outputReportLength, byte? outputReportId)
    {
        if (outputReportId is > 0) return outputReportId.Value;
        return outputReportLength >= LongReportLength ? LongReportId : ShortReportId;
    }

    /// <summary>是否应按长报文组装（report 0x11，数据区 18 字节）。</summary>
    public static bool IsLongMessage(byte reportId) => reportId == LongReportId;

    /// <summary>
    /// 组装 HID++ 2.0 的 featureIdAndFunc：高字节=feature 下标，低字节高 4 位=func（<c>HidppIO.Request</c> 再把低 4 位换成 SwId）。
    /// ⚠️ 切勿写成 <c>(feature&lt;&lt;8)|func</c>——func=1 会被 patchSw 抹成 func 0（真机：Unified Battery 一直读到 Capabilities 的 15%，读不到 GetStatus 的 90%/充电）。
    /// </summary>
    public static ushort FeatureFunc(byte featureIndex, byte func)
        => (ushort)((featureIndex << 8) | ((func & 0x0F) << 4));

    /// <summary>Root.GetFeature 响应里的 feature 下标（参数字节 [2]）；长度不足 → 0（不支持）。
    /// 旧 bug：误读 resp[0]（恒为 Root feature=0），导致 0x1004/0x1000 永远"不支持"。</summary>
    public static byte ParseFeatureIndex(ReadOnlySpan<byte> resp)
        => resp.Length >= 3 ? resp[2] : (byte)0;

    /// <summary>字符数上限，只防坏字节、不是协议值：取小了会把长营销名整条丢掉（logitech.md §1.2）。</summary>
    public const int MaxDeviceNameLength = 64;

    /// <summary>DEVICE_NAME func 0：参数 [0] = 字符数。0 或超过上限按无效丢弃。</summary>
    public static bool TryParseDeviceNameLength(ReadOnlySpan<byte> resp, out int length)
    {
        length = 0;
        if (resp.Length < 3) return false;
        int count = resp[2];
        if (count is 0 or > MaxDeviceNameLength) return false;
        length = count;
        return true;
    }

    /// <summary>DEVICE_NAME func 1：回一块正文（不回显起始下标）。长报文一帧 16 字符、短报文 3 字符。</summary>
    public static bool TryParseDeviceNameChunk(ReadOnlySpan<byte> resp, int take, out string chunk)
    {
        chunk = "";
        if (resp.Length < 3 || take <= 0) return false;
        int available = Math.Min(take, resp.Length - 2);
        if (available <= 0) return false;
        var chars = new char[available];
        for (int i = 0; i < available; i++) chars[i] = (char)resp[2 + i];
        chunk = new string(chars);
        return true;
    }

    /// <summary>0x1004 getStatus：参数 [0]=电量%、[2]=充电状态。状态码 4 是**充电故障**，不能报成充电中（logitech.md §1）。</summary>
    public static bool TryParseUnifiedBattery(ReadOnlySpan<byte> resp, out int percent, out ChargingState charging)
    {
        charging = ChargingState.Unknown;
        if (!TryParseBatteryPercent(resp, out percent)) return false;
        charging = resp[4] switch
        {
            0 => ChargingState.Discharging,
            1 or 2 => ChargingState.Charging,   // 2 = 慢充，UI 只有四档
            3 => ChargingState.Full,
            _ => ChargingState.Unknown,          // 4 = 充电故障
        };
        return true;
    }

    /// <summary>0x1000 getBatteryLevelStatus：同一位置的状态码是**另一套**枚举（2=几乎满、4=慢充、5/6/7=错误，logitech.md §1）。</summary>
    public static bool TryParseBatteryStatus(ReadOnlySpan<byte> resp, out int percent, out ChargingState charging)
    {
        charging = ChargingState.Unknown;
        if (!TryParseBatteryPercent(resp, out percent)) return false;
        charging = resp[4] switch
        {
            0 => ChargingState.Discharging,
            1 or 2 or 4 => ChargingState.Charging, // 2 = 几乎满、4 = 慢充
            3 => ChargingState.Full,
            _ => ChargingState.Unknown,            // 5/6/7 = 无效电池 / 过热 / 其它错误
        };
        return true;
    }

    /// <summary>两个电量特征共用的百分比校验：长度、0..100；0% 按占位丢弃（与其它插件的 InvalidPercentFloor 一致）。</summary>
    private static bool TryParseBatteryPercent(ReadOnlySpan<byte> resp, out int percent)
    {
        percent = 0;
        if (resp.Length < 5) return false;
        int pct = resp[2];
        if (pct is 0 or > 100) return false;
        percent = pct;
        return true;
    }

    /// <summary>Battery Voltage (0x1001) 参数：大端毫伏 + flags（bit7=充电中；低 3 位 1=充满；bit5=危急）。</summary>
    public static bool TryParseBatteryVoltage(
        ReadOnlySpan<byte> resp,
        IReadOnlyList<VoltagePoint> curve,
        out int percent,
        out ChargingState charging,
        out int millivolts)
    {
        percent = 0;
        charging = ChargingState.Unknown;
        millivolts = 0;
        if (resp.Length < 5) return false;
        millivolts = (resp[2] << 8) | resp[3];
        if (millivolts is < 2000 or > 5500) return false;

        byte flags = resp[4];
        if ((flags & 0x80) != 0)
        {
            charging = (flags & 0x07) == 1
                ? ChargingState.Full
                : ChargingState.Charging;
        }
        else
        {
            charging = ChargingState.Discharging;
        }

        percent = PercentFromMillivolts(millivolts, curve);
        return percent > 0 || millivolts <= curveFloor(curve);
    }

    /// <summary>ADC Measurement (0x1F20) 参数：大端毫伏 + flags（0x01 放电 / 0x03 充电 / 0x07 充满）。</summary>
    public static bool TryParseAdcMeasurement(
        ReadOnlySpan<byte> resp,
        IReadOnlyList<VoltagePoint> curve,
        out int percent,
        out ChargingState charging,
        out int millivolts)
    {
        percent = 0;
        charging = ChargingState.Unknown;
        millivolts = 0;
        if (resp.Length < 5) return false;
        millivolts = (resp[2] << 8) | resp[3];
        if (millivolts is < 2000 or > 5500) return false;

        charging = resp[4] switch
        {
            0x03 => ChargingState.Charging,
            0x07 => ChargingState.Full,
            0x01 => ChargingState.Discharging,
            _ => (resp[4] & 0x02) != 0 ? ChargingState.Charging : ChargingState.Discharging,
        };

        percent = PercentFromMillivolts(millivolts, curve);
        return percent > 0 || millivolts <= curveFloor(curve);
    }

    /// <summary>毫伏→百分比：按降序锚点线性插值；曲线为空返回 0。</summary>
    public static int PercentFromMillivolts(int millivolts, IReadOnlyList<VoltagePoint> curve)
    {
        if (curve == null || curve.Count == 0) return 0;
        if (millivolts >= curve[0].Millivolts) return curve[0].Percent;
        var last = curve[curve.Count - 1];
        if (millivolts <= last.Millivolts) return last.Percent;

        for (int i = 0; i < curve.Count - 1; i++)
        {
            var hi = curve[i];
            var lo = curve[i + 1];
            if (millivolts <= hi.Millivolts && millivolts >= lo.Millivolts)
            {
                int span = hi.Millivolts - lo.Millivolts;
                if (span <= 0) return hi.Percent;
                double t = (double)(millivolts - lo.Millivolts) / span;
                return (int)Math.Round(lo.Percent + t * (hi.Percent - lo.Percent));
            }
        }
        return 0;
    }

    /// <summary>配对信息里的设备类型字节（低 4 位）→ UI 用的 <see cref="DeviceKind"/>（协议常量，不按产品名增长）。</summary>
    public static DeviceKind MapPairingDeviceKind(byte kindByte) => (kindByte & 0x0F) switch
    {
        0x01 => DeviceKind.Keyboard,   // keyboard
        0x02 => DeviceKind.Mouse,      // mouse
        0x03 => DeviceKind.Keyboard,   // numpad
        0x04 => DeviceKind.Unknown,    // presenter（无专用 Kind，UI 按名兜底）
        0x07 => DeviceKind.Unknown,    // remote
        0x08 => DeviceKind.Mouse,      // trackball
        0x09 => DeviceKind.Unknown,    // touchpad
        0x0A => DeviceKind.Unknown,    // tablet
        0x0B => DeviceKind.Gamepad,
        0x0C => DeviceKind.Gamepad,    // joystick
        0x0D => DeviceKind.Headset,
        _ => DeviceKind.Unknown,
    };

    /// <summary>
    /// `0x0005` Device Name 的 func 2（getDeviceType）返回的**营销类型** → UI 用的 <see cref="DeviceKind"/>。
    /// 比配对寄存器权威（后者会把触控板/演示器报成键盘鼠标），所以优先用它；无对应 Kind 的类型回 Unknown 交给名字兜底。
    /// </summary>
    public static DeviceKind MapDeviceTypeKind(byte deviceType) => deviceType switch
    {
        0 => DeviceKind.Keyboard,   // keyboard
        2 => DeviceKind.Keyboard,   // numpad
        3 => DeviceKind.Mouse,
        5 => DeviceKind.Mouse,      // trackball
        8 => DeviceKind.Headset,
        10 => DeviceKind.Gamepad,   // steering wheel
        11 => DeviceKind.Gamepad,   // joystick
        12 => DeviceKind.Gamepad,
        14 => DeviceKind.Speaker,
        _ => DeviceKind.Unknown,
    };

    /// <summary>
    /// 接收器信息（RECEIVER_INFO 子寄存器 0x03）：参数区 [1..4]=序列号、[6]=配对槽位容量；相对 feature 缓冲即 resp[3..6]、resp[8]。
    /// 容量只接受 1..6（Unifying / Bolt 协议上限），越界当作"没读到"——由目录常量兜底，判据同 Solaar `extract_max_devices` 与 OpenLogi `get_receiver_info`。
    /// 额外校验 resp[2] 是子寄存器回显：偏移若与固件不符（例如把参数区当成了整个缓冲）宁可读不到，也不要读出一位错位的容量。
    /// </summary>
    public static bool TryParseReceiverSlotCount(ReadOnlySpan<byte> resp, out byte slotCount)
    {
        slotCount = 0;
        if (resp.Length < 9) return false;
        if (resp[2] != ReceiverInfoSubRegister) return false;
        byte value = resp[8];
        if (value is < 1 or > 6) return false;
        slotCount = value;
        return true;
    }

    /// <summary>Unifying 风格配对信息（RECEIVER_INFO 0x2N）：参数区 [3..4]=WPID，[7]=kind（相对 feature 缓冲即 resp[5..6]、resp[9]）。</summary>
    public static bool TryParseUnifyingPairing(
        ReadOnlySpan<byte> resp, out ushort wirelessPid, out DeviceKind kind)
    {
        wirelessPid = 0;
        kind = DeviceKind.Unknown;
        if (resp.Length < 10) return false;
        wirelessPid = (ushort)((resp[5] << 8) | resp[6]);
        kind = MapPairingDeviceKind(resp[9]);
        return wirelessPid != 0 || kind != DeviceKind.Unknown;
    }

    /// <summary>Bolt 风格配对信息（RECEIVER_INFO 0x5N）：参数区 [1]=kind，[2..3]=WPID（小端序）；相对 feature 缓冲即 resp[3]、resp[4..5]（交换后成大端 WPID）。</summary>
    public static bool TryParseBoltPairing(
        ReadOnlySpan<byte> resp, out ushort wirelessPid, out DeviceKind kind)
    {
        wirelessPid = 0;
        kind = DeviceKind.Unknown;
        if (resp.Length < 6) return false;
        kind = MapPairingDeviceKind(resp[3]);
        wirelessPid = (ushort)((resp[5] << 8) | resp[4]);
        return wirelessPid != 0 || kind != DeviceKind.Unknown;
    }

    /// <summary>按报文偏好决定是否跳过当前接口。PreferLong（默认/未知 PID）：有长同伴则跳短（Bolt）；PreferShort：有短同伴则跳长（Unifying 常只在短通道应答）；Both：不跳过。</summary>
    public static bool ShouldSkipInterface(
        ushort vendorId, ushort productId, ushort outputReportLength,
        IEnumerable<(ushort Vid, ushort Pid, ushort OutLen)> peers,
        HidppReportPreference preference)
    {
        bool isShort = outputReportLength < LongReportLength;
        bool isLong = outputReportLength >= LongReportLength;
        // 32 字节扩展集合是另一条集合，HID++ 2.0 走 20 字节那条；两条都认领 = 同一接收器两个会话（logitech.md §1.2）
        bool isExtended = outputReportLength > LongReportLength;

        switch (preference)
        {
            case HidppReportPreference.Both:
                return false;

            case HidppReportPreference.PreferShort:
                if (!isLong) return false;
                foreach (var p in peers)
                {
                    if (p.Vid == vendorId && p.Pid == productId && p.OutLen < LongReportLength)
                        return true;
                }
                return false;

            case HidppReportPreference.PreferLong:
            default:
                if (isExtended)
                {
                    foreach (var p in peers)
                    {
                        if (p.Vid == vendorId && p.Pid == productId && p.OutLen == LongReportLength)
                            return true;
                    }
                    return false;
                }
                if (!isShort) return false;
                foreach (var p in peers)
                {
                    if (p.Vid == vendorId && p.Pid == productId && p.OutLen >= LongReportLength)
                        return true;
                }
                return false;
        }
    }

    /// <summary>兼容旧测试：等价于 PreferLong。</summary>
    public static bool ShouldSkipShortInterface(
        ushort vendorId, ushort productId, ushort outputReportLength,
        IEnumerable<(ushort Vid, ushort Pid, ushort OutLen)> peers)
        => ShouldSkipInterface(vendorId, productId, outputReportLength, peers,
            HidppReportPreference.PreferLong);

    private static int curveFloor(IReadOnlyList<VoltagePoint> curve)
        => curve.Count == 0 ? 0 : curve[curve.Count - 1].Millivolts;
}
