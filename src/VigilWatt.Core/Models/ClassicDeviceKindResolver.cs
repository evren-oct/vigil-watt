namespace VigilWatt.Core.Models;

/// <summary>
/// 功 能： 经典蓝牙设备类型判定（纯逻辑，可单测）：按"Windows 设备类别 → 服务 UUID → 显示名"三级判定， 猜不出返回 <see cref="DeviceKind.Unknown"/>——**绝不退回 Headset**（那正是蓝牙鼠标被显示成耳机的根因）。
/// 2026/10/09 17:22:33 xliu
/// 详见 docs/dev/runtime-pitfalls.md §3.1。
/// </summary>
public static class ClassicDeviceKindResolver
{
    /// <summary>音频类服务 UUID（8 位短式）：HSP / HFP / A2DP / AVRCP。</summary>
    private static readonly string[] AudioServiceUuids =
    {
        "00001108",   // Headset (HSP)
        "0000110a",   // Audio Source (A2DP)
        "0000110b",   // Audio Sink (A2DP)
        "0000110c",   // A/V Remote Control Target
        "0000110e",   // A/V Remote Control
        "00001112",   // Headset Audio Gateway (HSP AG)
        "0000111e",   // Handsfree (HFP HF)
        "0000111f",   // Handsfree Audio Gateway (HFP AG)
    };

    /// <summary>判定设备类型。<paramref name="categories"/> / <paramref name="serviceUuids"/> 可为 null 或空（逐级回退）。</summary>
    public static DeviceKind Resolve(
        string? displayName,
        IReadOnlyList<string>? categories = null,
        IReadOnlyCollection<string>? serviceUuids = null)
    {
        var byCategory = FromCategories(categories);
        if (byCategory != DeviceKind.Unknown) return byCategory;

        // 有音频服务 = 确实是音频设备（与名字有无关键字无关）；"音频 + HID 按键"并存的游戏耳机也归这里
        if (HasAudioService(serviceUuids))
        {
            return DeviceKindGuesser.Guess(displayName) == DeviceKind.Speaker
                ? DeviceKind.Speaker
                : DeviceKind.Headset;
        }

        // HID 设备（HIDP 服务）只能到这一步才知道是鼠标还是键盘；猜不出就 Unknown
        return DeviceKindGuesser.Guess(displayName);
    }

    /// <summary>服务 UUID → 协议描述（<see cref="DeviceStatus.Protocol"/>，UI 副标题会显示）。</summary>
    public static string DescribeProtocol(IReadOnlyCollection<string>? serviceUuids)
    {
        if (HasAudioService(serviceUuids)) return "经典蓝牙 A2DP/HFP";
        if (HasService(serviceUuids, "00001124")) return "经典蓝牙 HIDP";
        return "经典蓝牙";
    }

    /// <summary>Windows 设备类别 → 类型。厂商可自定义取值（XSD 允许任意字符串），所以按关键字匹配而不是穷举枚举。</summary>
    private static DeviceKind FromCategories(IReadOnlyList<string>? categories)
    {
        if (categories == null) return DeviceKind.Unknown;

        foreach (var raw in categories)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var c = raw.ToLowerInvariant();

            if (c.Contains("mouse", StringComparison.Ordinal) ||
                c.Contains("trackball", StringComparison.Ordinal))
                return DeviceKind.Mouse;
            if (c.Contains("keyboard", StringComparison.Ordinal))
                return DeviceKind.Keyboard;
            if (c.Contains("gamepad", StringComparison.Ordinal) ||
                c.Contains("gaming", StringComparison.Ordinal) ||
                c.Contains("joystick", StringComparison.Ordinal) ||
                c.Contains("controller", StringComparison.Ordinal))
                return DeviceKind.Gamepad;
            if (c.Contains("headphone", StringComparison.Ordinal) ||
                c.Contains("headset", StringComparison.Ordinal) ||
                c.Contains("earbud", StringComparison.Ordinal) ||
                c.Contains("earphone", StringComparison.Ordinal))
                return DeviceKind.Headset;
            if (c.Contains("speaker", StringComparison.Ordinal))
                return DeviceKind.Speaker;
        }

        return DeviceKind.Unknown;
    }

    private static bool HasAudioService(IReadOnlyCollection<string>? serviceUuids)
    {
        if (serviceUuids == null) return false;
        foreach (var uuid in serviceUuids)
            foreach (var audio in AudioServiceUuids)
                if (string.Equals(uuid, audio, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static bool HasService(IReadOnlyCollection<string>? serviceUuids, string uuid)
    {
        if (serviceUuids == null) return false;
        foreach (var u in serviceUuids)
            if (string.Equals(u, uuid, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
