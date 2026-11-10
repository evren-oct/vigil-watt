using VigilWatt.Core.Plugins.Logitech;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 罗技 ReceiverIdentity 首版门控——只有 C548 的接收器槽位填 Bolt。
/// 2026/11/10 21:22:41 20211
/// </summary>
public class LogitechReceiverIdentityPolicyTests
{
    [Fact]
    public void L1_C548接收器槽位填Bolt()
    {
        var identity = LogitechReceiverIdentityPolicy.For(false, 0xC548);

        Assert.NotNull(identity);
        Assert.Null(identity.Value.Role);
        Assert.Equal("bolt", identity.Value.Family);
    }

    [Fact]
    public void L2_C548直连不填()
    {
        Assert.Null(LogitechReceiverIdentityPolicy.For(true, 0xC548));
    }

    [Theory]
    [InlineData(0xC52B)]
    [InlineData(0xC532)]
    [InlineData(0xC539)]
    [InlineData(0xC518)]
    [InlineData(0xC517)]
    public void L3_非C548接收器不填(int pid)
    {
        Assert.Null(LogitechReceiverIdentityPolicy.For(false, (ushort)pid));
    }

    [Fact]
    public void L4_未收录或缺失PID不填()
    {
        Assert.Null(LogitechReceiverIdentityPolicy.For(false, 0xFFFF));
        Assert.Null(LogitechReceiverIdentityPolicy.For(false, 0x0000));
    }
}
