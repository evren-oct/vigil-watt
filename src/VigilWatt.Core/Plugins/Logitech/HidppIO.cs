using VigilWatt.Core.Hid;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Logitech;

/// <summary>
/// 功 能： HID++ 底层 I/O：通过 vendor 接口写 output report 发请求、读 input report 匹配响应（报文格式对照 Solaar base.py）。
/// 2026/10/06 15:53:36 xliu
/// 通知只入队、出锁后才派发——在等包循环里同步回调会让"处理器再发请求"变成深递归（issue #55）。详见 docs/hardware/logitech.md。
/// </summary>
internal sealed class HidppIO : IDisposable
{
    public const byte SwId = 0x0B; // Solaar 约定，避开 G HUB(0x0D)/OpenRGB(0x07)/LGSTray(0x0A)

    /// <summary>单次泵出的时间预算：按时间不按条数，否则 N 条 × 请求超时会把这一个线程占满几分钟。</summary>
    private static readonly TimeSpan DrainBudget = TimeSpan.FromMilliseconds(200);

    /// <summary>写被系统拒绝后停写这么久。写失败说明接口本身不通（不是设备睡着），继续戳只会刷屏。</summary>
    private static readonly TimeSpan WriteBrokenCooldown = TimeSpan.FromSeconds(30);

    private readonly HidDeviceHandle _handle;
    private readonly byte _reportId;
    private readonly bool _longMessage;
    private readonly object _ioGate = new();
    private readonly HidppNotificationQueue _notifications = new();

    private DateTimeOffset _writeBrokenUntil;

    /// <summary>底层句柄（供探测工具使用）。</summary>
    public HidDeviceHandle Handle => _handle;

    /// <summary>本接口实际使用的 report ID（0x10 或 0x11）。</summary>
    public byte ReportId => _reportId;

    /// <summary>本接口是否走长报文（20 字节）。0x2202 这类长响应 feature 只能在这里读。</summary>
    public bool IsLongMessage => _longMessage;

    /// <summary>设备自发通知：参数为 (devIdx, data)，data 从 feature 字节起（与 <see cref="Request"/> 返回值同布局）。
    /// 在请求等待与空闲 Pump 时都会触发；派发已在 I/O 锁外进行，处理器仍应尽快返回，别做长耗时 I/O。</summary>
    public Action<byte, byte[]>? NotificationReceived;

    /// <summary>
    /// 写入许可（安全闸门）：返回 false 时 <see cref="Request"/> **一帧都不发**、直接返回 null。
    /// 这是罗技插件唯一的写入收口（safety.md §3 规则 1/2），由插件按用户开关刷新。
    /// </summary>
    public Func<bool>? WritesAllowed;

    public HidppIO(HidDeviceHandle handle)
    {
        _handle = handle;
        _reportId = HidppCodec.SelectReportId(handle.Info.OutputReportLength, handle.Info.OutputReportId);
        _longMessage = HidppCodec.IsLongMessage(_reportId);
    }

