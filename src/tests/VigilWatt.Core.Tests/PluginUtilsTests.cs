using VigilWatt.Core.Plugins;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： PluginUtils.StableHash + DeviceClaimRegistry —— 都是零依赖纯逻辑，之前没有覆盖（全检报告行动项 5）。
/// 2026/10/10 17:44:33 xliu
/// StableHash 的稳定性是设备 Id 的前提：重启/重扫后同一条路径必须映射到同一个 Id， 否则历史记录会认不出设备（每条记录都变成新设备）。
/// </summary>
public class PluginUtilsTests
{
    [Fact]
    public void P1_同一输入永远得到同一结果()
    {
        const string path = @"\\?\hid#vid_24ae&pid_1412#7&1a2b3c4d&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";
        Assert.Equal(PluginUtils.StableHash(path), PluginUtils.StableHash(path));
    }

    [Fact]
    public void P2_不同输入得到不同结果()
    {
        Assert.NotEqual(PluginUtils.StableHash("hid#a"), PluginUtils.StableHash("hid#b"));
    }

    [Fact]
    public void P3_结果固定为16位十六进制_不受平台默认格式影响()
    {
        // 用 "X16" 而不是 "x" 或依赖文化：Id 会进 history.db，格式漂移等于历史对不上
        var hash = PluginUtils.StableHash("x");
        Assert.Equal(16, hash.Length);
        Assert.Matches("^[0-9A-F]{16}$", hash);
    }

    [Fact]
    public void P4_空串也有确定结果_不抛异常()
    {
        var hash = PluginUtils.StableHash("");
        Assert.Equal(16, hash.Length);
        Assert.Equal(hash, PluginUtils.StableHash(""));
    }

    [Fact]
    public void P5_大小写敏感_路径大小写不同视为不同设备()
    {
        // 设备路径由系统给出，大小写固定；这里只锁"实现不会自作主张归一化"
        Assert.NotEqual(PluginUtils.StableHash("HID#A"), PluginUtils.StableHash("hid#a"));
    }

    // 本机实测的三个端口实例（Bolt C548 的 HID++ 长接口）：换口后 ID 漂移，列表里多出一台同名设备
    private const string Guid = "#{4d1e55b2-f16f-11cf-88cb-001111000030}";
    private const string PortA = @"\\?\HID#VID_046D&PID_C548&MI_02&Col02#7&15dc4901&0&0001" + Guid;
    private const string PortB = @"\\?\HID#VID_046D&PID_C548&MI_02&Col02#7&393736c6&0&0001" + Guid;
    private const string PortC = @"\\?\HID#VID_046D&PID_C548&MI_02&Col02#7&8918bd5&0&0001" + Guid;

    [Fact]
    public void P6_同一接口换USB口_端口无关键相同()
    {
        // 前提：整条路径确实随端口变（这正是旧实现的 bug）
        Assert.NotEqual(PluginUtils.StableHash(PortA), PluginUtils.StableHash(PortB));

        Assert.Equal(PluginUtils.StableDeviceKey(PortA), PluginUtils.StableDeviceKey(PortB));
        Assert.Equal(PluginUtils.StableDeviceKey(PortA), PluginUtils.StableDeviceKey(PortC));
        Assert.Equal("18AADF9F12EA1A68", PluginUtils.StableDeviceHash(PortB));
    }

    [Fact]
    public void P7_同一接收器的不同集合_仍是不同设备()
    {
        // 集合序号（0&0000 / 0&0001）稳定且必须保留，否则同一个接收器的键盘和鼠标会被并成一台
        var col01 = @"\\?\HID#VID_046D&PID_C548&MI_02&Col01#7&15dc4901&0&0000" + Guid;
        var col02 = @"\\?\HID#VID_046D&PID_C548&MI_02&Col02#7&15dc4901&0&0001" + Guid;
        Assert.NotEqual(PluginUtils.StableDeviceHash(col01), PluginUtils.StableDeviceHash(col02));
        Assert.Contains("#0&0000", PluginUtils.StableDeviceKey(col01));
    }

    [Fact]
    public void P8_不同型号仍是不同设备()
    {
        var other = @"\\?\HID#VID_24AE&PID_1412&MI_02&Col02#7&15dc4901&0&0001" + Guid;
        Assert.NotEqual(PluginUtils.StableDeviceHash(PortA), PluginUtils.StableDeviceHash(other));
    }

