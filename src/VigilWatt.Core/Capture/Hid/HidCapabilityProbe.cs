using VigilWatt.Core.Localization;
namespace VigilWatt.Core.Capture.Hid;
/// <summary>
/// 功 能： 采集前能力预检：按本机实际结果逐项给状态，非「可用」项必带原因/影响/后续。
/// 2026/10/24 12:40:43 20211
/// 只陈述查到的与试过的事实；没试过的一律标「未验证」，不为好看伪造可用。
/// </summary>
public static class HidCapabilityProbe
{
    public static IReadOnlyList<CapabilityItem> Probe(CaptureDeviceInfo device)
    {
        var items = new List<CapabilityItem>
        {
            new()
            {
                Id = "device-enum",
                Title = UiText.Get("S_c8a8ba706d", "操作系统能枚举到目标设备"),
                Status = CapabilityStatus.Available,
                Evidence = UiText.Format("S_bee56d6231", "已从设备接口列表取到该接口（{0}）", device.InterfaceKey),
            },
            VidPidItem(device),
            StringItem("product-name", UiText.Get("S_abc0ac7995", "产品名称"), device.Product),
            StringItem("manufacturer", UiText.Get("S_37490df91c", "厂商名称"), device.Manufacturer),
            SerialItem(device),
            new()
            {
                Id = "interface-id",
                Title = UiText.Get("S_acf97f6ca7", "接口编号与设备路径"),
                Status = CapabilityStatus.Available,
                Evidence = device.InterfaceKey,
            },
            GroupingItem(device),
            new()
            {
                Id = "bus-address",
                Title = UiText.Get("S_dabe5a321d", "USB 总线号 / 设备地址 / 父设备"),
                Status = CapabilityStatus.Unverified,
                Reason = UiText.Get("S_8b9bbbd2a4", "本版本未实现（需要读设备节点属性）。"),
                Impact = UiText.Get("S_1b348abbda", "报告里没有总线号与地址，跨会话比对同一物理设备会更麻烦。"),
                NextStep = UiText.Get("S_0e4f63e1f4", "后续版本补 CfgMgr32 的 DEVPKEY_Device_BusNumber / Address。"),
            },
            DescriptorItem(device),
            CapsItem(device),
            InputReportItem(device),
            FeatureReportItem(device),
            OutputReportItem(device),
            PermissionItem(),
            OccupancyItem(),
            UsbBusItem(),
            WirelessItem(),
        };
        return items;
    }

    private static CapabilityItem GroupingItem(CaptureDeviceInfo device)
    {
        var byContainer = DeviceGroupKey.IsContainer(device.GroupKey);
        return new CapabilityItem
        {
            Id = "device-grouping",
            Title = UiText.Get("S_80faea3ac2", "同一物理设备的多接口归属"),
            Status = CapabilityStatus.Available,
            Evidence = byContainer
                ? UiText.Format("S_c45cf6f646", "系统 ContainerId：{0}", device.GroupKey["container:".Length..])
                : UiText.Get("S_345c53095e", "读不到 ContainerId，按 VID/PID 归组"),
            Impact = byContainer
                ? null
                : UiText.Get("S_689cdd0f3c", "同型号的两只会被并成一台，同一个 dongle 的不同 MI 也可能被拆开——选接口时以「有没有输入通道」为准。"),
            NextStep = byContainer ? null : UiText.Get("S_45cad31a9f", "拔掉同型号的另一只、或换个 USB 口后重新枚举。"),
        };
    }

    private static CapabilityItem VidPidItem(CaptureDeviceInfo device) => new()
    {
        Id = "vid-pid",
        Title = UiText.Get("S_72afaa7508", "VID / PID / 版本号"),
        Status = device.VendorId == 0 && device.ProductId == 0 ? CapabilityStatus.Unavailable : CapabilityStatus.Available,
        Evidence = UiText.Format("S_f6a343e5dc", "{0} 版本 0x{1:X4}", device.VidPid, device.VersionNumber),
        Reason = device.VendorId == 0 && device.ProductId == 0 ? UiText.Get("S_fc7339bdb2", "设备没有返回属性，且路径里也没有 VID/PID。") : null,
        Impact = device.VendorId == 0 && device.ProductId == 0 ? UiText.Get("S_642459f784", "无法按 VID/PID 归类设备。") : null,
        NextStep = device.VendorId == 0 && device.ProductId == 0 ? UiText.Get("S_1bc8a0ed57", "换一个接口或换 USB 口后重新枚举。") : null,
    };

