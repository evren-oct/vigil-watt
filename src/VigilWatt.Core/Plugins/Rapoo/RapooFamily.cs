using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Rapoo;

/// <summary>接口形态指纹——认领判据的输入，纯值、可单测。</summary>
internal readonly record struct RapooInterfaceShape(
    ushort UsagePage, ushort Usage, ushort InputLength, ushort OutputLength)
{
    public static RapooInterfaceShape Of(HidDeviceInfo info)
        => new(info.UsagePage, info.Usage, info.InputReportLength, info.OutputReportLength);
}

/// <summary>链路提示：由协议族自己报告断链/恢复，插件转成离线候选。</summary>
internal enum RapooLinkHint
{
    /// <summary>本族不提供链路信息。</summary>
    None,
    /// <summary>设备自报断链（如状态帧 <c>[18].bit7=0</c>）。</summary>
    Down,
    /// <summary>设备自报链路正常。</summary>
    Up,
}

/// <summary>一条有效读数。</summary>
internal readonly record struct RapooReading(int? Percent, ChargingState Charging);

/// <summary>
/// 被动帧处理结果：读数与链路提示**相互独立**——电量越界的休眠瞬态帧仍然带可信的链路位，
/// 丢掉读数但必须保留链路提示（见 docs/hardware/rapoo.md §3）。
/// </summary>
internal readonly record struct RapooPassiveResult(RapooReading? Reading, RapooLinkHint Link);

/// <summary>
/// 解析所需的设备身份。<see cref="BatteryPluginBase.TrackedDevice"/> 是 protected 嵌套类型，
/// 协议族拿不到，只能摊成这几个字段：<see cref="Id"/> 做日志去重键、<see cref="DevicePath"/> 做去抖状态键、
/// <see cref="ProductId"/> 用来区分同一族里的子变体（如 V700diy 的键盘布局）。
/// </summary>
internal readonly record struct RapooDeviceRef(string Id, string DevicePath, ushort ProductId);

/// <summary>
/// 功 能： 一套雷柏协议的实现单元——认领判据、首次探测、被动帧解析、轮询各管各的。
/// 2026/11/01 22:20:00 xliu
/// 加一套协议 = 加一个子类 + 在 <see cref="RapooFamilies"/> 注册一行，插件主体不再改；族清单见 docs/hardware/rapoo-catalog.md §2。
/// </summary>
internal abstract class RapooFamily
{
    /// <summary>
    /// 本族负责的协议族名（目录 <c>protocol</c> 字段的取值）。**可以是多个**：
    /// `VT_nrf54L` / `LM20RT` / `VT_CH584` 三个官方 driver 的帧布局完全一样，由一个族统一吃下。
    /// </summary>
    public abstract string[] Protocols { get; }

    /// <summary>本族设备的类型（决定图标）。一族内类型必须一致，混装请拆成两族。</summary>
    public abstract DeviceKind Kind { get; }

    /// <summary>认领判据：这个接口形态是不是本族的通道。<b>必须校验形状</b>，否则同型号的旁路接口会被重复认领。</summary>
    public abstract bool MatchesShape(RapooInterfaceShape shape);

    /// <summary>首次认领时被动听一个窗口；听不到返回 null（设备可能在睡，不是失败）。</summary>
    public virtual Task<(int? Percent, ChargingState Charging)?> ProbeAsync(
        HidDeviceHandle handle, TimeSpan window)
        => Task.FromResult<(int? Percent, ChargingState Charging)?>(null);

    /// <summary>解析一帧被动输入；不是本族的帧返回 null。</summary>
    public virtual RapooPassiveResult? ParsePassive(RapooDeviceRef dev, byte[] input) => null;

    /// <summary>主动轮询；纯被动族恒返回 null（这是协议形态，不是失联）。</summary>
    public virtual (int? Percent, ChargingState Charging)? Poll(RapooDeviceRef dev) => null;

    /// <summary>设备移除后清理按设备保存的状态（去抖表等），防随插拔累积。</summary>
    public virtual void Prune(IReadOnlyList<HidDeviceInfo> present) { }

    /// <summary>插件被禁用/卸载时清空全部状态。</summary>
    public virtual void Reset() { }

    /// <summary>
    /// 被动听一个窗口，首个解析成功的帧立即返回。**不发任何字节**。
    /// 各族的探测只是"用不同的解析器看同一批报文"，所以循环只有这一份。
    /// </summary>
    protected static async Task<(int? Percent, ChargingState Charging)?> ListenAsync(
        HidDeviceHandle handle, TimeSpan window,
        Func<byte[], (int Percent, ChargingState Charging)?> parse)
    {
        var deadline = DateTime.UtcNow + window;
        while (DateTime.UtcNow < deadline)
        {
            var remaining = deadline - DateTime.UtcNow;
            var slice = remaining < TimeSpan.FromMilliseconds(400) ? remaining : TimeSpan.FromMilliseconds(400);
            if (slice <= TimeSpan.Zero) break;

            var input = await handle.ReadInputAsync(slice).ConfigureAwait(false);
            if (input == null) continue;
            if (Log.IsEnabled(LogLevel.Debug)) Log.Debug($"雷柏 收到报文: {Log.Hex(input)}");

            if (parse(input) is { } r) return r;
        }
        return null;
    }
}
