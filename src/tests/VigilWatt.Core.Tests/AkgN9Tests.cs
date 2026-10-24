using VigilWatt.Core.Plugins.Akg;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 覆盖 N9 真机电量帧、分片、陈旧缓存和单次查询安全边界。
/// 2026/10/23 21:07:10 30149
/// </summary>
public sealed class AkgN9Tests
{
    private const string Path = @"\\?\hid#vid_0ecb&pid_20d9&mi_03&col02#instance";
    private static readonly byte[] Ack = Convert.FromHexString("055B0300D60C00");
    private static byte[] Result(byte percent = 50) => new byte[] { 5, 0x5D, 5, 0, 0xD6, 0x0C, 0, 0, percent };

    private static byte[] Report(params byte[] payload)
    {
        var report = new byte[AkgN9Codec.ReportLength];
        report[0] = 7;
        report[1] = (byte)payload.Length;
        report[2] = 0x80;
        payload.CopyTo(report, 3);
        return report;
    }

    [Fact]
    public void QueryMatchesVerifiedOutput()
        => Assert.Equal(Convert.FromHexString("060780055A0300D60C00" + new string('0', 104)),
            AkgN9Codec.CreateBatteryQuery());

    [Fact]
    public void OnlyExactN9ControlCollectionIsSupported()
    {
        Assert.True(AkgN9Codec.IsSupportedInterface(0x0ECB, 0x20D9, Path.ToUpperInvariant(), 0xFF13, 1, 62, 62, 0, 6));
        Assert.False(AkgN9Codec.IsSupportedInterface(0x0ECB, 0x20D8, Path, 0xFF13, 1, 62, 62, 0, 6));
        Assert.False(AkgN9Codec.IsSupportedInterface(0x3329, 0x20D9, Path, 0xFF13, 1, 62, 62, 0, 6));
        Assert.False(AkgN9Codec.IsSupportedInterface(0x0ECB, 0x20D9, Path, 0x000B, 1, 62, 62, 0, 6));
        Assert.False(AkgN9Codec.IsSupportedInterface(0x0ECB, 0x20D9, Path, 0xFF13, 2, 62, 62, 0, 6));
        Assert.False(AkgN9Codec.IsSupportedInterface(0x0ECB, 0x20D9, Path, 0xFF13, 1, 64, 62, 0, 6));
        Assert.False(AkgN9Codec.IsSupportedInterface(0x0ECB, 0x20D9, Path, 0xFF13, 1, 62, 64, 0, 6));
        Assert.False(AkgN9Codec.IsSupportedInterface(0x0ECB, 0x20D9, Path, 0xFF13, 1, 62, 62, 17, 6));
        Assert.False(AkgN9Codec.IsSupportedInterface(0x0ECB, 0x20D9, Path, 0xFF13, 1, 62, 62, 0, null));
        Assert.False(AkgN9Codec.IsSupportedInterface(0x0ECB, 0x20D9, Path, 0xFF13, 1, 62, 62, 0, 7));
    }

    [Theory]
    [InlineData("&mi_03&col04#")]
    [InlineData("&mi_03&col03#")]
    [InlineData("&mi_030&col02#")]
    [InlineData("&mi_03&col020#")]
    public void OtherCollectionsAreRejected(string segment)
        => Assert.False(AkgN9Codec.IsSupportedInterface(0x0ECB, 0x20D9,
            Path.Replace("&mi_03&col02#", segment), 0xFF13, 1, 62, 62, 0, 6));

    [Fact]
    public void RealResponseHasAckAnd50PercentDespiteTrailingOldBytes()
    {
        var report = Convert.FromHexString(
            "071080055B0300D60C00055D0500D60C00003201090500000A0700055D08000109050000010400055D080001090500000A0700055D080001090500000104");
        Assert.True(new AkgN9PacketAssembler().TryAccept(report, out var messages));
        Assert.Equal(2, messages.Count);
        Assert.True(AkgN9Codec.IsSuccessfulAck(messages[0]));
        Assert.True(AkgN9Codec.TryParseBattery(messages[1], out int percent));
        Assert.Equal(50, percent);
    }