    private static CapabilityItem StringItem(string id, string title, string value) => new()
    {
        Id = id,
        Title = title,
        Status = string.IsNullOrWhiteSpace(value) ? CapabilityStatus.Unavailable : CapabilityStatus.Available,
        Evidence = string.IsNullOrWhiteSpace(value) ? UiText.Get("S_4e83f5d9e4", "设备未返回该字符串描述符") : value,
        Reason = string.IsNullOrWhiteSpace(value) ? UiText.Get("S_a0aba6e34c", "设备没有提供该字符串（很多 dongle 就是这样）。") : null,
        Impact = string.IsNullOrWhiteSpace(value) ? UiText.Get("S_3fd7c734f2", "报告里该字段为空，无法据此辨认设备。") : null,
        NextStep = string.IsNullOrWhiteSpace(value) ? UiText.Get("S_8d958c3c56", "以 VID/PID 与接口段辨认；或查看设备外壳丝印。") : null,
    };

    private static CapabilityItem SerialItem(CaptureDeviceInfo device) => new()
    {
        Id = "serial-number",
        Title = UiText.Get("S_0e052ec218", "设备序列号"),
        Status = string.IsNullOrEmpty(device.SerialNumber) ? CapabilityStatus.Unavailable : CapabilityStatus.Available,
        Evidence = string.IsNullOrEmpty(device.SerialNumber) ? UiText.Get("S_96b46046eb", "HidD_GetSerialNumberString 未返回内容") : device.SerialNumber,
        Reason = string.IsNullOrEmpty(device.SerialNumber) ? UiText.Get("S_aea9187509", "设备没有提供序列号字符串。") : null,
        Impact = string.IsNullOrEmpty(device.SerialNumber) ? UiText.Get("S_5bda8fde54", "无法区分同型号的不同个体。") : null,
        NextStep = string.IsNullOrEmpty(device.SerialNumber) ? UiText.Get("S_180fc4e6a3", "改按接口路径 + USB 口位置区分。") : null,
    };

    private static CapabilityItem DescriptorItem(CaptureDeviceInfo device) => new()
    {
        Id = "report-descriptor",
        Title = UiText.Get("S_1cf62f6de7", "HID 报告描述符（原始字节）"),
        Status = CapabilityStatus.Unavailable,
        Evidence = UiText.Get("S_d28d509d8a", "hid.dll 不导出该 API；IOCTL_HID_GET_REPORT_DESCRIPTOR 面向驱动，用户态调用返回 ERROR_INVALID_FUNCTION"),
        Reason = UiText.Get("S_347b95435e", "Windows 用户态没有读取原始报告描述符的接口（本机已逐条试过）。"),
        Impact = UiText.Get("S_799a8a6c52", "拿不到描述符原始字节，只能看到 caps 派生出的 usage / 长度 / report ID。"),
        NextStep = UiText.Get("S_47083917bb", "需要原始字节时用 USBPcap 抓总线，或换 Linux 用 usbhid-dump。"),
    };

    private static CapabilityItem CapsItem(CaptureDeviceInfo device) => new()
    {
        Id = "caps",
        Title = UiText.Get("S_1216c3cc6d", "HID 接口能力摘要（caps）"),
        Status = device.CapsAvailable ? CapabilityStatus.Available : CapabilityStatus.Unavailable,
        Evidence = device.CapsAvailable
            ? UiText.Format("S_22da8e4eaa", "usage 0x{0:X4}/0x{1:X4}，长度 in/out/feat = ", device.UsagePage, device.Usage)
              + $"{device.InputReportLength}/{device.OutputReportLength}/{device.FeatureReportLength}"
            : UiText.Get("S_6b5fb4e34b", "HidP_GetCaps 未成功"),
        Reason = device.CapsAvailable ? null : UiText.Get("S_8453f1c929", "该接口没有返回 preparsed data。"),
        Impact = device.CapsAvailable ? null : UiText.Get("S_63ebd1103e", "无法判断该接口有哪些通道。"),
        NextStep = device.CapsAvailable ? null : UiText.Get("S_25c7c63ec7", "换接口或换 USB 口后重新预检。"),
    };

