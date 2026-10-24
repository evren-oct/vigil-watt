using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace VigilWatt.Core.Hid;

/// <summary>
/// 功 能： 一个 HID 接口的静态元数据（枚举时收集，不开句柄）。
/// 2026/10/06 15:53:36 xliu
/// </summary>
public sealed class HidDeviceInfo
{
    public required string DevicePath { get; init; }
    public ushort VendorId { get; init; }
    public ushort ProductId { get; init; }
    public ushort Version { get; init; }
    public ushort UsagePage { get; init; }
    public ushort Usage { get; init; }
    public ushort InputReportLength { get; init; }
    public ushort OutputReportLength { get; init; }
    public ushort FeatureReportLength { get; init; }

    /// <summary>输出报文的 report ID（扫描时从 caps 读取）。null = 读不到；0 = 接口不使用 report ID。
    /// HID++ 长接口应是 0x11、短接口应是 0x10——以本字段为准，勿硬编码。</summary>
    public byte? OutputReportId { get; init; }

    public string Product { get; init; } = "";
    public string Manufacturer { get; init; } = "";

    public string VidPid => $"VID_{VendorId:X4}&PID_{ProductId:X4}";

    public override string ToString() =>
        $"{Product} [{VidPid}] usage=0x{UsagePage:X4}/0x{Usage:X4} in={InputReportLength} out={OutputReportLength} feat={FeatureReportLength}" +
        (OutputReportId.HasValue ? $" outId=0x{OutputReportId.Value:X2}" : "");

    /// <summary>打开设备。键盘/鼠标等受保护的 collection 会抛 UnauthorizedAccessException。</summary>
    public HidDeviceHandle Open()
    {
        var handle = NativeHid.CreateFileW(
            DevicePath,
            NativeHid.GENERIC_READ | NativeHid.GENERIC_WRITE,
            NativeHid.FILE_SHARE_READ | NativeHid.FILE_SHARE_WRITE,
            IntPtr.Zero,
            NativeHid.OPEN_EXISTING,
            NativeHid.FILE_FLAG_OVERLAPPED,
            IntPtr.Zero);

        int hr = handle.IsInvalid ? Marshal.GetHRForLastWin32Error() : 0;
        if (handle.IsInvalid)
        {
            throw hr switch
            {
                unchecked((int)0x80070005) => new UnauthorizedAccessException($"无权访问 {DevicePath}（系统保护 collection）"),
                _ => new Win32Exception(hr, $"打开 HID 设备失败: {DevicePath}"),
            };
        }

        var stream = new FileStream(handle, FileAccess.ReadWrite, 0, isAsync: true);
        return new HidDeviceHandle(this, stream);
    }
}

/// <summary>已打开的 HID 设备句柄，封装 feature/input/output 报文读写。</summary>
public sealed class HidDeviceHandle : IDisposable
{
    private readonly FileStream _stream;

    public HidDeviceInfo Info { get; }

    internal HidDeviceHandle(HidDeviceInfo info, FileStream stream)
    {
        Info = info;
        _stream = stream;
    }

    /// <summary>发送 feature report 并立即读回同 ID 的响应（HidD 同步调用，自带设备层超时）；buffer[0] 必须是 report ID。</summary>
    public byte[]? GetFeature(byte reportId)
    {
        ushort len = Info.FeatureReportLength;
        if (len == 0) return null;
        var buffer = new byte[len];
        buffer[0] = reportId;
        if (!NativeHid.HidD_GetFeature(_stream.SafeFileHandle, buffer, len))
            return null;
        return buffer;
    }

    /// <summary>通过控制传输读取指定输入报告；部分设备不向中断输入端点推送状态。</summary>
    public byte[]? GetInputReport(byte reportId)
    {
        ushort length = Info.InputReportLength;
        if (length == 0) return null;
        var buffer = new byte[length];
        buffer[0] = reportId;
        return NativeHid.HidD_GetInputReport(_stream.SafeFileHandle, buffer, length) ? buffer : null;
    }