    [Fact]
    public void SplitResultSurvivesEmptyFragmentWithStaleBytes()
    {
        var assembler = new AkgN9PacketAssembler();
        var result = Result();
        Assert.True(assembler.TryAccept(Report(result[..5]), out var first));
        Assert.Empty(first);
        var stale = Report(Result(90));
        stale[1] = 0;
        Assert.True(assembler.TryAccept(stale, out var empty));
        Assert.Empty(empty);
        Assert.True(assembler.TryAccept(Report(result[5..]), out var last));
        Assert.Equal(result, Assert.Single(last));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(100)]
    public void BatteryPercentBoundsAreValid(byte percent)
    {
        Assert.True(AkgN9Codec.TryParseBattery(Result(percent), out int actual));
        Assert.Equal(percent, actual);
    }

    [Theory]
    [InlineData(101)]
    [InlineData(255)]
    public void InvalidPercentIsRejected(byte percent)
        => Assert.False(AkgN9Codec.TryParseBattery(Result(percent), out _));

    [Fact]
    public void FailedStatusWrongAgentAndOtherCommandsAreRejected()
    {
        foreach (int offset in new[] { 0, 1, 2, 3, 4, 5, 6, 7 })
        {
            var message = Result();
            message[offset] ^= 1;
            Assert.False(AkgN9Codec.TryParseBattery(message, out _));
        }
        Assert.False(AkgN9Codec.TryParseBattery(Ack, out _));
        Assert.False(AkgN9Codec.TryParseBattery(Result()[..8], out _));
    }

    [Fact]
    public void MalformedFragmentAndMessageLengthsAreRejected()
    {
        foreach (int offset in new[] { 0, 2 })
        {
            var report = Report(Result());
            report[offset] ^= 1;
            Assert.False(new AkgN9PacketAssembler().TryAccept(report, out _));
        }
        var oversized = Report();
        oversized[1] = 60;
        Assert.False(new AkgN9PacketAssembler().TryAccept(oversized, out _));
        Assert.False(new AkgN9PacketAssembler().TryAccept(new byte[3], out _));
        foreach (string hex in new[] { "045D0500", "055A0500", "055D0100", "055DFD03" })
            Assert.False(new AkgN9PacketAssembler().TryAccept(Report(Convert.FromHexString(hex)), out _));
    }

    [Fact]
    public void MaximumMessageCanBeFollowedByBatteryInTheSameFragmentStream()
    {
        var unrelated = new byte[AkgN9Codec.MaxMessageLength];
        Convert.FromHexString("055DFC030109").CopyTo(unrelated, 0);
        var stream = unrelated.Concat(Result()).ToArray();
        var assembler = new AkgN9PacketAssembler();
        var messages = new List<byte[]>();
        foreach (var chunk in stream.Chunk(59))
        {
            Assert.True(assembler.TryAccept(Report(chunk), out var ready));
            messages.AddRange(ready);
        }
        Assert.Equal(2, messages.Count);
        Assert.Equal(unrelated, messages[0]);
        Assert.True(AkgN9Codec.TryParseBattery(messages[1], out int percent));
        Assert.Equal(50, percent);
    }

    [Fact]
    public void DisabledTransportDoesNotAccessDevice()
    {
        int reads = 0, writes = 0;
        Assert.Null(AkgN9Transport.Query(() => false, _ => { writes++; return true; },
            () => { reads++; return Report(); }, _ => { }));
        Assert.Equal(0, reads);
        Assert.Equal(0, writes);
    }

    [Fact]
    public void DisablingAfterDrainPreventsQuery()
    {
        bool enabled = true;
        int writes = 0;
        Assert.Null(AkgN9Transport.Query(() => enabled, _ => { writes++; return true; },
            () => { enabled = false; return Report(); }, _ => { }));
        Assert.Equal(0, writes);
    }

