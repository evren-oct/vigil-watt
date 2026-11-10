using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Logitech;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 罗技接收器槽位连接推断门控——只有有效非 Direct 槽位才允许 Inferred 2.4G。
/// 2026/11/10 22:10:00 20211
/// </summary>
public class LogitechConnectionPolicyTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void C1_有效接收器槽位推断2点4G()
    {
        var connection = LogitechConnectionPolicy.For(false, 1, 6, At);

        Assert.True(connection.HasValue);
        Assert.Equal(ConnectionKind.Wireless24G, connection.Value);
        Assert.Equal(TelemetrySource.Inferred, connection.Source);
        Assert.Equal(At, connection.ObservedAt);
    }

    [Fact]
    public void C2_最大槽位仍是有效槽位()
    {
        var connection = LogitechConnectionPolicy.For(false, 6, 6, At);

        Assert.True(connection.HasValue);
        Assert.Equal(ConnectionKind.Wireless24G, connection.Value);
        Assert.Equal(TelemetrySource.Inferred, connection.Source);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(0xFF)]
    public void C3_零槽位越界槽位和Direct标记不推断(byte slotIndex)
    {
        Assert.False(LogitechConnectionPolicy.For(false, slotIndex, 6, At).HasValue);
    }

    [Fact]
    public void C4_Direct设备不推断()
    {
        Assert.False(LogitechConnectionPolicy.For(true, 1, 6, At).HasValue);
        Assert.False(LogitechConnectionPolicy.For(true, 0xFF, 6, At).HasValue);
    }

    [Fact]
    public void C5_没有槽位容量时不推断()
    {
        Assert.False(LogitechConnectionPolicy.For(false, 1, 0, At).HasValue);
    }

    [Fact]
    public void C6_推断值不覆盖设备上报()
    {
        var stored = new DeviceStatus
        {
            Id = "logitech:test:1",
            Name = "test",
            Source = DeviceSource.Hid24G,
            Connection = TelemetryValue<ConnectionKind>.Reported(ConnectionKind.Wired, At),
        };
        var incoming = new DeviceStatus
        {
            Id = stored.Id,
            Name = stored.Name,
            Source = stored.Source,
            Connection = LogitechConnectionPolicy.For(false, 1, 6, At + TimeSpan.FromSeconds(1)),
        };

        VigilWatt.Core.Devices.DeviceStatusMerger.Apply(stored, incoming);

        Assert.Equal(ConnectionKind.Wired, stored.Connection.Value);
        Assert.Equal(TelemetrySource.DeviceReported, stored.Connection.Source);
    }
}