    private static CapabilityItem InputReportItem(CaptureDeviceInfo device) => new()
    {
        Id = "input-report",
        Title = UiText.Get("S_3807186c0a", "读取 HID 输入报告"),
        Status = device.AccessDenied
            ? CapabilityStatus.NeedsPermission
            : device.InputReportLength > 0 ? CapabilityStatus.Available : CapabilityStatus.Unavailable,
        Evidence = device.AccessDenied
            ? UiText.Format("S_6f150d320a", "CreateFile 返回 {0}（系统保护 collection）", device.OpenError)
            : UiText.Format("S_69542e31d5", "输入报文长度 {0}，report ID {1}", device.InputReportLength, Ids(device.InputReportIds)),
        Reason = device.AccessDenied
            ? UiText.Get("S_9903660d6f", "该接口是键盘/鼠标的系统保护 collection，普通权限打不开。")
            : device.InputReportLength > 0 ? null : UiText.Get("S_f34a00cfbe", "该接口没有输入通道（输入报文长度为 0）。"),
        Impact = device.AccessDenied
            ? UiText.Get("S_a62c996bc2", "这个接口采不到任何报文；请改选同设备的厂商定义接口。")
            : device.InputReportLength > 0 ? null : UiText.Get("S_3ae12670f5", "被动监听在这个接口上不会产生记录。"),
        NextStep = device.AccessDenied
            ? UiText.Get("S_e9a5e5a70a", "在同一台设备的其他接口里选厂商定义（usage page 0xFF00 段）的那个。")
            : device.InputReportLength > 0 ? null : UiText.Get("S_1e08f716dd", "改选其他接口，或改用 Feature 读取。"),
    };

    private static CapabilityItem FeatureReportItem(CaptureDeviceInfo device) => new()
    {
        Id = "feature-report",
        Title = UiText.Get("S_c397fcc9ae", "读取 Feature Report（只读 GET_REPORT）"),
        Status = device.FeatureReportLength == 0 || device.AccessDenied
            ? CapabilityStatus.Unavailable
            : CapabilityStatus.Unverified,
        Evidence = device.FeatureReportLength == 0
            ? UiText.Get("S_c3e3bc4205", "该接口 Feature 报文长度为 0")
            : UiText.Format("S_ecbf5759ac", "Feature 报文长度 {0}，caps 里的 ID {1}", device.FeatureReportLength, Ids(device.FeatureReportIds)),
        Reason = device.FeatureReportLength == 0
            ? UiText.Get("S_de97a83803", "该接口没有 Feature 通道。")
            : device.AccessDenied
                ? UiText.Get("S_3f140eb0af", "接口打不开，无法读取。")
                : UiText.Get("S_79029512b0", "本会话还没有实际执行过 GET_REPORT，所以不标「可用」。"),
        Impact = device.FeatureReportLength == 0
            ? UiText.Get("S_3b3512a8c3", "无法通过 Feature 读取任何数据。")
            : UiText.Get("S_6e280a5484", "不勾选 Feature 读取时，只能采到设备主动推送的输入报告。"),
        NextStep = device.FeatureReportLength == 0 || device.AccessDenied
            ? UiText.Get("S_31c8658f8c", "改选其他接口。")
            : UiText.Get("S_894e35e539", "在采集设置里勾选 Feature 读取并选择 report ID（只能从上面这些 ID 里选）。"),
    };