    [Fact]
    public void OldBatteryIsDrainedAndCurrentResultIsReturnedWithOneWrite()
    {
        var queue = new Queue<byte[]>(new[] { Report(Result(90)), Report(), Report(Ack), Report(Result(50)) });
        int writes = 0;
        var reading = AkgN9Transport.Query(() => true, query =>
        {
            Assert.Equal(AkgN9Codec.CreateBatteryQuery(), query);
            writes++;
            return true;
        }, () => queue.Dequeue(), _ => { });
        Assert.True(reading.HasValue);
        Assert.Equal(50, reading.Value.Percent);
        Assert.Equal(Result(50), reading.Value.Message);
        Assert.Equal(1, writes);
        Assert.Empty(queue);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LateResultBeforeAckIsIgnored(bool sameReport)
    {
        var queue = new Queue<byte[]>();
        queue.Enqueue(Report());
        if (sameReport)
            queue.Enqueue(Report(Result(90).Concat(Ack).Concat(Result(50)).ToArray()));
        else
        {
            queue.Enqueue(Report(Result(90)));
            queue.Enqueue(Report(Ack.Concat(Result(50)).ToArray()));
        }
        int writes = 0;
        var reading = AkgN9Transport.Query(() => true, _ => { writes++; return true; },
            () => queue.Dequeue(), _ => { });
        Assert.True(reading.HasValue);
        Assert.Equal(50, reading.Value.Percent);
        Assert.Equal(Result(50), reading.Value.Message);
        Assert.Equal(1, writes);
        Assert.Empty(queue);
    }

    [Fact]
    public void ResultWithoutAckIsNotAccepted()
    {
        var queue = new Queue<byte[]>(new[] { Report(), Report(Result(90)) });
        int writes = 0;
        Assert.Null(AkgN9Transport.Query(() => true, _ => { writes++; return true; },
            () => queue.Count > 0 ? queue.Dequeue() : Report(), _ => { }));
        Assert.Equal(1, writes);
        Assert.Empty(queue);
    }

    [Fact]
    public void AckFromTimedOutQueryDoesNotAuthorizeNextResult()
    {
        var queue = new Queue<byte[]>(new[] { Report(), Report(Ack) });
        int writes = 0;
        (int Percent, byte[] Message)? Query() => AkgN9Transport.Query(() => true,
            _ => { writes++; return true; }, () => queue.Count > 0 ? queue.Dequeue() : Report(), _ => { });
        Assert.Null(Query());
        queue.Enqueue(Report());
        queue.Enqueue(Report(Result(90)));
        queue.Enqueue(Report(Ack.Concat(Result(50)).ToArray()));
        var reading = Query();
        Assert.True(reading.HasValue);
        Assert.Equal(50, reading.Value.Percent);
        Assert.Equal(Result(50), reading.Value.Message);
        Assert.Equal(2, writes);
        Assert.Empty(queue);
    }

    [Fact]
    public void PersistentNonemptySnapshotIsBoundedAndPreventsWrite()
    {
        int reads = 0, writes = 0;
        Assert.Null(AkgN9Transport.Query(() => true, _ => { writes++; return true; },
            () => { reads++; return Report(Result()); }, _ => { }));
        Assert.Equal(20, reads);
        Assert.Equal(0, writes);
    }

    [Fact]
    public void NoResponseDoesNotResendAndHasBoundedReads()
    {
        int reads = 0, writes = 0;
        Assert.Null(AkgN9Transport.Query(() => true, _ => { writes++; return true; },
            () => { reads++; return Report(); }, _ => { }));
        Assert.Equal(1, writes);
        Assert.Equal(61, reads);
    }

    [Fact]
    public void WriteFailureDoesNotReadResponseOrRetry()
    {
        int reads = 0, writes = 0;
        Assert.Null(AkgN9Transport.Query(() => true, _ => { writes++; return false; },
            () => { reads++; return Report(); }, _ => { }));
        Assert.Equal(1, reads);
        Assert.Equal(1, writes);
    }

    [Fact]
    public void RejectedAckStopsWithoutRetry()
    {
        var rejected = (byte[])Ack.Clone();
        rejected[6] = 1;
        var queue = new Queue<byte[]>(new[] { Report(), Report(rejected) });
        int writes = 0;
        Assert.Null(AkgN9Transport.Query(() => true, _ => { writes++; return true; },
            () => queue.Dequeue(), _ => { }));
        Assert.Equal(1, writes);
        Assert.Empty(queue);
    }

    [Fact]
    public void ReadFailureBeforeQueryDoesNotWrite()
    {
        int writes = 0, reads = 0;
        Assert.Null(AkgN9Transport.Query(() => true, _ => { writes++; return true; },
            () => { reads++; return null; }, _ => { }));
        Assert.Equal(20, reads);
        Assert.Equal(0, writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TransientReadFailuresRecoverWithinTheWindow(bool afterWrite)
    {
        var wrongId = Report();
        wrongId[0] = 6;
        int reads = 0, writes = 0;
        var result = AkgN9Transport.Query(() => true, _ => { writes++; return true; }, () =>
        {
            int step = reads++;
            if (afterWrite && step == 0) return Report();
            step -= afterWrite ? 1 : 0;
            if (step == 0) return null;
            if (step == 1) return wrongId;
            return writes == 0 ? Report() : Report(Ack.Concat(Result()).ToArray());
        }, _ => { });
        Assert.True(result.HasValue);
        Assert.Equal(50, result.Value.Percent);
        Assert.Equal(1, writes);
        Assert.Equal(4, reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedPartialSnapshotIsAppendedOnce(bool stalePaddingChanges)
    {
        var first = Report(Ack.Concat(Result()[..5]).ToArray());
        var repeated = first.ToArray();
        if (stalePaddingChanges) repeated[^1] = 0xA5;
        int reads = 0, writes = 0;
        var result = AkgN9Transport.Query(() => true, _ => { writes++; return true; }, () => reads++ switch
        {
            0 => Report(),
            1 => first,
            2 => repeated,
            _ => Report(Result()[5..]),
        }, _ => { });
        Assert.True(result.HasValue);
        Assert.Equal(Result(), result.Value.Message);
        Assert.Equal(1, writes);
        Assert.Equal(4, reads);
    }

    [Fact]
    public void IdenticalAckAndBatterySnapshotsAreAcceptedInSuccessiveQueries()
    {
        bool pending = false;
        int writes = 0;
        byte[] Read()
        {
            if (!pending) return Report();
            pending = false;
            return Report(Ack.Concat(Result()).ToArray());
        }
        for (int i = 0; i < 2; i++)
        {
            var result = AkgN9Transport.Query(() => true,
                _ => { writes++; pending = true; return true; }, Read, _ => { });
            Assert.True(result.HasValue);
            Assert.Equal(50, result.Value.Percent);
        }
        Assert.Equal(2, writes);
    }

    [Fact]
    public void InvalidExpectedReportFailsClosedWithoutWriting()
    {
        var report = Report();
        report[1] = 60;
        int writes = 0;
        Assert.Null(AkgN9Transport.Query(() => true, _ => { writes++; return true; }, () => report, _ => { }));
        Assert.Equal(0, writes);
    }

    [Fact]
    public void DisabledDuringTransientReadRecoveryStopsWithoutWriting()
    {
        bool enabled = true;
        int reads = 0, writes = 0;
        Assert.Null(AkgN9Transport.Query(() => enabled, _ => { writes++; return true; },
            () => { reads++; return null; }, _ => enabled = false));
        Assert.Equal(1, reads);
        Assert.Equal(0, writes);
    }
}
