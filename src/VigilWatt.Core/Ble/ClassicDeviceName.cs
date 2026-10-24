using System.Text.RegularExpressions;

namespace VigilWatt.Core.Ble;

/// <summary>
/// 功 能： 经典蓝牙显示名的纯逻辑：识别"占位名"（WinRT 在友好名未解析时会拿 AEP 设备 ID 当名字，真机实测）。
/// 2026/10/06 15:53:36 xliu
/// 详见 docs/project/handoff.md「经典蓝牙设备显示名修成原始 ID」。
/// </summary>
internal static class ClassicDeviceName
{
    /// <summary>裸 MAC 形态：<c>0c:ae:bd:c6:cd:a7</c> 或 <c>0C-AE-BD-C6-CD-A7</c>。</summary>
    private static readonly Regex MacLike = new(
        @"^[0-9a-fA-F]{2}([:-][0-9a-fA-F]{2}){5}$", RegexOptions.Compiled);

    /// <summary>是不是"占位名"（空、AEP 设备 ID、设备接口路径、裸 MAC）——这些都不能当显示名用。</summary>
    public static bool IsPlaceholder(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return true;
        string n = name.Trim();

        if (n.Contains("Bluetooth#", StringComparison.OrdinalIgnoreCase)) return true; // AEP 设备 ID
        if (n.StartsWith(@"\\?\", StringComparison.Ordinal)) return true;              // 设备接口路径
        if (n.Contains("&0&", StringComparison.Ordinal)) return true;                  // 接口路径片段
        if (MacLike.IsMatch(n)) return true;                                          // 裸 MAC

        return false;
    }

    /// <summary>MAC 文本（大写、冒号分隔）：<c>0c:ae:bd:c6:cd:a7</c> → <c>0C:AE:BD:C6:CD:A7</c>。</summary>
    public static string MacText(string mac) => mac.ToUpperInvariant().Replace('-', ':');

    /// <summary>任何名字都拿不到时的兜底显示名（总比一长串 AEP ID 强）。</summary>
    public static string FallbackName(string? mac)
        => string.IsNullOrEmpty(mac) ? "经典蓝牙设备" : $"经典蓝牙设备 {MacText(mac)}";
}
