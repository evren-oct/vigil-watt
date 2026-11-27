using VigilWatt.Core.Localization;
namespace VigilWatt.Core.Capture;
/// <summary>
/// 功 能： 采集功能的枚举与字符串契约（写进 JSONL，改动即破坏历史包兼容）。
/// 2026/10/24 12:40:43 20211
/// </summary>

/// <summary>采集任务。两者都走 USB/HID，但**列的设备和提示不同**：未知设备看全部，已支持只看插件在用的。</summary>
public enum CaptureTaskKind
{
    /// <summary>未适配的新设备：列出全部接口，未适配的排前面。</summary>
    UnknownDeviceInvestigation = 0,
    /// <summary>已在用的设备读数异常：只列被插件认领的接口，用于排查。</summary>
    SupportedDeviceDiagnosis = 1,
}

/// <summary>采集方式。**只由用户选择，禁止按设备特征推断或降级**（见 docs/project/capture.md）。</summary>
public enum CaptureMethod
{
    UsbHid = 0,
}

/// <summary>
/// 通信层级。**不同层不能混为一谈**：接收器暴露的 HID 数据 ≠ 设备与接收器之间的 2.4G 链路。
/// </summary>
public enum CaptureLayer
{
    /// <summary>设备枚举与描述符信息（VID/PID/路径/接口号）。</summary>
    DeviceEnum = 0,
    /// <summary>HID 接口能力（caps：usage/长度/report ID）。不是原始报告描述符。</summary>
    HidCaps = 1,
    /// <summary>经 HID API 实际读到的输入报告。</summary>
    HidApiInput = 2,
    /// <summary>经 HidD_GetFeature 实际读到的 Feature 报告。</summary>
    HidApiFeature = 3,
    /// <summary>USB 总线级记录。本应用不采集，只有外部 USBPcap 才有。</summary>
    UsbBus = 4,
    /// <summary>蓝牙 / 2.4G 空中通信。接收器 HID 层面不可见。</summary>
    Wireless = 5,
    /// <summary>主机→设备方向：用户在采集页手动发出的 output report（WriteFile）。</summary>
    HidApiOutput = 6,
}

/// <summary>
/// 能力状态。`Unverified` 与 `Unavailable` 必须分开：前者是"没试过"，后者是"试过且不行"。
/// </summary>
public enum CapabilityStatus
{
    /// <summary>已实际验证可执行。</summary>
    Available = 0,
    /// <summary>已确认当前环境不支持或访问失败。</summary>
    Unavailable = 1,
    /// <summary>尚未实际验证。</summary>
    Unverified = 2,
    /// <summary>需要用户执行授权操作（如管理员、关闭占用进程）。</summary>
    NeedsPermission = 3,
    /// <summary>需要安装或配置其他采集工具。</summary>
    NeedsExtraTool = 4,
}

/// <summary>单条采集记录的读取结果。失败只进 errors.jsonl，绝不产出空报文冒充成功。</summary>
public enum CaptureRecordStatus
{
    Received = 0,
    Failed = 1,
    /// <summary>主机发出去了（只有 output report 会有这个状态）。</summary>
    Sent = 2,
}

/// <summary>会话事件类型。用户标记与设备状态变化都是事件，**不进原始记录流**。</summary>
public enum CaptureEventKind
{
    SessionStarted = 0,
    UserMark = 1,
    Paused = 2,
    Resumed = 3,
    DeviceRemoved = 4,
    DeviceReturned = 5,
    BufferLimitReached = 6,
    SessionStopped = 7,
    /// <summary>用户从官方软件/设备屏幕读到的电量，作为核对参照（可选，不是报文）。</summary>
    BatteryReference = 8,
}

/// <summary>错误/警告类型。</summary>
public enum CaptureErrorKind
{
    OpenDenied = 0,
    OpenFailed = 1,
    ReadFailed = 2,
    FeatureFailed = 3,
    DeviceRemoved = 4,
    BufferLimit = 5,
    SourceException = 6,
    PackageFailed = 7,
    WriteFailed = 8,
}

