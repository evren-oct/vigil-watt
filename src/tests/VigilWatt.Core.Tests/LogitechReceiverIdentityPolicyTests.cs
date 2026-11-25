using System.Reflection;
using System.Text.Json;
using VigilWatt.Core.Plugins.Logitech;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 罗技 ReceiverIdentity 策略——目录登记过的接收器族全量填身份，未收录 PID 与 Direct 路径保持 null。
/// 2026/11/10 21:22:41 20211
/// 身份与链路是两件事：这里只管"这颗 PID 属于哪一族"，`Connection` 由 LogitechConnectionPolicy 单独门控。
/// </summary>
public class LogitechReceiverIdentityPolicyTests
{
    private static ReceiverInfo? Catalog(ushort pid) => HidppCatalog.FindReceiver(pid);

    [Fact]
    public void L1_C548接收器槽位填Bolt()
    {
        var identity = LogitechReceiverIdentityPolicy.For(false, Catalog(0xC548));

        Assert.NotNull(identity);
        Assert.Null(identity.Value.Role);
        Assert.Equal("bolt", identity.Value.Family);
        Assert.Equal("Bolt", identity.Value.DisplayName);
        Assert.Equal("bolt", identity.Value.IconKey);
    }

    [Fact]
    public void L2_C548直连不填()
    {
        Assert.Null(LogitechReceiverIdentityPolicy.For(true, Catalog(0xC548)));
    }

    [Theory]
    [InlineData(0xC52B, "unifying", "Unifying", "unifying")]
    [InlineData(0xC532, "unifying", "Unifying", "unifying")]
    [InlineData(0xC539, "lightspeed", "LIGHTSPEED", "lightning")]
    [InlineData(0xC53F, "lightspeed", "LIGHTSPEED", "lightning")]
    [InlineData(0xC518, "nano", "Nano", "nano")]
    [InlineData(0xC537, "nano", "Nano", "nano")]
    [InlineData(0xC517, "legacy27mhz", "27MHz", "wireless")]
    public void L3_目录登记的接收器族都填身份(int pid, string family, string display, string icon)
    {
        var identity = LogitechReceiverIdentityPolicy.For(false, Catalog((ushort)pid));

        Assert.NotNull(identity);
        Assert.Null(identity.Value.Role);
        Assert.Equal(family, identity.Value.Family);
        Assert.Equal(display, identity.Value.DisplayName);
        Assert.Equal(icon, identity.Value.IconKey);
    }

    [Fact]
    public void L4_未收录或缺失PID不填()
    {
        Assert.Null(LogitechReceiverIdentityPolicy.For(false, Catalog(0xFFFF)));
        Assert.Null(LogitechReceiverIdentityPolicy.For(false, Catalog(0x0000)));
    }

    /// <summary>图标 key 写错不会报错、只会静默退化成信号弧，只能靠这条测试挡。</summary>
    [Fact]
    public void L5_目录图标key是已知语义且一族一图()
    {
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "bolt", "unifying", "nano", "lightning", "multi", "wireless" };

        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("VigilWatt.Core.Plugins.Logitech.logitech-hidpp.json")!;
        using var doc = JsonDocument.Parse(stream);

        var byFamily = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var receiver in doc.RootElement.GetProperty("receivers").EnumerateArray())
        {
            var family = receiver.GetProperty("family").GetString()!;
            var icon = receiver.GetProperty("familyIcon").GetString()!;
            Assert.Contains(icon, known);
            if (byFamily.TryGetValue(family, out var existing))
                Assert.Equal(existing, icon);
            else
                byFamily[family] = icon;
        }
    }
}