    /// <summary>发送一条 HID++ 请求并等待匹配的响应（返回响应 data 部分，不含 report id 和 devIdx）；等待期间穿插的设备通知经 <see cref="NotificationReceived"/> 抛出。</summary>
    public byte[]? Request(byte devIndex, ushort featureIdAndFunc, ReadOnlySpan<byte> args,
        bool patchSw = true, TimeSpan? timeout = null)
    {
        ushort requestId = patchSw
            ? (ushort)((featureIdAndFunc & 0xFFF0) | SwId)   // (feature<<8)|(func<<4)|sw
            : featureIdAndFunc;
        int dataLen = _longMessage ? 18 : 5;
        var data = new byte[dataLen];
        data[0] = (byte)(requestId >> 8);
        data[1] = (byte)requestId;
        for (int i = 0; i < args.Length && 2 + i < dataLen; i++)
            data[2 + i] = args[i];

        var packet = new byte[dataLen + 2];
        packet[0] = _reportId;
        packet[1] = devIndex;
        Array.Copy(data, 0, packet, 2, dataLen);

        if (packet.Length < _handle.Info.OutputReportLength)
            Array.Resize(ref packet, _handle.Info.OutputReportLength);

        var wait = timeout ?? TimeSpan.FromSeconds(3);

        byte[]? reply = null;
        lock (_ioGate)
        {
            // 写入闸门（安全规则 R1）：这是本插件唯一的写点，许可关闭时连一次 WriteOutput 都不发。
            if (WritesAllowed != null && !WritesAllowed())
            {
                Log.Debug($"HID++ 写入被开关挡住 dev={devIndex} req=0x{requestId:X4}");
            }
            else if (DateTimeOffset.UtcNow >= _writeBrokenUntil)
            {
                if (!_handle.WriteOutput(packet, TimeSpan.FromMilliseconds(500)))
                {
                    // 写不进去说明这条接口本身不通（G102 的有线 HID++ 通道实测如此）：
                    // 不停一会儿的话，每条通知都会再触发一轮"写→等→超时"。
                    _writeBrokenUntil = DateTimeOffset.UtcNow + WriteBrokenCooldown;
                    Log.Warn($"HID++ 写失败 dev={devIndex} req=0x{requestId:X4} report=0x{_reportId:X2}，" +
                             $"停写 {WriteBrokenCooldown.TotalSeconds:0} 秒");
                }
                else
                {
                    _writeBrokenUntil = default;
                    reply = ReadMatching(devIndex, data, wait);
                }
            }
            // 冷却期内静默跳过：进入冷却时已经 Warn 过一次，这里再记就是刷屏。
        }

        DrainNotifications();
        return reply;
    }

    /// <summary>空闲泵：只读不写，把超时窗口内到达的通知交给 <see cref="NotificationReceived"/>；与 <see cref="Request"/> 互斥（同一把锁），零写入，供后台线程持续监听插拔电等事件。</summary>
    public void PumpNotifications(TimeSpan wait)
    {
        if (wait <= TimeSpan.Zero) return;
        lock (_ioGate)
        {
            var deadline = DateTime.UtcNow + wait;
            while (DateTime.UtcNow < deadline)
            {
                var remaining = deadline - DateTime.UtcNow;
                var input = _handle.ReadInput(remaining > TimeSpan.FromMilliseconds(50)
                    ? remaining
                    : TimeSpan.FromMilliseconds(50));
                if (input == null) break;
                _notifications.TryEnqueue(input);
            }
        }

        DrainNotifications();
    }

    /// <summary>读输入流直到出现前两字节与请求匹配的响应；通知报文入队后继续等。</summary>
    private byte[]? ReadMatching(byte devIndex, byte[] requestData, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var remaining = deadline - DateTime.UtcNow;
            var input = _handle.ReadInput(remaining > TimeSpan.FromMilliseconds(100)
                ? remaining
                : TimeSpan.FromMilliseconds(100));
            if (input == null) break;

            byte reportId = input[0];
            if (reportId != HidppCodec.ShortReportId && reportId != HidppCodec.LongReportId) continue;

            // 设备自发通知（sw=0）：只入队，出锁后再派发（issue #55 的递归环就出在这句同步回调上）
            if (_notifications.TryEnqueue(input))
                continue;

            byte replyDev = input[1];
            bool devMatch = replyDev == devIndex || replyDev == (byte)~devIndex;
            if (!devMatch) continue;

            int dataStart = 2;
            if (input.Length < dataStart + 2) continue;
            if (input[dataStart] != requestData[0] || input[dataStart + 1] != requestData[1]) continue;

            int dataLen = input.Length - dataStart;
            var reply = new byte[dataLen];
            Array.Copy(input, dataStart, reply, 0, dataLen);

            if (reply[0] == 0x8F || reply[0] == 0xFF)
                return null;
            return reply;
        }
        Log.Warn($"HID++ 响应超时 dev={devIndex} req=0x{requestData[0]:X2}{requestData[1]:X2} report=0x{_reportId:X2}");
        return null;
    }

    /// <summary>把队列里的通知派发出去（必须在 <see cref="_ioGate"/> 之外调用）。</summary>
    private void DrainNotifications()
    {
        if (NotificationReceived is not { } handler) return;
        _notifications.Drain(handler, DrainBudget);
    }

    public void Dispose() => _handle.Dispose();
}
