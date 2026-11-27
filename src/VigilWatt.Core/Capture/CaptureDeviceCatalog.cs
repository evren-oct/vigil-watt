using VigilWatt.Core.Localization;

namespace VigilWatt.Core.Capture;
/// <summary>
/// 功 能： 把扁平的 HID 接口列表整理成「一台物理设备 + 它的若干接口」，并标出是否已被适配。
/// 2026/10/24 13:10:00 20211
/// 采集页选接口靠它：30 个接口直接摆出来，用户分不清哪个是刚插上的设备。
/// </summary>
public sealed class CaptureDeviceGroup
{
    public required string GroupKey { get; init; }

    public required ushort VendorId { get; init; }

    public required ushort ProductId { get; init; }

    public string Product { get; init; } = "";

    public string Manufacturer { get; init; } = "";

    public string? SerialNumber { get; init; }

    public IReadOnlyList<CaptureDeviceInfo> Interfaces { get; init; } = [];

    /// <summary>正在认领该设备的插件名。null = 没有插件认领（很可能是未适配的新设备）。</summary>
    public string? ClaimedBy { get; init; }

    /// <summary>插件实际认领的那个接口路径。已适配设备靠它给出**准确**的推荐，不靠猜。</summary>
    public string? ClaimedPath { get; init; }

    public bool IsAdapted => !string.IsNullOrEmpty(ClaimedBy);

    public string VidPid => $"VID_{VendorId:X4}&PID_{ProductId:X4}";

    public string Title => string.IsNullOrWhiteSpace(Product) ? VidPid : Product;

    /// <summary>该设备下最值得先试的接口（插件在用的那个优先；未适配时才按通道特征猜）。</summary>
    public CaptureDeviceInfo? Recommended
    {
        get
        {
            if (!string.IsNullOrEmpty(ClaimedPath))
            {
                var claimed = Interfaces.FirstOrDefault(i =>
                    string.Equals(i.InterfacePath, ClaimedPath, StringComparison.OrdinalIgnoreCase));
                if (claimed != null) return claimed;
            }
            return CaptureDeviceCatalog.Recommend(Interfaces);
        }
    }

    /// <summary>
    /// 这台设备本身就是鼠标/键盘（含它们的系统控制集合）。
    /// 采它的时候「关闭设备」一步会把自己的光标弄没，界面必须提示改用键盘快捷键。
    /// </summary>
    public bool IsInputPeripheral => Interfaces.Any(i =>
        i.UsagePage == 0x0001 && i.Usage is 0x01 or 0x02 or 0x06 or 0x80);

    public string Summary
    {
        get
        {
            var parts = new List<string>
            {
                VidPid,
                UiText.Format("S_cb8aca18f5", "{0} 个接口", Interfaces.Count),
                IsAdapted
                    ? UiText.Format("S_7656b7e221", "已适配（{0}）", ClaimedBy)
                    : UiText.Get("S_e3d46eadfc", "未适配"),
            };
            if (Interfaces.All(i => i.AccessDenied)) parts.Add(UiText.Get("S_c38d8b1bfd", "全部打不开"));
            return string.Join(" · ", parts);
        }
    }
}

/// <summary>
/// 功 能： 设备分组、按任务过滤、推荐接口与接口文案。纯逻辑，界面与报告共用同一套判断。
/// 2026/10/24 13:10:00 20211
/// </summary>
public static class CaptureDeviceCatalog
{
    /// <summary>按 GroupKey（ContainerId，读不到才退回 VID/PID）分组，并问一次「这个接口被谁认领」。</summary>
    public static IReadOnlyList<CaptureDeviceGroup> Group(
        IEnumerable<CaptureDeviceInfo> devices, Func<string, string?>? claimOf = null)
    {
        var groups = new List<CaptureDeviceGroup>();
        foreach (var bucket in devices.GroupBy(d => d.GroupKey, StringComparer.OrdinalIgnoreCase))
        {
            // 打不开的接口沉底：它们只能证明"存在"，采不到东西；也别让它们当整组的门面
            var interfaces = bucket
                .OrderBy(i => i.AccessDenied ? 1 : 0)
                .ThenBy(i => i.InterfaceKey, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var first = interfaces[0];
            var claimedPath = claimOf == null
                ? null
                : interfaces.Select(i => i.InterfacePath).FirstOrDefault(p => !string.IsNullOrEmpty(claimOf(p)));
            groups.Add(new CaptureDeviceGroup
            {
                GroupKey = bucket.Key,
                VendorId = first.VendorId,
                ProductId = first.ProductId,
                // 产品名/厂商名要在**整组**里找：打不开的接口（如 MI_00）读不到字符串描述符，
                // 取第一个接口会让整组标题退化成 VID_xxxx&PID_xxxx。
                Product = interfaces.Select(i => i.Product).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p)) ?? "",
                Manufacturer = interfaces.Select(i => i.Manufacturer).FirstOrDefault(m => !string.IsNullOrWhiteSpace(m)) ?? "",
                SerialNumber = interfaces.Select(i => i.SerialNumber).FirstOrDefault(s => !string.IsNullOrEmpty(s)),
                Interfaces = interfaces,
                ClaimedBy = claimedPath == null ? null : claimOf!(claimedPath),
                ClaimedPath = claimedPath,
            });
        }
        return groups.OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// 按任务筛选：未知设备看全部（未适配的排前面），已支持诊断只看插件正在认领的。
    /// 已支持诊断返回空 = 插件都没在跑，不是"没有设备"。
    /// </summary>
    public static IReadOnlyList<CaptureDeviceGroup> ForTask(IReadOnlyList<CaptureDeviceGroup> groups, CaptureTaskKind task)
    {
        if (task == CaptureTaskKind.SupportedDeviceDiagnosis)
            return groups.Where(g => g.IsAdapted).OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase).ToList();