    /// <summary>
    /// 读该接口**输出报文**的 report ID（由 caps 的 ReportID 字段给出）。0 = 该接口不使用 report ID；读不到返回 null。
    /// **绝不能硬编码**（北通 T9 三个厂商集合 1/4/5、鲲鹏20 是 2）：写一个该接口没有的 report ID，驱动直接报 ERROR_INVALID_FUNCTION。
    /// 用户态拿不到原始报告描述符（hid.dll 不导出 HidP_GetReportDescriptor），故走 caps。详见 docs/hardware/betop.md §1.3。
    /// </summary>
    public byte? GetOutputReportId()
    {
        if (!NativeHid.HidD_GetPreparsedData(_stream.SafeFileHandle, out var preparsed)) return null;
        try
        {
            // 输出集合优先；个别接口的输出集合没有 caps（纯 padding），退回输入集合——
            // Report ID 是 HID 全局项，同一接口的各报文类型共用同一组 ID。
            var id = NativeHid.ReadReportId(preparsed, NativeHid.HIDP_REPORT_TYPE.HidP_Output);
            id ??= NativeHid.ReadReportId(preparsed, NativeHid.HIDP_REPORT_TYPE.HidP_Input);
            return id;
        }
        catch
        {
            return null;
        }
        finally
        {
            NativeHid.HidD_FreePreparsedData(preparsed);
        }
    }

    /// <summary>发送 feature report（带自定义 payload），成功后可选择立刻读回。</summary>
    public bool SetFeature(byte reportId, byte[] payload)
    {
        ushort len = Info.FeatureReportLength;
        if (len == 0) return false;
        var buffer = new byte[len];
        buffer[0] = reportId;
        Array.Copy(payload, 0, buffer, 1, Math.Min(payload.Length, len - 1));
        return NativeHid.HidD_SetFeature(_stream.SafeFileHandle, buffer, len);
    }

    /// <summary>
    /// 写 output report（WriteFile，异步带超时）。buffer[0] 是 report ID。
    /// <paramref name="quiet"/> = true 时不把失败记成 WARN——调用方已知该失败是**预期结果**（如探测不属于本协议的通道，写失败正是"不是这条通道"的判据）。
    /// **别用它掩盖真正的写入故障。**
    /// </summary>
    public bool WriteOutput(byte[] report, TimeSpan timeout, bool quiet = false)
    {
        try
        {
            var writeTask = _stream.WriteAsync(report, 0, report.Length);
            writeTask.Wait(timeout);
            if (!writeTask.IsCompletedSuccessfully)
            {
                int err = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                if (!quiet) Utils.Log.Warn($"WriteOutput 失败 len={report.Length} lastWin32Err={err}");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            if (!quiet) Utils.Log.Warn($"WriteOutput 异常 len={report.Length}: {ex.Message}");
            return false;
        }
    }

    // 单悬空读：超时后旧 ReadAsync 仍挂在驱动队列上（无法取消），重复开新读会导致
    // 设备响应被旧读吃掉。整个句柄生命周期只保留一个 pending read，数据来了在下次调用时取走。
    private Task<int>? _pendingRead;
    private byte[] _readBuffer = Array.Empty<byte>();

    /// <summary>读一个 input report（阻塞等待）。发现路径请用 <see cref="ReadInputAsync"/>，避免堵死线程池。</summary>
    public byte[]? ReadInput(TimeSpan timeout)
        => ReadInputAsync(timeout).GetAwaiter().GetResult();

    /// <summary>读一个 input report（可等待）。超时后旧读继续挂着，下次调用再取走。</summary>
    public async Task<byte[]?> ReadInputAsync(TimeSpan timeout)
    {
        ushort len = Info.InputReportLength;
        if (len == 0) return null;
        try
        {
            if (_pendingRead == null)
            {
                _readBuffer = new byte[len];
                _pendingRead = _stream.ReadAsync(_readBuffer, 0, len);
            }
            var completed = await Task.WhenAny(_pendingRead, Task.Delay(timeout)).ConfigureAwait(false);
            if (completed != _pendingRead)
                return null;
            int n = await _pendingRead.ConfigureAwait(false);
            _pendingRead = null;
            if (n <= 0) return null;
            var result = new byte[n];
            Array.Copy(_readBuffer, result, n);
            return result;
        }
        catch (Exception ex)
        {
            // 读失败多半是设备拔出/句柄失效（预期），记 Debug 即可（CR-21）。
            if (Utils.Log.IsEnabled(Utils.LogLevel.Debug))
                Utils.Log.Debug($"ReadInput 失败: {ex.Message}");
            _pendingRead = null;
            return null;
        }
    }

    public void Dispose()
    {
        _stream.Dispose();
    }
}