    [Fact]
    public void P9_路径格式不认识_原样返回不抛异常()
    {
        // 退回旧行为（哈希整条路径）也比抛异常好：认不出的路径至少还能用
        Assert.Equal(@"\\?\hid#a", PluginUtils.StableDeviceKey(@"\\?\hid#a"));
        Assert.Equal(@"\\?\HID#X#7&a#{}", PluginUtils.StableDeviceKey(@"\\?\HID#X#7&a#{}"));
        Assert.Equal("", PluginUtils.StableDeviceKey(""));
    }
}

/// <summary>接口认领注册表：同一接口同一时刻只能归一个插件（抢句柄/抢会话的防线）。</summary>
public class DeviceClaimRegistryTests
{
    private const string PathA = @"\\?\hid#a";
    private const string PathB = @"\\?\hid#b";

    [Fact]
    public void R1_先到先得_后到的插件被拒()
    {
        var reg = new DeviceClaimRegistry();
        Assert.True(reg.TryClaim(PathA, "Rapoo"));
        Assert.False(reg.TryClaim(PathA, "Mchose"));
        Assert.True(reg.IsClaimedBy(PathA, "Rapoo"));
        Assert.False(reg.IsClaimedBy(PathA, "Mchose"));
    }

    [Fact]
    public void R2_同一插件重复认领是幂等的()
    {
        var reg = new DeviceClaimRegistry();
        Assert.True(reg.TryClaim(PathA, "Rapoo"));
        Assert.True(reg.TryClaim(PathA, "Rapoo"));
    }

    /// <summary>CR-145：同名双插件（迈从键鼠 + A5）必须能各自互斥，不能因为共用设置键就互相踩。</summary>
    [Fact]
    public void R2b_同名不同认领身份互不干扰()
    {
        var reg = new DeviceClaimRegistry();
        var main = new ClaimOwner("Mchose", "Mchose");
        var a5 = new ClaimOwner("Mchose#A5", "Mchose");

        Assert.True(reg.TryClaim(PathA, main));
        Assert.False(reg.TryClaim(PathA, a5));        // A5 抢不走已归主插件的接口
        Assert.True(reg.IsClaimedBy(PathA, main));
        Assert.False(reg.IsClaimedBy(PathA, a5));

        reg.Release(PathA, a5);                        // A5 释放不能把主插件的认领删掉
        Assert.True(reg.IsClaimedBy(PathA, main));

        reg.ReleaseAll(a5.Id);                         // 卸载 A5 也不能清主插件
        Assert.True(reg.IsClaimedBy(PathA, main));

        Assert.Equal("Mchose", reg.OwnerOf(PathA));    // 设置页仍按插件名查开关
    }

    [Fact]
    public void R3_不同接口互不影响()
    {
        var reg = new DeviceClaimRegistry();
        Assert.True(reg.TryClaim(PathA, "Rapoo"));
        Assert.True(reg.TryClaim(PathB, "Mchose"));
    }

    [Fact]
    public void R4_非持有者释放无效_防止误放别人的接口()
    {
        var reg = new DeviceClaimRegistry();
        reg.TryClaim(PathA, "Rapoo");

        reg.Release(PathA, "Mchose");   // 不是持有者，应被忽略
        Assert.True(reg.IsClaimedBy(PathA, "Rapoo"));
    }

    [Fact]
    public void R5_持有者释放后可被他人认领()
    {
        var reg = new DeviceClaimRegistry();
        reg.TryClaim(PathA, "Rapoo");
        reg.Release(PathA, "Rapoo");

        Assert.False(reg.IsClaimedBy(PathA, "Rapoo"));
        Assert.True(reg.TryClaim(PathA, "Mchose"));
    }

    [Fact]
    public void R6_ReleaseAll只放自己的()
    {
        var reg = new DeviceClaimRegistry();
        reg.TryClaim(PathA, "Rapoo");
        reg.TryClaim(PathB, "Mchose");

        reg.ReleaseAll("Rapoo");
        Assert.False(reg.IsClaimedBy(PathA, "Rapoo"));
        Assert.True(reg.IsClaimedBy(PathB, "Mchose"));
    }
}