        return groups
            .OrderBy(g => g.IsAdapted ? 1 : 0)
            .ThenBy(g => g.Recommended == null ? 1 : 0)
            .ThenBy(g => g.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 未适配设备的推荐接口：**只在厂商定义页（≥0xFF00）里挑**，再按"小报文优先"排。
    /// 只有标准键鼠/消费类接口的设备返回 null——那种接口不该被推荐，它不可能有电量协议。
    /// </summary>
    public static CaptureDeviceInfo? Recommend(IReadOnlyList<CaptureDeviceInfo> interfaces)
    {
        var vendor = interfaces
            .Where(i => !i.AccessDenied && i.UsagePage >= 0xFF00)
            .ToList();
        if (vendor.Count == 0) return null;

        var statusLike = vendor.Where(i => i.InputReportLength is > 0 and <= 128).ToList();
        if (statusLike.Count > 0) return statusLike[0];

        return vendor.FirstOrDefault(i => i.InputReportLength > 0)
               ?? vendor.FirstOrDefault(i => i.FeatureReportLength > 0)
               ?? vendor[0];
    }

    /// <summary>接口一行字：用户靠它判断"这个接口能不能采到东西"。</summary>
    public static string DescribeInterface(CaptureDeviceInfo device)
    {
        var name = ShortInterface(device.InterfaceKey);
        var usage = $"usage 0x{device.UsagePage:X4}/0x{device.Usage:X4}";
        if (device.AccessDenied)
            return $"{name} · {usage} · "
                   + UiText.Format("S_354f21b44d", "打不开（Win32 {0}，系统保护 collection）", device.OpenError);

        string channel;
        if (device.InputReportLength > 0)
        {
            channel = UiText.Format("S_586c0bdf09", "有输入通道（in {0}", device.InputReportLength);
            if (device.InputReportIds.Count > 0) channel += ", report " + Ids(device.InputReportIds);
            channel += ")";
            if (device.InputReportLength > 128)
                channel += UiText.Get("S_4a1f76212a", "，大缓冲通道通常不自发推送");
        }
        else if (device.FeatureReportLength > 0)
        {
            channel = UiText.Format("S_89fe5bab60", "仅 Feature 通道（feat {0}，report {1}）",
                          device.FeatureReportLength, Ids(device.FeatureReportIds))
                      + UiText.Get("S_6dbe83233a", "；多为配置通道，可能没有状态数据");
        }
        else
        {
            channel = UiText.Get("S_ca61f23c69", "无输入 / Feature 通道（被动监听采不到东西）");
        }
        // 标准用途页只有键鼠/消费类报文，别让用户以为这里能出电量协议
        if (!device.IsVendorDefined) channel += UiText.Get("S_f6a7bbe6a5", "；标准用途页，不会有厂商协议");
        return $"{name} · {usage} · {channel}";
    }

    /// <summary>路径末段（Col09 / MI_02），用于在界面里区分同一台设备的多个接口。</summary>
    public static string ShortInterface(string interfaceKey)
    {
        var index = interfaceKey.LastIndexOf('&');
        return index >= 0 && index + 1 < interfaceKey.Length ? interfaceKey[(index + 1)..] : interfaceKey;
    }

    private static string Ids(IReadOnlyList<byte> ids)
        => ids.Count == 0 ? "无" : string.Join("/", ids.Select(i => $"0x{i:X2}"));
}