    private static CapabilityItem OutputReportItem(CaptureDeviceInfo device) => new()
    {
        Id = "output-report",
        Title = UiText.Get("S_2cf8c78abb", "写入 Output Report / Set Feature"),
        Status = CapabilityStatus.Unavailable,
        Evidence = device.OutputReportLength > 0
            ? UiText.Format("S_2498b57bc7", "接口存在输出通道（{0} 字节），但本功能不写", device.OutputReportLength)
            : UiText.Get("S_5e9fff0e00", "该接口没有输出通道"),
        Reason = UiText.Get("S_d5224d6ab2", "本功能定位为只读采集，不向设备写入任何报文（docs/hardware/safety.md 事故一）。"),
        Impact = UiText.Get("S_7aa31a12ca", "需要先发会话帧/查询帧才能出数据的设备，本次采不到推送以外的内容。"),
        NextStep = UiText.Get("S_56a8263d85", "如需写入验证，用项目已有的插件机制或独立脚本，不要用采集页。"),
    };

    private static CapabilityItem PermissionItem() => new()
    {
        Id = "process-permission",
        Title = UiText.Get("S_835ea76b35", "当前进程权限"),
        Status = CapabilityStatus.Available,
        Evidence = CaptureEnvironment.PermissionLabel,
        Impact = CaptureEnvironment.IsElevated
            ? null
            : UiText.Get("S_5363ff33e5", "系统保护 collection 仍会拒绝访问；本功能不会静默提权。"),
        NextStep = CaptureEnvironment.IsElevated ? null : UiText.Get("S_ab0d1eab14", "需要时请改选厂商定义接口，而不是提权。"),
    };

    private static CapabilityItem OccupancyItem() => new()
    {
        Id = "device-occupancy",
        Title = UiText.Get("S_c1edde9c09", "设备是否被其他进程占用"),
        Status = CapabilityStatus.Unverified,
        Evidence = UiText.Get("S_c397b7ecac", "HID 允许多个句柄同时打开，无法从打开结果可靠判断"),
        Reason = UiText.Get("S_29db36a54b", "Windows 不提供「谁持有该 HID 接口」的查询接口。"),
        Impact = UiText.Get("S_4e8d6a51eb", "若官方软件或本应用的插件正在轮询同一接口，采集可能受影响，但无法证实。"),
        NextStep = UiText.Get("S_2df3b18cf6", "采集前关掉官方配置软件；本应用内被插件认领的接口会在设备列表里标注。"),
    };

    private static CapabilityItem UsbBusItem()
    {
        var path = CaptureEnvironment.FindUsbPcapCommand();
        return new CapabilityItem
        {
            Id = "usb-bus",
            Title = UiText.Get("S_8ee34d2f17", "USB 总线级抓包"),
            Status = path == null ? CapabilityStatus.NeedsExtraTool : CapabilityStatus.Available,
            Evidence = path ?? UiText.Get("S_53685a7ef7", "未找到 USBPcapCMD.exe"),
            Reason = path == null ? UiText.Get("S_73cb7c10c9", "需要安装 USBPcap（过滤驱动），本应用不代为安装。") : null,
            Impact = path == null ? UiText.Get("S_2abcd0aef8", "本次无法获得 USB 总线级通信记录。") : UiText.Get("S_ffd0f1b76c", "可用外部工具另行抓包，本应用不内嵌。"),
            NextStep = path == null
                ? UiText.Get("S_f615f6b755", "装 Wireshark 时勾选 USBPcap，或用 docs/hardware/debugging.md §9 的命令。")
                : UiText.Get("S_95ceba9c08", "用 docs/hardware/debugging.md §9 的 usb capture 命令。"),
        };
    }

    private static CapabilityItem WirelessItem() => new()
    {
        Id = "wireless",
        Title = UiText.Get("S_940f0bafc2", "蓝牙 / 2.4G 空中通信"),
        Status = CapabilityStatus.Unavailable,
        Evidence = UiText.Get("S_37b3d63682", "接收器只向上暴露 HID 通道"),
        Reason = UiText.Get("S_832e811705", "设备与接收器之间的无线链路不经过主机 USB 栈。"),
        Impact = UiText.Get("S_283e9bf9e6", "本报告不会有任何空中链路数据，不能据此判断配对/跳频行为。"),
        NextStep = UiText.Get("S_023b952649", "需要时用专用射频抓包设备，本项目不做。"),
    };

    private static string Ids(IReadOnlyList<byte> ids)
        => ids.Count == 0 ? UiText.Get("S_72077749f7", "无") : string.Join(", ", ids.Select(i => $"0x{i:X2}"));
}
