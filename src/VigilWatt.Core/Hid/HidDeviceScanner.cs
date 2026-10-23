using System.Runtime.InteropServices;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Hid;

/// <summary>
/// 功 能： 枚举当前在位的所有 HID 接口，收集元数据供插件匹配。
/// 2026/10/06 15:53:36 xliu
/// 路径未变时复用上次元数据，避免发现节拍反复 CreateFile（见 docs/project/code-flow.md §2.1）。
/// </summary>
public static class HidDeviceScanner
{
    private static readonly object CacheGate = new();
    private static readonly Dictionary<string, HidDeviceInfo> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static DateTimeOffset? _cacheUpdatedAt;

    public static (DateTimeOffset? UpdatedAt, IReadOnlyList<HidDeviceInfo> Devices) CachedSnapshot()
    {
        lock (CacheGate) return (_cacheUpdatedAt, Cache.Values.ToArray());
    }

    /// <summary>只列接口路径，**不开任何句柄**（<see cref="Scan"/> 在它为每个新路径读 caps 之前先拿清单）。</summary>
    public static string[] EnumeratePaths()
    {
        var guid = NativeHid.GuidDeviceInterfaceHid;
        if (NativeHid.CM_Get_Device_Interface_List_SizeW(out uint size, ref guid, null,
                NativeHid.CM_GET_DEVICE_INTERFACE_LIST_PRESENT) != 0 || size == 0)
            return Array.Empty<string>();

        var buffer = new char[size];
        if (NativeHid.CM_Get_Device_Interface_ListW(ref guid, null, buffer, size,
                NativeHid.CM_GET_DEVICE_INTERFACE_LIST_PRESENT) != 0)
            return Array.Empty<string>();

        // 多字符串（double-null 结尾）
        return new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    public static List<HidDeviceInfo> Scan()
    {
        var list = new List<HidDeviceInfo>();
        var meta = new List<string>();
        var paths = EnumeratePaths();
        if (paths.Length == 0)
        {
            lock (CacheGate)
            {
                Cache.Clear();
                _cacheUpdatedAt = DateTimeOffset.Now;
            }
            return list;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        lock (CacheGate)
        {
            foreach (var path in paths)
            {
                seen.Add(path);
                if (Cache.TryGetValue(path, out var cached))
                {
                    list.Add(cached);
                    continue;
                }

                try
                {
                    var info = Inspect(path, meta);
                    if (info == null) continue;
                    Cache[path] = info;
                    list.Add(info);
                }
                catch
                {
                    // 个别接口打不开不影响整体枚举
                }
            }

            if (Cache.Count != seen.Count)
            {
                foreach (var key in Cache.Keys.Where(k => !seen.Contains(k)).ToList())
                    Cache.Remove(key);
            }
            _cacheUpdatedAt = DateTimeOffset.Now;
        }

        // 明细只在设备集合变化时打，去重交给 Log.InfoOnChange（见 docs/dev/logging.md）。
        var signature = string.Join("|", list
            .Select(d => $"{d.VendorId:X4}:{d.ProductId:X4}:{d.UsagePage:X4}:{d.Usage:X4}:{d.InputReportLength}:{d.OutputReportLength}:{d.FeatureReportLength}")
            .OrderBy(s => s, StringComparer.Ordinal));
        if (Log.InfoOnChange("hid-scan-signature", signature,
                $"HID 扫描完成，共 {list.Count} 个接口（设备集合有变化，明细见 Debug 档）"))
        {
            foreach (var m in meta) Log.Debug(m);
            foreach (var d in list) Log.Debug("  " + d);
        }
        return list;
    }

    private static HidDeviceInfo? Inspect(string path, List<string> meta)
    {
        // 只为了读元数据短暂打开；键盘/鼠标保护 collection 打不开就跳过
        var handle = NativeHid.CreateFileW(
            path,
            NativeHid.GENERIC_READ | NativeHid.GENERIC_WRITE,
            NativeHid.FILE_SHARE_READ | NativeHid.FILE_SHARE_WRITE,
            IntPtr.Zero, NativeHid.OPEN_EXISTING, NativeHid.FILE_FLAG_OVERLAPPED, IntPtr.Zero);

        using var safe = handle;
        if (safe.IsInvalid) return null;

        var attrs = new NativeHid.HIDD_ATTRIBUTES { Size = Marshal.SizeOf<NativeHid.HIDD_ATTRIBUTES>() };
        if (!NativeHid.HidD_GetAttributes(safe, ref attrs)) return null;

        ushort usagePage = 0, usage = 0;
        ushort inLen = 0, outLen = 0, featLen = 0;
        byte? outReportId = null;
        int capsStatus = -1;
        bool preparsedOk = NativeHid.HidD_GetPreparsedData(safe, out var preparsed);
        if (preparsedOk)
        {
            try
            {
                var caps = new NativeHid.HIDP_CAPS();
                // HidP_GetCaps 返回 NTSTATUS，成功码是 0x00110000 而不是 0
                const int HIDP_STATUS_SUCCESS = 0x00110000;
                capsStatus = NativeHid.HidP_GetCaps(preparsed, ref caps);
                if (capsStatus == HIDP_STATUS_SUCCESS)
                {
                    usagePage = caps.UsagePage;
                    usage = caps.Usage;
                    inLen = caps.InputReportByteLength;
                    outLen = caps.OutputReportByteLength;
                    featLen = caps.FeatureReportByteLength;
                    // 输出集合优先；没有则退回输入集合（Report ID 是全局项，同接口共用）
                    outReportId = NativeHid.ReadReportId(preparsed, NativeHid.HIDP_REPORT_TYPE.HidP_Output)
                               ?? NativeHid.ReadReportId(preparsed, NativeHid.HIDP_REPORT_TYPE.HidP_Input);
                }
            }
            finally
            {
                NativeHid.HidD_FreePreparsedData(preparsed);
            }
        }
        meta.Add($"HID 元数据 {attrs.VendorID:X4}/{attrs.ProductID:X4}: preparsed={preparsedOk} capsStatus=0x{capsStatus:X8}");

        return new HidDeviceInfo
        {
            DevicePath = path,
            VendorId = attrs.VendorID,
            ProductId = attrs.ProductID,
            Version = attrs.VersionNumber,
            UsagePage = usagePage,
            Usage = usage,
            InputReportLength = inLen,
            OutputReportLength = outLen,
            FeatureReportLength = featLen,
            OutputReportId = outReportId,
            Product = GetString(safe, NativeHid.HidD_GetProductString) ?? "",
            Manufacturer = GetString(safe, NativeHid.HidD_GetManufacturerString) ?? "",
        };
    }

    private static string? GetString(
        Microsoft.Win32.SafeHandles.SafeFileHandle handle,
        Func<Microsoft.Win32.SafeHandles.SafeFileHandle, char[], uint, bool> api)
    {
        var buffer = new char[126];
        return api(handle, buffer, (uint)(buffer.Length * 2)) ? new string(buffer).TrimEnd('\0') : null;
    }
}
