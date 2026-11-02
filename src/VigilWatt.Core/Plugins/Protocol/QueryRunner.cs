using VigilWatt.Core.Hid;

namespace VigilWatt.Core.Plugins.Protocol;

/// <summary>
/// 功 能： 按 <see cref="ActiveSpec"/> 发查询帧并读到截止，重试次数与窗口全部来自 JSON。
/// 2026/11/01 23:20:00 xliu
/// 写失败一律视为"不是这条通道"（quiet），与各插件原来的口径一致。
/// </summary>
internal static class QueryRunner
{
    private static readonly TimeSpan WriteTimeout = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan Slice = TimeSpan.FromMilliseconds(300);

    /// <summary>同步版：给 <c>PollDevice</c> 这类不能 await 的入口用。</summary>
    public static FrameReading? Run(HidDeviceHandle handle, ActiveSpec spec)
    {
        for (int attempt = 1; attempt <= spec.Attempts; attempt++)
        {
            if (!handle.WriteOutput(spec.Query, WriteTimeout, quiet: true)) return null;

            var deadline = DateTime.UtcNow.AddMilliseconds(spec.WindowMs);
            while (DateTime.UtcNow < deadline)
            {
                var slice = Remaining(deadline);
                if (slice <= TimeSpan.Zero) break;

                var input = handle.ReadInput(slice);
                if (input is null) continue;
                if (TryParse(spec, input) is { } reading) return reading;
            }
        }
        return null;
    }

    /// <summary>异步版：给探测用，避免阻塞其它插件的扫描。</summary>
    public static async Task<FrameReading?> RunAsync(HidDeviceHandle handle, ActiveSpec spec)
    {
        for (int attempt = 1; attempt <= spec.Attempts; attempt++)
        {
            if (!handle.WriteOutput(spec.Query, WriteTimeout, quiet: true)) return null;

            var deadline = DateTime.UtcNow.AddMilliseconds(spec.WindowMs);
            while (DateTime.UtcNow < deadline)
            {
                var slice = Remaining(deadline);
                if (slice <= TimeSpan.Zero) break;

                var input = await handle.ReadInputAsync(slice).ConfigureAwait(false);
                if (input is null) continue;
                if (TryParse(spec, input) is { } reading) return reading;
            }
        }
        return null;
    }

    /// <summary>有的接收器会把主机写入回显进输入队列，整帧等于查询帧时丢弃。</summary>
    private static FrameReading? TryParse(ActiveSpec spec, byte[] input)
    {
        if (spec.RejectQueryEcho && input.AsSpan().SequenceEqual(spec.Query)) return null;
        return FrameReader.Read(spec.Response, input);
    }

    private static TimeSpan Remaining(DateTime deadline)
    {
        var remaining = deadline - DateTime.UtcNow;
        return remaining < Slice ? remaining : Slice;
    }
}
