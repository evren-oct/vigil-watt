namespace VigilWatt.Core.Models;

/// <summary>
/// 功 能： 插件当前认领的 USB/HID 接口枚举身份；**不保证是设备本体**，认领到接收器时它就是接收器。
/// 2026/11/09 21:10:00 20211
/// 禁止用它推断型号：一颗 dongle 常被多款设备共用，见 docs/hardware/reference/mchose-catalog.md §1.3。
/// </summary>
public readonly record struct UsbIdentity(ushort VendorId, ushort ProductId, string? SerialNumber)
{
    /// <summary>VID/PID 都为非 0 才是可用身份；<c>default</c> 因此天然无效，不能拿去覆盖已知值。</summary>
    public bool IsValid => VendorId != 0 && ProductId != 0;

    /// <summary>同一个被认领接口：VID **和** PID 都相同才算，只有 VID 相同不算。</summary>
    public bool SameInterface(UsbIdentity other) => VendorId == other.VendorId && ProductId == other.ProductId;

    /// <summary>与 <see cref="VigilWatt.Core.Hid.HidDeviceInfo.VidPid"/> 同格式的短串。</summary>
    public string VidPid => $"VID_{VendorId:X4}&PID_{ProductId:X4}";

    /// <summary>无效/空快照不覆盖旧值；**换了接口就整体替换，绝不继承旧序列号**；同一接口才补缺失的序列号。</summary>
    public static UsbIdentity? Merge(UsbIdentity? stored, UsbIdentity? incoming)
    {
        if (incoming is not { } next || !next.IsValid) return stored;
        if (stored is not { } prev || !prev.IsValid) return next with { SerialNumber = Clean(next.SerialNumber) };

        var serial = Clean(next.SerialNumber)
                     ?? (next.SameInterface(prev) ? Clean(prev.SerialNumber) : null);
        return next with { SerialNumber = serial };
    }

    /// <summary>空串与纯空白一律归成 null：它们表达的是"没读到"，不是"序列号是空"。</summary>
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

/// <summary>
/// 功 能： 协议主动自报的身份（目前只有迈从 0x0900 提供）；<see cref="Model"/> 为空 = 目录未收录，不是"没有型号"。
/// 2026/11/09 21:10:00 20211
/// 与 <see cref="UsbIdentity"/> 是两件事：自报的是配对本体，USB 枚举到的可能是承载它的 dongle。
/// </summary>
public readonly record struct ReportedIdentity(ushort VendorId, ushort ProductId, string? Model)
{
    /// <summary>VID/PID 都为非 0 才是可用自报身份；<c>default</c> 因此天然无效。</summary>
    public bool IsValid => VendorId != 0 && ProductId != 0;

    /// <summary>目录里命中过型号。false 时 UI 只能说"未收录"，不许自己拼型号。</summary>
    public bool HasModel => !string.IsNullOrWhiteSpace(Model);

    /// <summary>同一台自报设备：VID 和 PID 都相同才算，只有 VID 相同不算。</summary>
    public bool SameDevice(ReportedIdentity other) => VendorId == other.VendorId && ProductId == other.ProductId;

    /// <summary>与 <see cref="UsbIdentity.VidPid"/> 同格式的短串。</summary>
    public string VidPid => $"VID_{VendorId:X4}&PID_{ProductId:X4}";

    /// <summary>无效/空快照不覆盖旧值；**换了设备就整体替换，绝不继承旧型号**；同一台设备才补缺失的型号。</summary>
    public static ReportedIdentity? Merge(ReportedIdentity? stored, ReportedIdentity? incoming)
    {
        if (incoming is not { } next || !next.IsValid) return stored;
        if (stored is not { } prev || !prev.IsValid) return next with { Model = Clean(next.Model) };

        var model = Clean(next.Model)
                    ?? (next.SameDevice(prev) ? Clean(prev.Model) : null);
        return next with { Model = model };
    }

    /// <summary>空串与纯空白一律归成 null：型号未知只有一种表达，不造第二种。</summary>
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

/// <summary>
/// 功 能： 已知的接收器/传输通道身份（目录登记的 dongle 角色）；它说明"这条接口是什么通道"，不证明当前链路。
/// 2026/11/09 21:10:00 20211
/// DisplayName 是目录里的展示名（Bolt / 2.4G 接收器），展示层只读它、不映射品牌。
/// IconKey 同理：目录给语义 key（wireless / lightning / multi），App 只负责把 key 画成图形。
/// 本体接口没有接收器身份，返回 null——不要把本体 PID 塞进来冒充接收器。
/// </summary>
public readonly record struct ReceiverIdentity(
    string? Role, string? Family, string? DisplayName = null, string? IconKey = null)
{
    /// <summary>四项都空 = 没有信息，合并时按"未提供"处理，不能拿它清掉已有身份。</summary>
    public bool IsEmpty
        => string.IsNullOrWhiteSpace(Role) && string.IsNullOrWhiteSpace(Family)
           && string.IsNullOrWhiteSpace(DisplayName) && string.IsNullOrWhiteSpace(IconKey);

    /// <summary>
    /// 空结构不覆盖旧值；Role / Family 任一改变都整体替换——没有目录能证明两者的对应关系，
    /// 拼接新旧字段会造出一个从未存在过的通道（2026-11-09 复核裁定）。
    /// </summary>
    public static ReceiverIdentity? Merge(ReceiverIdentity? stored, ReceiverIdentity? incoming)
    {
        if (incoming is not { } next || next.IsEmpty) return stored;
        if (stored is not { } prev || prev.IsEmpty) return next;

        // 只有"另一项没变"时才敢沿用旧值，否则新旧拼在一起可能是错的组合
        var role = next.Role ?? (Same(next.Family, prev.Family) ? prev.Role : null);
        var family = next.Family ?? (Same(next.Role, prev.Role) ? prev.Family : null);
        // 展示名与图标都是 Role/Family 的派生标签：身份没变才沿用，否则会把旧族的名字/图形挂到新族上
        var sameIdentity = Same(family, prev.Family) && Same(role, prev.Role);
        var display = next.DisplayName ?? (sameIdentity ? prev.DisplayName : null);
        var icon = next.IconKey ?? (sameIdentity ? prev.IconKey : null);
        return next with { Role = role, Family = family, DisplayName = display, IconKey = icon };
    }

    /// <summary>比较两个可选字段是否"没变"：两侧都空也算没变（空到空不携带信息）。</summary>
    private static bool Same(string? left, string? right)
        => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
}
