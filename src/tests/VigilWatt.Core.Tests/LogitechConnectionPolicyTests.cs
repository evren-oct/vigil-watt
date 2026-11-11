using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Logitech;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 罗技接收器槽位连接推断门控——只有目录确认为 Bolt（当前仅 C548）的有效非 Direct 槽位才允许 Inferred 2.4G。
/// 2026/11/10 22:10:00 20211
/// </summary>
public class LogitechConnectionPolicyTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    private const ushort BoltC548 = 0xC548;

    private static ReceiverInfo? Catalog(ushort pid) => HidppCatalog.FindReceiver(pid);

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    public void C01_C548_Bolt有效槽位推断2点4G(byte slotIndex)
    {
        var connection = LogitechConnectionPolicy.For(false, Catalog(BoltC548), slotIndex, At);

        Assert.True(connection.HasValue);
        Assert.Equal(ConnectionKind.Wireless24G, connection.Value);
        Assert.Equal(TelemetrySource.Inferred, connection.Source);
        Assert.Equal(At, connection.ObservedAt);
    }

    [Fact]
    public void C02_Direct设备不推断()
    {
        var connection = LogitechConnectionPolicy.For(true, Catalog(BoltC548), 1, At);

        Assert.False(connection.HasValue);
    }

    [Fact]
    public void C03_零槽位不推断()
    {
        Assert.False(LogitechConnectionPolicy.For(false, Catalog(BoltC548), 0, At).HasValue);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(0xFF)]
    public void C04_越界槽位不推断(byte slotIndex)
    {
        Assert.False(LogitechConnectionPolicy.For(false, Catalog(BoltC548), slotIndex, At).HasValue);
    }

    [Fact]
    public void C05_目录记录槽位容量非正不推断()
    {
        var broken = new ReceiverInfo(BoltC548, ReceiverFamily.Bolt, 0, HidppReportPreference.PreferLong);

        Assert.False(LogitechConnectionPolicy.For(false, broken, 1, At).HasValue);
    }

    [Theory]
    [InlineData(0xC52B)]
    [InlineData(0xC532)]
    public void C06_Unifying接收器不推断(ushort pid)
    {
        Assert.Equal(ReceiverFamily.Unifying, Catalog(pid)?.Family);
        Assert.False(LogitechConnectionPolicy.For(false, Catalog(pid), 1, At).HasValue);
    }

    [Fact]
    public void C07_Lightspeed接收器不推断()
    {
        Assert.Equal(ReceiverFamily.Lightspeed, Catalog(0xC541)?.Family);
        Assert.False(LogitechConnectionPolicy.For(false, Catalog(0xC541), 1, At).HasValue);
    }

    [Fact]
    public void C08_Nano接收器不推断()
    {
        Assert.Equal(ReceiverFamily.Nano, Catalog(0xC518)?.Family);
        Assert.False(LogitechConnectionPolicy.For(false, Catalog(0xC518), 1, At).HasValue);
    }

    [Fact]
    public void C09_Legacy27Mhz接收器不推断()
    {
        Assert.Equal(ReceiverFamily.Legacy27Mhz, Catalog(0xC517)?.Family);
        Assert.False(LogitechConnectionPolicy.For(false, Catalog(0xC517), 1, At).HasValue);
    }

    [Fact]
    public void C10_目录内Unknown族不推断()
    {
        var unknown = new ReceiverInfo(0xC548, ReceiverFamily.Unknown, 6, HidppReportPreference.PreferLong);

        Assert.False(LogitechConnectionPolicy.For(false, unknown, 1, At).HasValue);
    }

    [Fact]
    public void C11_未收录PID不推断()
    {
        Assert.Null(Catalog(0xC999));
        Assert.False(LogitechConnectionPolicy.For(false, Catalog(0xC999), 1, At).HasValue);
    }

    [Fact]
    public void C12_PID缺失不推断()
    {
        Assert.Null(Catalog(0));
        Assert.False(LogitechConnectionPolicy.For(false, Catalog(0), 1, At).HasValue);
    }

    [Fact]
    public void C13_推断值不覆盖设备上报()
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
            Connection = LogitechConnectionPolicy.For(false, Catalog(BoltC548), 1, At + TimeSpan.FromSeconds(1)),
        };

        VigilWatt.Core.Devices.DeviceStatusMerger.Apply(stored, incoming);

        Assert.Equal(ConnectionKind.Wired, stored.Connection.Value);
        Assert.Equal(TelemetrySource.DeviceReported, stored.Connection.Source);
    }

    [Fact]
    public void C14_Direct路径不推断有线()
    {
        // Direct 只表示"直连接口"，链路证据不足时必须是无值 Unknown，不允许落成 Wired
        var connection = LogitechConnectionPolicy.For(true, Catalog(BoltC548), 0xFF, At);

        Assert.False(connection.HasValue);
        Assert.Equal(TelemetrySource.Unknown, connection.Source);
    }
}
