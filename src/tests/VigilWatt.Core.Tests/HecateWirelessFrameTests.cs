using VigilWatt.Core.Plugins.Pulsar;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： HECATE Wireless Receiver（35BB:D3FB）两条真机应答经 PulsarCodec 的校验与解析。
/// 2026/10/23 20:22:34 22469
/// 样本为本机 2026-10-23 的 STATUS/POWER 应答；充电位未验证，见 docs/hardware/hecate.md §2。
/// </summary>
public class HecateWirelessFrameTests
{
    private static readonly byte[] StatusReply =
        Convert.FromHexString("0803000000010131AFF9000000000000" + "6F");

    /// <summary>50%、充电位 0、0x0F08 = 3848 mV。</summary>
    private static readonly byte[] PowerReply =
        Convert.FromHexString("08040000000232000F08000000000000FE");

    [Fact]
    public void Real_replies_pass_checksum()
    {
        Assert.Equal(StatusReply[^1], PulsarCodec.Checksum(StatusReply));
        Assert.Equal(PowerReply[^1], PulsarCodec.Checksum(PowerReply));
    }

    [Fact]
    public void Status_reply_reports_online()
    {
        Assert.True(PulsarCodec.TryParse(StatusReply, PulsarCodec.CmdStatus, out var payload));
        Assert.True(PulsarCodec.TryParseOnline(payload, out bool online));
        Assert.True(online);
    }

    [Fact]
    public void Power_reply_reads_percent_and_millivolts()
    {
        Assert.True(PulsarCodec.TryParse(PowerReply, PulsarCodec.CmdPower, out var payload));
        Assert.True(PulsarCodec.TryParsePower(payload, out int percent, out bool charging, out int mv));
        Assert.Equal(50, percent);
        Assert.False(charging);
        Assert.Equal(3848, mv);
    }
}
