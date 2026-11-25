using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Logitech;

/// <summary>
/// 功 能： HID++ 通知的入队与派发——读报文的一方只入队，派发一律在 I/O 锁外进行。
/// 2026/11/01 18:14:36 20211
/// 派发期间重入只入队不递归，理由与栈溢出事故见 docs/hardware/brands/logitech.md §3、issue #55。
/// </summary>
internal sealed class HidppNotificationQueue
{
    /// <summary>积压上限：超出丢最旧。设备刷屏时宁可少几条事件，也不能无限占内存。</summary>
    private const int Capacity = 256;

    private readonly Queue<(byte DevIdx, byte[] Data)> _queue = new();
    private readonly object _gate = new();

    /// <summary>1 = 正在派发。用 Interlocked 而非 lock：派发必须在 I/O 锁外，且不能被同线程重入。</summary>
    private int _draining;

    private long _dropped;

    /// <summary>因积压上限被丢弃的通知条数（诊断用）。</summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    public int Count
    {
        get { lock (_gate) return _queue.Count; }
    }

    /// <summary>
    /// 若 <paramref name="report"/> 是设备自发通知则入队并返回 true。
    /// HID++ 2.0 通知的 software id（低 4 位）为 0；请求应答带 SwId=0x0B，不能被当成通知。
    /// </summary>
    public bool TryEnqueue(byte[] report)
    {
        if (report.Length < 4) return false;
        byte reportId = report[0];
        if (reportId != HidppCodec.ShortReportId && reportId != HidppCodec.LongReportId) return false;
        if ((report[3] & 0x0F) != 0) return false;

        var data = new byte[report.Length - 2];
        Array.Copy(report, 2, data, 0, data.Length);

        lock (_gate)
        {
            if (_queue.Count >= Capacity)
            {
                _queue.Dequeue();
                Interlocked.Increment(ref _dropped);
            }
            _queue.Enqueue((report[1], data));
        }
        return true;
    }

    /// <summary>
    /// 在 <paramref name="budget"/> 内把队列派发干净，返回派发条数。
    /// 已在派发中（同线程重入或另一线程并发）时立即返回 0：新入队的条目由外层循环接手，
    /// 于是"处理器里再发请求 → 又来通知"变成平循环而不是深递归。
    /// </summary>
    public int Drain(Action<byte, byte[]> handler, TimeSpan budget)
    {
        if (Interlocked.CompareExchange(ref _draining, 1, 0) != 0) return 0;

        int delivered = 0;
        var deadline = DateTime.UtcNow + budget;
        try
        {
            while (DateTime.UtcNow < deadline && TryDequeue(out byte devIdx, out var data))
            {
                try
                {
                    handler(devIdx, data);
                }
                catch (Exception ex)
                {
                    Log.Warn($"HID++ 通知处理异常: {ex.Message}");
                }
                delivered++;
            }
        }
        finally
        {
            Volatile.Write(ref _draining, 0);
        }
        return delivered;
    }

    private bool TryDequeue(out byte devIdx, out byte[] data)
    {
        lock (_gate)
        {
            if (_queue.Count > 0)
            {
                var item = _queue.Dequeue();
                devIdx = item.DevIdx;
                data = item.Data;
                return true;
            }
        }
        devIdx = 0;
        data = Array.Empty<byte>();
        return false;
    }
}