/// <summary>会话生命周期。停止幂等，任一状态都能转 Failed 且保留已有证据。</summary>
public enum CaptureSessionState
{
    Created = 0,
    Probing = 1,
    Ready = 2,
    Running = 3,
    Paused = 4,
    Stopped = 5,
    Failed = 6,
}

/// <summary>枚举与 JSON 字符串的双向映射。写错一个词就会让历史包读不回来，故集中在此并单测。</summary>
public static class CaptureNames
{
    public static string ToWire(this CaptureLayer value) => value switch
    {
        CaptureLayer.DeviceEnum => "device-enum",
        CaptureLayer.HidCaps => "hid-caps",
        CaptureLayer.HidApiInput => "hid-api-input",
        CaptureLayer.HidApiFeature => "hid-api-feature",
        CaptureLayer.UsbBus => "usb-bus",
        CaptureLayer.Wireless => "wireless",
        CaptureLayer.HidApiOutput => "hid-api-output",
        _ => "unknown",
    };

    public static CaptureLayer ParseLayer(string? text) => text switch
    {
        "device-enum" => CaptureLayer.DeviceEnum,
        "hid-caps" => CaptureLayer.HidCaps,
        "hid-api-input" => CaptureLayer.HidApiInput,
        "hid-api-feature" => CaptureLayer.HidApiFeature,
        "usb-bus" => CaptureLayer.UsbBus,
        "wireless" => CaptureLayer.Wireless,
        "hid-api-output" => CaptureLayer.HidApiOutput,
        _ => CaptureLayer.DeviceEnum,
    };

    public static string ToWire(this CaptureRecordStatus value) => value switch
    {
        CaptureRecordStatus.Received => "received",
        CaptureRecordStatus.Sent => "sent",
        _ => "failed",
    };

    public static CaptureRecordStatus ParseStatus(string? text) => text switch
    {
        "sent" => CaptureRecordStatus.Sent,
        "failed" => CaptureRecordStatus.Failed,
        _ => CaptureRecordStatus.Received,
    };

    public static string ToWire(this CaptureEventKind value) => value switch
    {
        CaptureEventKind.SessionStarted => "session_started",
        CaptureEventKind.UserMark => "user_mark",
        CaptureEventKind.Paused => "paused",
        CaptureEventKind.Resumed => "resumed",
        CaptureEventKind.DeviceRemoved => "device_removed",
        CaptureEventKind.DeviceReturned => "device_returned",
        CaptureEventKind.BufferLimitReached => "buffer_limit_reached",
        CaptureEventKind.SessionStopped => "session_stopped",
        CaptureEventKind.BatteryReference => "battery_reference",
        _ => "unknown",
    };

    public static CaptureEventKind ParseEventKind(string? text) => text switch
    {
        "session_started" => CaptureEventKind.SessionStarted,
        "user_mark" => CaptureEventKind.UserMark,
        "paused" => CaptureEventKind.Paused,
        "resumed" => CaptureEventKind.Resumed,
        "device_removed" => CaptureEventKind.DeviceRemoved,
        "device_returned" => CaptureEventKind.DeviceReturned,
        "buffer_limit_reached" => CaptureEventKind.BufferLimitReached,
        "session_stopped" => CaptureEventKind.SessionStopped,
        "battery_reference" => CaptureEventKind.BatteryReference,
        _ => CaptureEventKind.UserMark,
    };

    public static string ToWire(this CaptureErrorKind value) => value switch
    {
        CaptureErrorKind.OpenDenied => "open_denied",
        CaptureErrorKind.OpenFailed => "open_failed",
        CaptureErrorKind.ReadFailed => "read_failed",
        CaptureErrorKind.FeatureFailed => "feature_failed",
        CaptureErrorKind.DeviceRemoved => "device_removed",
        CaptureErrorKind.BufferLimit => "buffer_limit",
        CaptureErrorKind.SourceException => "source_exception",
        CaptureErrorKind.PackageFailed => "package_failed",
        CaptureErrorKind.WriteFailed => "write_failed",
        _ => "unknown",
    };

