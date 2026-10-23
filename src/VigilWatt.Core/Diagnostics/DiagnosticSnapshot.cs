namespace VigilWatt.Core.Diagnostics;

/// <summary>
/// 功 能： 承载诊断导出所需的现有状态与接口缓存。
/// 2026/10/23 22:37:53 Administrator
/// </summary>
public sealed record DiagnosticSnapshot(
    DateTimeOffset CapturedAt, string AppVersion, string OsVersion, string Architecture,
    string TimeZone, string ActiveLogLevel, string ConfiguredLogLevel,
    IReadOnlyDictionary<string, bool> Plugins, IReadOnlyList<DiagnosticDevice> Devices,
    DateTimeOffset? HidCacheUpdatedAt, IReadOnlyList<DiagnosticHidInterface> HidInterfaces);

public sealed record DiagnosticDevice(
    string Id, string Name, string Source, string Protocol, bool IsOnline,
    int? BatteryPercent, string Charging, DateTimeOffset LastUpdated);

public sealed record DiagnosticHidInterface(
    string Path, string VidPid, ushort UsagePage, ushort Usage,
    ushort InputReportLength, ushort OutputReportLength, ushort FeatureReportLength,
    byte? OutputReportId, string Product, string Manufacturer, string? Owner);
