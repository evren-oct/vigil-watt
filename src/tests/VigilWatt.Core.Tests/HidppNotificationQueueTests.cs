using VigilWatt.Core.Plugins.Logitech;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： HidppNotificationQueue —— HID++ 通知的入队与派发（不在等包循环里回调）。
/// 2026/11/01 18:14:36 20211
/// 覆盖 issue #55 的递归环：派发期间再入队只能被同一轮接手，不能嵌套调用。
/// </summary>
public class HidppNotificationQueueTests
{
    /// <summary>请求应答的 software id，与 HidppIO.SwId 同值（HidppIO 依赖句柄，测试工程不编译它）。</summary>
    private const byte SwId = 0x0B;

    private static byte[] Frame(byte devIdx, byte feature, byte funcSw, byte param = 0)
    {
        var f = new byte[20];
        f[0] = HidppCodec.LongReportId;
        f[1] = devIdx;
        f[2] = feature;
        f[3] = funcSw;
        f[4] = param;
        return f;
    }

    private static byte[] ShortFrame(byte devIdx, byte feature, byte funcSw)
    {
        var f = new byte[7];
        f[0] = HidppCodec.ShortReportId;
        f[1] = devIdx;
        f[2] = feature;
        f[3] = funcSw;
        return f;
    }

    [Fact]
    public void T1_通知帧入队且data从feature字节起()
    {
        var q = new HidppNotificationQueue();
        Assert.True(q.TryEnqueue(Frame(0x02, 0x0F, 0x00, 0x37)));
        Assert.Equal(1, q.Count);

        var got = new List<(byte DevIdx, byte[] Data)>();
        int n = q.Drain((i, d) => got.Add((i, d)), TimeSpan.FromMilliseconds(200));

        Assert.Equal(1, n);
        var item = Assert.Single(got);
        Assert.Equal(0x02, item.DevIdx);
        Assert.Equal(0x0F, item.Data[0]);   // feature
        Assert.Equal(0x00, item.Data[1]);   // funcSw
        Assert.Equal(0x37, item.Data[2]);   // 参数
        Assert.Equal(0, q.Count);
    }

    [Fact]
    public void T2_应答帧不算通知()
    {
        var q = new HidppNotificationQueue();
        // SwId=0x0B 的是请求应答，不能被当成设备自发通知
        Assert.False(q.TryEnqueue(Frame(0x02, 0x0F, SwId)));
        Assert.Equal(0, q.Count);
    }

    [Fact]
    public void T3_非HID加加报文一律不入队()
    {
        var q = new HidppNotificationQueue();
        Assert.False(q.TryEnqueue(new byte[] { 0x00, 0x02, 0x0F, 0x00 }));
        Assert.False(q.TryEnqueue(new byte[] { 0x12, 0x02, 0x0F, 0x00 }));
        Assert.False(q.TryEnqueue(new byte[] { 0x11, 0x02, 0x0F }));   // 太短
        Assert.Equal(0, q.Count);
    }

    [Fact]
    public void T4_短报文也能入队()
    {
        var q = new HidppNotificationQueue();
        Assert.True(q.TryEnqueue(ShortFrame(0x03, 0x0F, 0x00)));
        Assert.Equal(1, q.Count);
    }

    [Fact]
    public void T5_派发期间重入Drain只入队不递归()
    {
        // 事故现场：处理器里发请求 → 等包期间又收到通知 → 再回调 → 栈溢出 1335 层。
        var q = new HidppNotificationQueue();
        q.TryEnqueue(Frame(0x01, 0x0F, 0x00));

        int innerDrainDelivered = 0;
        int nestedDepth = 0;
        int maxDepth = 0;

        q.Drain((_, _) =>
        {
            nestedDepth++;
            maxDepth = Math.Max(maxDepth, nestedDepth);
            // 模拟"处理器里再发请求、请求又收到通知"：重入 Drain 必须立刻返回 0
            q.TryEnqueue(Frame(0x01, 0x0F, 0x00, 0x11));
            innerDrainDelivered += q.Drain((_, _) => { }, TimeSpan.FromMilliseconds(50));
            nestedDepth--;
        }, TimeSpan.FromMilliseconds(200));

        Assert.Equal(0, innerDrainDelivered);
        Assert.Equal(1, maxDepth);
    }

    [Fact]
    public void T6_派发期间新入队的会被同一轮接手()
    {
        var q = new HidppNotificationQueue();
        q.TryEnqueue(Frame(0x01, 0x0F, 0x00, 0x01));

        var seen = new List<byte>();
        q.Drain((_, d) =>
        {
            seen.Add(d[2]);
            if (seen.Count == 1) q.TryEnqueue(Frame(0x01, 0x0F, 0x00, 0x02));
        }, TimeSpan.FromMilliseconds(200));

        Assert.Equal(new byte[] { 0x01, 0x02 }, seen);
        Assert.Equal(0, q.Count);
    }

    [Fact]
    public void T7_积压超上限丢最旧并计数()
    {
        var q = new HidppNotificationQueue();
        for (int i = 0; i < 300; i++)
            q.TryEnqueue(Frame(0x01, 0x0F, 0x00, (byte)i));

        Assert.Equal(256, q.Count);
        Assert.Equal(44, q.Dropped);

        // 留下的是最新的 256 条：第一条应为 i=44
        byte firstParam = 0xFF;
        q.Drain((_, d) => { if (firstParam == 0xFF) firstParam = d[2]; }, TimeSpan.FromMilliseconds(200));
        Assert.Equal(44, firstParam);
    }

    [Fact]
    public void T8_处理器抛异常不影响后续派发()
    {
        var q = new HidppNotificationQueue();
        q.TryEnqueue(Frame(0x01, 0x0F, 0x00, 0x01));
        q.TryEnqueue(Frame(0x01, 0x0F, 0x00, 0x02));

        int calls = 0;
        q.Drain((_, d) =>
        {
            calls++;
            if (d[2] == 0x01) throw new InvalidOperationException("boom");
        }, TimeSpan.FromMilliseconds(200));

        Assert.Equal(2, calls);
        Assert.Equal(0, q.Count);
    }

    [Fact]
    public void T9_预算用尽即返回_剩余留队列()
    {
        var q = new HidppNotificationQueue();
        for (int i = 0; i < 5; i++) q.TryEnqueue(Frame(0x01, 0x0F, 0x00));

        int calls = 0;
        q.Drain((_, _) =>
        {
            calls++;
            Thread.Sleep(40);   // 单条就超过预算，保证第一条之后即收手
        }, TimeSpan.FromMilliseconds(30));

        Assert.Equal(1, calls);
        Assert.Equal(4, q.Count);
    }
}
