using VigilWatt.Core.Hid;

namespace VigilWatt.Core.Capture.Hid;
/// <summary>
/// 功 能： USB/HID 采集来源：被动读输入报告，可选按用户指定的 report ID 做只读 GET_REPORT。
/// 2026/10/24 12:40:43 20211
/// **默认只申请读权限**；只有用户在本会话显式授权（CaptureOptions.AllowSend）才申请写权限，
/// 且写入只发生在用户手动点「发送」时——本来源从不自己发帧。
/// 读异常被 HidDeviceHandle 吞成 null（BACKLOG CR-174），只有接口从系统消失才报 device_removed。
/// </summary>
public sealed class UsbHidCaptureSource : ICaptureSource, ICaptureWriter
{
    private readonly CaptureDeviceInfo _device;
    private readonly IReadOnlyList<byte> _featureReportIds;
    private readonly TimeSpan _readTimeout;
    private readonly TimeSpan _featureInterval;
    private readonly bool _allowSend;
    private int _consecutiveFeatureFailures;
    private HidDeviceHandle? _handle;

    public UsbHidCaptureSource(CaptureDeviceInfo device, IReadOnlyList<byte> featureReportIds,
        TimeSpan? readTimeout = null, TimeSpan? featureInterval = null, bool allowSend = false)
    {
        _device = device;
        _featureReportIds = featureReportIds;
        _readTimeout = readTimeout ?? TimeSpan.FromMilliseconds(250);
        _featureInterval = featureInterval ?? TimeSpan.FromSeconds(1);
        _allowSend = allowSend;
    }

    public string SourceName => "usb-hid";

    /// <summary>用户手动发一帧。**只在采集运行中可用**，写失败要如实返回原因。</summary>
    public bool TrySend(byte[] report, out string? error)
    {
        if (!_allowSend)
        {
            error = "本会话没有开启「允许发送」，句柄是只读的";
            return false;
        }

        var handle = _handle;
        if (handle == null)
        {
            error = "采集未在运行（句柄已释放）";
            return false;
        }

        if (!handle.WriteOutput(report, TimeSpan.FromSeconds(1)))
        {
            error = "WriteFile 失败，设备可能不接受这一帧";
            return false;
        }

        error = null;
        return true;
    }

    public async Task RunAsync(CaptureClock clock, ICaptureSink sink, CancellationToken token)
    {
        HidDeviceHandle handle;
        try
        {
            // 只有本会话被授权发帧时才申请写权限：没勾选的会话从权限层面就写不了
            handle = _allowSend
                ? _device.ToHidDeviceInfo().OpenReadWrite()
                : _device.ToHidDeviceInfo().OpenRead();
        }
        catch (UnauthorizedAccessException ex)
        {
            sink.Error(Error(clock, CaptureErrorKind.OpenDenied, ex.Message, 5));
            return;
        }
        catch (Exception ex)
        {
            sink.Error(Error(clock, CaptureErrorKind.OpenFailed, ex.Message, null));
            return;
        }

        using (handle)
        {
            _handle = handle;
            try
            {
                await PumpAsync(handle, clock, sink, token).ConfigureAwait(false);
            }
            finally
            {
                _handle = null;
            }
        }
    }

    private async Task PumpAsync(HidDeviceHandle handle, CaptureClock clock, ICaptureSink sink, CancellationToken token)
    {
        var nextFeatureAt = clock.ElapsedMs;
        var lastPresenceCheck = clock.ElapsedMs;

        while (!token.IsCancellationRequested)
        {
            if (_featureReportIds.Count > 0 && clock.ElapsedMs >= nextFeatureAt && _consecutiveFeatureFailures < 3)
            {
                nextFeatureAt = clock.ElapsedMs + (long)_featureInterval.TotalMilliseconds;
                PollFeatures(handle, clock, sink);
            }

            var startedAt = clock.ElapsedMs;
            var data = await handle.ReadInputAsync(_readTimeout).ConfigureAwait(false);
            if (data is { Length: > 0 })
            {
                sink.Record(Record(clock, CaptureLayer.HidApiInput, "hid_input_report",
                    ReportIdOf(data), "ReadFile", data));
                continue;
            }

            // 读异常会立刻返回（不走超时路径），这里补一个退避，避免设备拔掉后空转烧 CPU。
            if (clock.ElapsedMs - startedAt < 50) await Task.Delay(200, token).ConfigureAwait(false);

            if (clock.ElapsedMs - lastPresenceCheck < 2000) continue;
            lastPresenceCheck = clock.ElapsedMs;
            if (IsPresent()) continue;

            sink.Error(Error(clock, CaptureErrorKind.DeviceRemoved, "目标接口已从系统消失（设备拔出或重置）", null));
            return;
        }
    }

    private void PollFeatures(HidDeviceHandle handle, CaptureClock clock, ICaptureSink sink)
    {
        foreach (var reportId in _featureReportIds)
        {
            var data = handle.GetFeature(reportId);
            if (data is { Length: > 0 })
            {
                _consecutiveFeatureFailures = 0;
                sink.Record(Record(clock, CaptureLayer.HidApiFeature, "hid_feature_report", reportId,
                    "HidD_GetFeature", data));
                continue;
            }

            _consecutiveFeatureFailures++;
            sink.Error(Error(clock, CaptureErrorKind.FeatureFailed,
                $"读取 Feature report 0x{reportId:X2} 失败", null));
            if (_consecutiveFeatureFailures >= 3)
            {
                sink.Error(Error(clock, CaptureErrorKind.FeatureFailed,
                    "Feature 读取连续失败 3 次，本次会话已停止 Feature 轮询（输入报告继续采集）", null));
                return;
            }
        }
    }

    /// <summary>接口是否还在。只查设备接口列表，不开句柄、不发报文。</summary>
    private bool IsPresent()
        => HidDeviceScanner.EnumeratePaths().Any(p => string.Equals(p, _device.InterfacePath, StringComparison.OrdinalIgnoreCase));

    /// <summary>用了 report ID 的接口，ReadFile 返回的第一个字节就是 ID；不用的接口首字节是数据。</summary>
    private byte? ReportIdOf(byte[] data)
        => _device.UsesReportId && data.Length > 0 ? data[0] : null;

    private CaptureRecord Record(CaptureClock clock, CaptureLayer layer, string recordType,
        byte? reportId, string api, byte[] data) => new()
    {
        TimestampMono = clock.ElapsedMs,
        TimestampUtc = DateTimeOffset.UtcNow,
        Layer = layer,
        Source = SourceName,
        DeviceId = _device.DeviceId,
        InterfaceId = _device.InterfacePath,
        RecordType = recordType,
        ReportId = reportId,
        Api = api,
        Data = data,
        Status = CaptureRecordStatus.Received,
    };

    private CaptureError Error(CaptureClock clock, CaptureErrorKind kind, string message, int? hresult) => new()
    {
        TimestampMono = clock.ElapsedMs,
        TimestampUtc = DateTimeOffset.UtcNow,
        Kind = kind,
        Message = message,
        HResult = hresult,
        DeviceId = _device.DeviceId,
        InterfaceId = _device.InterfacePath,
    };

    public void Dispose()
    {
        // 句柄在 RunAsync 内 using 释放；这里没有长持有资源，保留接口以便未来扩展。
    }
}
