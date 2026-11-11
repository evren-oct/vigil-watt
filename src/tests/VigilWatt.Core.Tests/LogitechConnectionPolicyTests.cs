using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Logitech;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 罗技接收器槽位连接推断门控——目录族属 2.4G 体系（Bolt/Unifying/LightSpeed/Nano）、
///         且槽位落在该族目录 MaxSlots 内的非 Direct 槽位，才允许 Inferred 2.4G。
/// 2026/11/10 22:10:00 20211
/// 2026/11/11 16:05:00 20211 门从"仅 Bolt"放开到全 2.4G 族；27MHz 与 Unknown 族仍不推断（C09/C10）。
/// 2026/11/11 16:20:00 20211 容量改为"接收器自报优先、目录兜底"（C17~C19）。
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
    [InlineData(0xC52B, 1)]
    [InlineData(0xC532, 6)]
    public void C06_Unifying接收器推断2点4G(ushort pid, byte slotIndex)
    {
        Assert.Equal(ReceiverFamily.Unifying, Catalog(pid)?.Family);
        var connection = LogitechConnectionPolicy.For(false, Catalog(pid), slotIndex, At);

        Assert.Equal(ConnectionKind.Wireless24G, connection.Value);
        Assert.Equal(TelemetrySource.Inferred, connection.Source);
    }

    [Fact]
    public void C07_Lightspeed接收器推断2点4G()
    {
        Assert.Equal(ReceiverFamily.Lightspeed, Catalog(0xC541)?.Family);

        Assert.Equal(ConnectionKind.Wireless24G,
            LogitechConnectionPolicy.For(false, Catalog(0xC541), 1, At).Value);
    }

    [Theory]
    [InlineData(0xC518, 1)]
    [InlineData(0xC537, 2)]
    public void C08_Nano接收器推断2点4G(ushort pid, byte slotIndex)
    {
        Assert.Equal(ReceiverFamily.Nano, Catalog(pid)?.Family);

        Assert.Equal(ConnectionKind.Wireless24G,
            LogitechConnectionPolicy.For(false, Catalog(pid), slotIndex, At).Value);
    }

    [Fact]
    public void C09_Legacy27Mhz接收器不推断()
    {
        // 27MHz 不是 2.4G 频段，ConnectionKind 也没有对应值，只能留 Unknown
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

    [Fact]
    public void C15_槽位超过该族目录容量不推断()
    {
        // C518 目录只有 1 槽；C52B 只有 6 槽。超出的槽位没有目录证据，不能借别族的容量
        Assert.False(LogitechConnectionPolicy.For(false, Catalog(0xC518), 2, At).HasValue);
        Assert.False(LogitechConnectionPolicy.For(false, Catalog(0xC52B), 7, At).HasValue);
    }

    [Fact]
    public void C16_各族推断都不升级为设备上报()
    {
        foreach (var pid in new ushort[] { 0xC548, 0xC52B, 0xC539, 0xC518 })
            Assert.Equal(TelemetrySource.Inferred,
                LogitechConnectionPolicy.For(false, Catalog(pid), 1, At).Source);
    }

    [Fact]
    public void C17_自报容量大于目录时按自报放开()
    {
        // C518 目录只登记 1 槽，但接收器自报 6 → 槽 2 也有容量证据，可以推断
        Assert.False(LogitechConnectionPolicy.For(false, Catalog(0xC518), 2, At).HasValue);

        var connection = LogitechConnectionPolicy.For(false, Catalog(0xC518), 2, At, 6);

        Assert.Equal(ConnectionKind.Wireless24G, connection.Value);
        Assert.Equal(TelemetrySource.Inferred, connection.Source);
    }

    [Fact]
    public void C18_自报容量小于目录时按自报收紧()
    {
        // C52B 目录 6 槽，接收器自报只有 2 → 槽 3 越界
        Assert.True(LogitechConnectionPolicy.For(false, Catalog(0xC52B), 3, At).HasValue);
        Assert.False(LogitechConnectionPolicy.For(false, Catalog(0xC52B), 3, At, 2).HasValue);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(0xFF)]
    public void C19_自报容量越界时退回目录值(int reported)
    {
        // 0 / 7 / 255 都不是合法容量：不能因为读到一个怪值就收紧或放开
        Assert.True(LogitechConnectionPolicy.For(false, Catalog(0xC52B), 6, At, reported).HasValue);
        Assert.False(LogitechConnectionPolicy.For(false, Catalog(0xC52B), 7, At, reported).HasValue);
    }
}