    public static CaptureErrorKind ParseErrorKind(string? text) => text switch
    {
        "open_denied" => CaptureErrorKind.OpenDenied,
        "open_failed" => CaptureErrorKind.OpenFailed,
        "read_failed" => CaptureErrorKind.ReadFailed,
        "feature_failed" => CaptureErrorKind.FeatureFailed,
        "device_removed" => CaptureErrorKind.DeviceRemoved,
        "buffer_limit" => CaptureErrorKind.BufferLimit,
        "source_exception" => CaptureErrorKind.SourceException,
        "package_failed" => CaptureErrorKind.PackageFailed,
        "write_failed" => CaptureErrorKind.WriteFailed,
        _ => CaptureErrorKind.OpenFailed,
    };

    public static string ToWire(this CaptureTaskKind value)
        => value == CaptureTaskKind.SupportedDeviceDiagnosis ? "supported-diagnosis" : "unknown-device";

    public static CaptureTaskKind ParseTask(string? text)
        => text == "supported-diagnosis" ? CaptureTaskKind.SupportedDeviceDiagnosis : CaptureTaskKind.UnknownDeviceInvestigation;

    public static string Label(this CaptureTaskKind value)
        => value == CaptureTaskKind.SupportedDeviceDiagnosis
            ? UiText.Get("S_afe1cc98fc", "已支持设备诊断（排查读数）")
            : UiText.Get("S_eb94933afe", "未知设备调查（找新协议）");

    public static string ToWire(this CapabilityStatus value) => value switch    {
        CapabilityStatus.Available => "available",
        CapabilityStatus.Unavailable => "unavailable",
        CapabilityStatus.Unverified => "unverified",
        CapabilityStatus.NeedsPermission => "needs_permission",
        CapabilityStatus.NeedsExtraTool => "needs_extra_tool",
        _ => "unverified",
    };

    public static CapabilityStatus ParseCapabilityStatus(string? text) => text switch
    {
        "available" => CapabilityStatus.Available,
        "unavailable" => CapabilityStatus.Unavailable,
        "needs_permission" => CapabilityStatus.NeedsPermission,
        "needs_extra_tool" => CapabilityStatus.NeedsExtraTool,
        _ => CapabilityStatus.Unverified,
    };

    public static string ToWire(this CaptureSessionState value) => value switch    {
        CaptureSessionState.Created => "created",
        CaptureSessionState.Probing => "probing",
        CaptureSessionState.Ready => "ready",
        CaptureSessionState.Running => "running",
        CaptureSessionState.Paused => "paused",
        CaptureSessionState.Stopped => "stopped",
        CaptureSessionState.Failed => "failed",
        _ => "unknown",
    };

    public static CaptureSessionState ParseState(string? text) => text switch
    {
        "created" => CaptureSessionState.Created,
        "probing" => CaptureSessionState.Probing,
        "ready" => CaptureSessionState.Ready,
        "running" => CaptureSessionState.Running,
        "paused" => CaptureSessionState.Paused,
        "stopped" => CaptureSessionState.Stopped,
        "failed" => CaptureSessionState.Failed,
        _ => CaptureSessionState.Stopped,
    };

    /// <summary>能力状态的用户可见文案（界面与报告共用，避免两处写不一样）。</summary>
    public static string Label(this CapabilityStatus value) => value switch
    {
        CapabilityStatus.Available => UiText.Get("S_e91365cf9e", "可用"),
        CapabilityStatus.Unavailable => UiText.Get("S_beff4a1cd1", "不可用"),
        CapabilityStatus.Unverified => UiText.Get("S_6815896741", "未验证"),
        CapabilityStatus.NeedsPermission => UiText.Get("S_a0b02597af", "需要权限"),
        CapabilityStatus.NeedsExtraTool => UiText.Get("S_72a9c734b4", "需要额外工具"),
        _ => UiText.Get("S_d9c32a4c3d", "未知"),
    };
}
