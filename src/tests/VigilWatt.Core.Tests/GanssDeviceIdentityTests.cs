using VigilWatt.Core.Plugins.Ganss;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： GanssDeviceIdentity —— 0x05AC:024F 一族设备的身份归一规则。
/// 2026/10/26
///
/// 真机样本（2026-10-26，<c>scripts-reverse/probe_pair_identity.py</c> 实测）：
/// 狼蛛 F108 Pro 的 6 条接口里只有 <c>mi_03</c>（usage 0xFF60/0x0061、33 字节）被认领，
/// **序列号全为空**（设备不报 SN）→ 只能归到型号粒度。
/// 换口前后路径里的 <c>7&amp;xxxx</c> 段会变，路径哈希随之变 →
/// 归一前同一只设备会变成两个 Id（历史断成两段、列表冒两行）。
/// </summary>
public sealed class GanssDeviceIdentityTests
{
    private const string Plugin = "ganss";

    /// <summary>本机实测的认领接口路径（mi_03 + 这次的实例号）。</summary>
    private const string Mi03Path =
        @"\\?\hid#vid_05ac&pid_024f&mi_03#7&ccb4232&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";

    /// <summary>换 USB 口后同一只设备的路径（实例号段不同）。</summary>
    private const string Mi03PathAfterReplug =
        @"\\?\hid#vid_05ac&pid_024f&mi_03#7&1a76e371&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";

    /// <summary>不是被认领的那条接口（mi_04，65 字节）。</summary>
    private const string Mi04Path =
        @"\\?\hid#vid_05ac&pid_024f&mi_04#7&30a27ff4&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";

    [Fact]
    public void CapturedInterfaceGetsStableModelId()
    {
        var id = GanssDeviceIdentity.IdForPath(Mi03Path, Plugin);
        Assert.Equal("ganss:aula-f108pro", id);
    }

    /// <summary>
    /// 换口前后必须得到**同一个** Id——这正是归一的目的：
    /// 换口后不该多出一个设备条目，历史也不该断成两段。
    /// </summary>
    [Fact]
    public void RepluggingKeepsSameId()
    {
        var before = GanssDeviceIdentity.IdForPath(Mi03Path, Plugin);
        var after = GanssDeviceIdentity.IdForPath(Mi03PathAfterReplug, Plugin);
        Assert.Equal(before, after);
        Assert.NotNull(after);
    }

    /// <summary>非认领接口（mi_04）不归一，回退路径哈希。</summary>
    [Fact]
    public void UncapturedInterfaceIsNotNormalized()
    {
        Assert.Null(GanssDeviceIdentity.IdForPath(Mi04Path, Plugin));
    }

    /// <summary>
    /// 归一 Id 必须是"插件名:型号"两段，不能带路径——带了路径就等于没归一。
    /// </summary>
    [Fact]
    public void NormalizedIdCarriesNoPathHash()
    {
        var id = GanssDeviceIdentity.IdForPath(Mi03Path, Plugin)!;
        Assert.DoesNotContain("ccb4232", id, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"\\?\hid", id, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>产品名判据：F108 与 GS3104T 是两个型号，不能混。</summary>
    [Fact]
    public void ProductNameDistinguishesModels()
    {
        Assert.Equal("ganss:aula-f108pro",
            GanssDeviceIdentity.ModelIdByProduct("F108Pro Dongle", Plugin));
        Assert.Equal("ganss:ganss-gs3104t-pro",
            GanssDeviceIdentity.ModelIdByProduct("GANSS GS3104T-Pro", Plugin));
        Assert.Null(GanssDeviceIdentity.ModelIdByProduct("Unknown Device", Plugin));
    }
}
