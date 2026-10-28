using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 守住"扫描器真的读了序列号"这条不变量——它一旦断掉，组合识别就整体失效。
/// 2026/10/26
///
/// 背景（1.3.0 官方版的真实缺陷）：
/// 官方版引入了 <c>VgnDevicePairing</c>（靠序列号把"无线接收器 + 插线"配成一只设备），
/// 配套的 <c>DeviceGroupKey</c> 也有 ContainerId 分组。但：
/// 1) <c>HidDeviceScanner</c> 构造 <c>HidDeviceInfo</c> 时**只填了 Product 与 Manufacturer，
///    根本没读 <c>HidD_GetSerialNumberString</c>** → <c>SerialNumber</c> 恒为空串
///    → <c>VgnDevicePairing.SameSerial</c> 恒为 false → 无线那条永远不被跳过
///    → 同一只 F2 在设备列表里两行（放电 100% / 充电 95%），这正是用户报的"同设备多个名称"。
/// 2) 实测本机 HID 设备**全部读不到 ContainerId**（见 scripts-reverse/probe_pair_identity.py），
///    所以采集页那条 ContainerId 分组路在本机也走不通——SN 是唯一可用判据。
///
/// 本测试用**源码级断言**锁住这条链路：反射确认属性存在，再确认扫描器源码里
/// 真的有 <c>HidD_GetSerialNumberString</c> 的读取调用。
/// 之所以不直接跑扫描器：它要开真实 HID 句柄，CI/单测环境没有硬件，测了也不稳定。
/// </summary>
public sealed class SerialNumberWiringTests
{
    /// <summary>HidDeviceInfo 必须有 SerialNumber 属性（1.3.0 里由 Serial 改名而来）。</summary>
    [Fact]
    public void HidDeviceInfoExposesSerialNumber()
    {
        var prop = typeof(VigilWatt.Core.Hid.HidDeviceInfo)
            .GetProperty("SerialNumber", BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(prop);
        Assert.Equal(typeof(string), prop!.PropertyType);
    }

    /// <summary>
    /// 扫描器源码里必须出现 <c>HidD_GetSerialNumberString</c> 的读取调用。
    /// 没有这一句，<c>SerialNumber</c> 就永远是空串，配对永远不成立。
    /// </summary>
    [Fact]
    public void ScannerReadsSerialNumberString()
    {
        var source = ReadRepoFile("src/VigilWatt.Core/Hid/HidDeviceScanner.cs");
        Assert.Contains("HidD_GetSerialNumberString", source, StringComparison.Ordinal);
        // 还要确认读出来的东西真的赋给了 SerialNumber，而不是读了个别的地方
        Assert.Contains("SerialNumber =", source, StringComparison.Ordinal);
    }

    /// <summary>原生层必须声明这个 P/Invoke，否则上面那句调不到。</summary>
    [Fact]
    public void NativeLayerDeclaresSerialNumberPInvoke()
    {
        var source = ReadRepoFile("src/VigilWatt.Core/Hid/NativeHid.cs");
        Assert.Contains("HidD_GetSerialNumberString", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// 采集页的 <c>CaptureDeviceExtensions</c> 也从 HidDeviceInfo 取 SN，
    /// 那条路不能因为改名而断掉。
    /// </summary>
    [Fact]
    public void CaptureLayerStillReadsSerialNumber()
    {
        var source = ReadRepoFile("src/VigilWatt.Core/Capture/Hid/CaptureDeviceExtensions.cs");
        Assert.Contains("SerialNumber", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// 从编译期记录的本文件位置（<c>[CallerFilePath]</c>）往上找仓库根，读取一个源文件。
    /// 用编译期路径而不是运行期 <c>AppContext.BaseDirectory</c>：后者在打包测试环境里
    /// 指向 bin 目录，上溯层级随配置变化，容易找错。
    /// </summary>
    private static string ReadRepoFile(string relativePath, [CallerFilePath] string thisFile = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(thisFile) ?? string.Empty);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = dir.Parent;
        }
        return string.Empty;   // 找不到源文件时下面断言会失败，信息更明确
    }
}
