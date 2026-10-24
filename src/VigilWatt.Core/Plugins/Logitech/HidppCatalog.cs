using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VigilWatt.Core.Plugins.Logitech;

/// <summary>
/// 功 能： 嵌入的罗技 HID++ 目录：接收器槽位/报文偏好 + 毫伏→百分比曲线；是补充不是白名单，未收录 PID 仍按默认行为打开 HID++ 接口。
/// 2026/10/08 11:27:03 xliu
/// </summary>
internal static class HidppCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly Lazy<CatalogData> Data = new(Load);

    public static ReceiverInfo? FindReceiver(ushort productId)
        => Data.Value.Receivers.TryGetValue(productId, out var r) ? r : null;

    public static bool IsReceiver(ushort productId)
        => FindReceiver(productId) != null || productId is >= 0xC500 and <= 0xC5FF;

    /// <summary>未收录接收器的默认值：最多 6 槽（Unifying/Bolt 上限），报文偏好按"有长则跳短"旧规则。</summary>
    public static int MaxSlots(ushort productId)
        => FindReceiver(productId)?.MaxSlots ?? 6;

    public static HidppReportPreference ReportPreference(ushort productId)
        => FindReceiver(productId)?.ReportPreference ?? HidppReportPreference.PreferLong;

    public static ReceiverFamily Family(ushort productId)
        => FindReceiver(productId)?.Family ?? ReceiverFamily.Unknown;

    /// <summary>按 wireless PID 选曲线；没有则用默认锂电 / ADC 专用曲线。</summary>
    public static IReadOnlyList<VoltagePoint> CurveFor(ushort? wirelessPid, bool adcFeature)
    {
        var data = Data.Value;
        if (wirelessPid is { } wpid &&
            data.WpidCurves.TryGetValue(wpid, out var named) &&
            data.Curves.TryGetValue(named, out var byWpid))
            return byWpid;

        if (adcFeature && data.Curves.TryGetValue(data.AdcCurve, out var adc))
            return adc;

        return data.Curves.TryGetValue(data.DefaultCurve, out var def)
            ? def
            : Array.Empty<VoltagePoint>();
    }

    private static CatalogData Load()
    {
        var asm = Assembly.GetExecutingAssembly();
        const string name = "VigilWatt.Core.Plugins.Logitech.logitech-hidpp.json";
        using var stream = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"嵌入资源缺失: {name}");
        var dto = JsonSerializer.Deserialize<CatalogDto>(stream, JsonOptions)
            ?? throw new InvalidOperationException("logitech-hidpp.json 解析失败");

        var receivers = new Dictionary<ushort, ReceiverInfo>();
        foreach (var r in dto.Receivers ?? [])
        {
            if (!TryParseHexPid(r.Pid, out var pid)) continue;
            receivers[pid] = new ReceiverInfo(
                pid,
                ParseFamily(r.Family),
                r.MaxSlots is > 0 and <= 6 ? r.MaxSlots : 6,
                ParsePreference(r.ReportPreference));
        }

        var curves = new Dictionary<string, IReadOnlyList<VoltagePoint>>(StringComparer.OrdinalIgnoreCase);
        if (dto.VoltageCurves != null)
        {
            foreach (var (key, pts) in dto.VoltageCurves)
            {
                if (pts == null || pts.Count == 0) continue;
                var list = pts
                    .Where(p => p.Mv > 0 && p.Pct is >= 0 and <= 100)
                    .OrderByDescending(p => p.Mv)
                    .Select(p => new VoltagePoint(p.Mv, p.Pct))
                    .ToArray();
                if (list.Length > 0)
                    curves[key] = list;
            }
        }

        var wpidCurves = new Dictionary<ushort, string>();
        if (dto.WpidCurves != null)
        {
            foreach (var (key, curveName) in dto.WpidCurves)
            {
                if (string.IsNullOrWhiteSpace(curveName)) continue;
                if (TryParseHexPid(key, out var wpid))
                    wpidCurves[wpid] = curveName;
            }
        }

        return new CatalogData(
            receivers,
            curves,
            wpidCurves,
            string.IsNullOrWhiteSpace(dto.DefaultCurve) ? "defaultLithium" : dto.DefaultCurve!,
            string.IsNullOrWhiteSpace(dto.AdcCurve) ? "adcLithium" : dto.AdcCurve!);
    }

    private static bool TryParseHexPid(string? text, out ushort pid)
    {
        pid = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        return ushort.TryParse(text.Trim(), System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture, out pid);
    }

    private static ReceiverFamily ParseFamily(string? s) => (s ?? "").Trim().ToLowerInvariant() switch
    {
        "unifying" => ReceiverFamily.Unifying,
        "bolt" => ReceiverFamily.Bolt,
        "lightspeed" => ReceiverFamily.Lightspeed,
        "nano" => ReceiverFamily.Nano,
        "27mhz" => ReceiverFamily.Legacy27Mhz,
        _ => ReceiverFamily.Unknown,
    };

    private static HidppReportPreference ParsePreference(string? s) => (s ?? "").Trim().ToLowerInvariant() switch
    {
        "short" => HidppReportPreference.PreferShort,
        "both" => HidppReportPreference.Both,
        "long" => HidppReportPreference.PreferLong,
        _ => HidppReportPreference.PreferLong,
    };

    private sealed record CatalogData(
        IReadOnlyDictionary<ushort, ReceiverInfo> Receivers,
        IReadOnlyDictionary<string, IReadOnlyList<VoltagePoint>> Curves,
        IReadOnlyDictionary<ushort, string> WpidCurves,
        string DefaultCurve,
        string AdcCurve);

    private sealed class CatalogDto
    {
        [JsonPropertyName("receivers")]
        public List<ReceiverDto>? Receivers { get; set; }

        [JsonPropertyName("voltageCurves")]
        public Dictionary<string, List<VoltagePointDto>>? VoltageCurves { get; set; }

        [JsonPropertyName("defaultCurve")]
        public string? DefaultCurve { get; set; }

        [JsonPropertyName("adcCurve")]
        public string? AdcCurve { get; set; }

        [JsonPropertyName("wpidCurves")]
        public Dictionary<string, string>? WpidCurves { get; set; }
    }

    private sealed class ReceiverDto
    {
        [JsonPropertyName("pid")]
        public string? Pid { get; set; }

        [JsonPropertyName("family")]
        public string? Family { get; set; }

        [JsonPropertyName("maxSlots")]
        public int MaxSlots { get; set; }

        [JsonPropertyName("reportPreference")]
        public string? ReportPreference { get; set; }
    }

    private sealed class VoltagePointDto
    {
        [JsonPropertyName("mv")]
        public int Mv { get; set; }

        [JsonPropertyName("pct")]
        public int Pct { get; set; }
    }
}

internal readonly record struct ReceiverInfo(
    ushort ProductId,
    ReceiverFamily Family,
    int MaxSlots,
    HidppReportPreference ReportPreference);

internal enum ReceiverFamily
{
    Unknown,
    Unifying,
    Bolt,
    Lightspeed,
    Nano,
    Legacy27Mhz,
}
