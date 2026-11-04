using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Protocol;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 雷柏 12 套协议的 JSON 规格回归——真机帧 + 官方解析器反推帧，全部走解释器。
/// 2026/11/01 23:20:00 xliu
/// 偏移与判据都在 specs/rapoo.protocols.json；本文件是"改 JSON 必须跑绿"的闸门。
/// </summary>
public sealed class RapooSpecTests
{
    private const string ProtocolsResource = "VigilWatt.Core.Plugins.Rapoo.specs.rapoo.protocols.json";
    private const string DevicesResource = "VigilWatt.Core.Plugins.Rapoo.rapoo-catalog.json";

    private static readonly ProtocolEngine Engine = ProtocolEngine.Load(ProtocolsResource, DevicesResource);

    /// <summary>真机（21:37:12）：94% / 充电标志 1 / RF=0xC8（bit7=1，RSSI=72）。</summary>
    private static readonly byte[] NormalFrame =
        Convert.FromHexString("071004800C800C015E000301001246010202C8");

    /// <summary>真机（21:38:38）：鼠标断电瞬间，RF=0x7F（bit7=0）——链路断开标志。</summary>
    private static readonly byte[] LinkDownFrame =
        Convert.FromHexString("071004800C800C015E0003010012460102027F");

    /// <summary>真机（21:39:06）：重连后充电标志 1→0、电量 94→95、RF=0xD2。</summary>
    private static readonly byte[] ReconnectFrame =
        Convert.FromHexString("071004800C800C005F000301001246010202D2");

    /// <summary>真机（VT2S Air Max 有线）：link=有线(2)、充电标志=2、电量 100、RF=0x00。</summary>
    private static readonly byte[] WiredFrame =
        Convert.FromHexString("0742021A041A04026400030100134B10000100");

    private static FrameSpec Status => Engine.SpecOf("status-report7")!.Passive!;

    private static byte[] WithByte(byte[] frame, int index, byte value)
    {
        var copy = (byte[])frame.Clone();
        copy[index] = value;
        return copy;
    }

    private static DeviceRef Dev(ushort pid = 0x1412, string path = @"\\?\hid#vid_24ae&pid_1412#test")
        => new("rapoo:test", path, 0x24AE, pid);

    // ---------- 状态帧：真机 ----------

    [Fact]
    public void R1_真机正常帧解析出电量与链路在位()
    {
        var r = FrameReader.Read(Status, NormalFrame);
        Assert.NotNull(r);
        Assert.Equal(94, r!.Value.Percent);
        Assert.Equal(ChargingState.Discharging, r.Value.Charging);
        Assert.Equal(LinkState.Up, r.Value.Link);
        Assert.Equal(LinkMode.Wireless2G, r.Value.Mode);
    }

    [Fact]
    public void R2_回归_真机断链帧必须被识别为链路断开()
    {
        var r = FrameReader.Read(Status, LinkDownFrame);
        Assert.Equal(LinkState.Down, r!.Value.Link);
        Assert.Equal(94, r.Value.Percent);      // 断链帧上的电量仍然可读
    }

    [Fact]
    public void R3_真机重连帧撤销断链()
    {
        var r = FrameReader.Read(Status, ReconnectFrame);
        Assert.Equal(LinkState.Up, r!.Value.Link);
        Assert.Equal(95, r.Value.Percent);
    }

    [Fact]
    public void R4_非07帧不算状态帧()
        => Assert.Null(FrameReader.Read(Status, WithByte(NormalFrame, 0, 0x04)));

    [Fact]
    public void R5_长度不足不算状态帧()
    {
        var shortFrame = new byte[18];
        shortFrame[0] = 0x07;
        Assert.Null(FrameReader.Read(Status, shortFrame));
    }

    [Fact]
    public void R6_兼容带reportID前缀的布局()
    {
        var prefixed = new byte[20];
        NormalFrame.CopyTo(prefixed, 1);
        Assert.Equal(94, FrameReader.Read(Status, prefixed)!.Value.Percent);
    }

    [Fact]
    public void R7_电量归零判为无效但链路位仍可信()
    {
        var r = FrameReader.Read(Status, WithByte(LinkDownFrame, 8, 0x00));
        Assert.Null(r!.Value.Percent);
        Assert.Equal(LinkState.Down, r.Value.Link);
    }

    [Fact]
    public void R8_电量越界判为无效()
        => Assert.Null(FrameReader.Read(Status, WithByte(NormalFrame, 8, 101))!.Value.Percent);

    [Theory]
    [InlineData(0x00, 2)]   // bit7=0 → 断链（LinkState.Down）
    [InlineData(0x7F, 2)]   // 真机观测到的断链哨兵
    [InlineData(0x80, 1)]   // bit7=1，RSSI=0（LinkState.Up）
    [InlineData(0xC8, 1)]   // bit7=1，RSSI=72
    [InlineData(0xFF, 1)]   // bit7=1，RSSI=127
    public void R9_链路位只看bit7(byte rfByte, int expected)
        => Assert.Equal((LinkState)expected, FrameReader.Read(Status, WithByte(NormalFrame, 18, rfByte))!.Value.Link);

    [Fact]
    public void R10_有线帧解析出有线模式与满电()
    {
        var r = FrameReader.Read(Status, WiredFrame);
        Assert.Equal(LinkMode.Wired, r!.Value.Mode);
        Assert.Equal(100, r.Value.Percent);
        // 满电一律判「充满」：设备满电后放底座上 [7] 会反复横跳（0/1/2）
        Assert.Equal(ChargingState.Full, r.Value.Charging);
    }

    [Fact]
    public void R11_有线模式下bit7恒0不算断链()
    {
        // 有线时没有 RF 链路，[18]=0x00 是正常语义；判成断链会让充电图标无限闪烁。
        var r = FrameReader.Read(Status, WiredFrame);
        Assert.Equal(LinkState.Unknown, r!.Value.Link);
    }

    [Fact]
    public void R12_2_4G模式下bit7为0仍算断链()
    {
        var r = FrameReader.Read(Status, LinkDownFrame);
        Assert.Equal(LinkMode.Wireless2G, r!.Value.Mode);
        Assert.Equal(LinkState.Down, r.Value.Link);
    }

    [Theory]
    [InlineData(0, 1)]   // 2.4G（LinkMode.Wireless2G）
    [InlineData(2, 2)]   // 有线（LinkMode.Wired）
    [InlineData(1, 0)]   // 未知
    [InlineData(3, 0)]   // 未知
    public void R13_连接方式由低4位决定(int linkRaw, int expected)
        => Assert.Equal((LinkMode)expected,
            FrameReader.Read(Status, WithByte(NormalFrame, 1, (byte)(linkRaw | 0x10)))!.Value.Mode);

    [Fact]
    public void R14_有线帧电量越界时也不当断链()
    {
        var r = FrameReader.Read(Status, WithByte(WiredFrame, 8, 0x00));
        Assert.Null(r!.Value.Percent);
        Assert.Equal(LinkState.Unknown, r.Value.Link);
    }

    [Fact]
    public void R19_满电时一律判充满不因充电标志抖()
    {
        foreach (var flag in new byte[] { 0x00, 0x01, 0x02 })
        {
            var r = FrameReader.Read(Status, WithByte(WiredFrame, 7, flag));
            Assert.Equal(100, r!.Value.Percent);
            Assert.Equal(ChargingState.Full, r.Value.Charging);
        }
    }

    [Fact]
    public void R20_未满电时按充电标志判()
    {
        Assert.Equal(ChargingState.Charging,
            FrameReader.Read(Status, WithByte(NormalFrame, 7, 0x02))!.Value.Charging);
        Assert.Equal(ChargingState.Discharging,
            FrameReader.Read(Status, WithByte(NormalFrame, 7, 0x01))!.Value.Charging);
    }

    // ---------- 充电去抖（判断，留在解释器里） ----------

    [Fact]
    public void R23_孤立的充电标志不判充电()
    {
        var dev = Dev(path: @"\\?\hid#debounce-23");
        var r = Engine.Parse(dev, "status-report7", WithByte(NormalFrame, 7, 0x02));
        Assert.Equal(ChargingState.Discharging, r!.Value.Charging);
    }

    [Fact]
    public void R24_连续两帧充电标志才判充电()
    {
        var dev = Dev(path: @"\\?\hid#debounce-24");
        var frame = WithByte(NormalFrame, 7, 0x02);
        Assert.Equal(ChargingState.Discharging, Engine.Parse(dev, "status-report7", frame)!.Value.Charging);
        Assert.Equal(ChargingState.Charging, Engine.Parse(dev, "status-report7", frame)!.Value.Charging);
    }

    [Fact]
    public void R25_充电标志来回跳时状态稳定不闪()
    {
        var dev = Dev(path: @"\\?\hid#debounce-25");
        var seen = new List<ChargingState>();
        foreach (var flag in new byte[] { 0x02, 0x01, 0x00, 0x02 })
            seen.Add(Engine.Parse(dev, "status-report7", WithByte(NormalFrame, 7, flag))!.Value.Charging);
        Assert.DoesNotContain(ChargingState.Charging, seen);
    }

    [Fact]
    public void R26_满电时去抖直接判充满()
    {
        var dev = Dev(path: @"\\?\hid#debounce-26");
        var r = Engine.Parse(dev, "status-report7", WithByte(WiredFrame, 7, 0x02));
        Assert.Equal(ChargingState.Full, r!.Value.Charging);
    }

    // ---------- Telink 8273（MT 系列，report 0xBB） ----------

    private static readonly byte[] T8273Frame = Convert.FromHexString("BBB0004006014F");

    [Fact]
    public void Telink8273解析电量且充电态Unknown()
    {
        var r = FrameReader.Read(Engine.SpecOf("telink-8273")!.Passive!, T8273Frame);
        Assert.Equal(79, r!.Value.Percent);
        // 0xBB 没有充电位，不能瞎猜
        Assert.Equal(ChargingState.Unknown, r.Value.Charging);
    }

    [Fact]
    public void Telink8273有效位为0时丢弃而不是报0()
        => Assert.Null(FrameReader.Read(Engine.SpecOf("telink-8273")!.Passive!,
            Convert.FromHexString("BBB0004006004F")));

    [Fact]
    public void Telink8273标识不是B0时丢弃()
        => Assert.Null(FrameReader.Read(Engine.SpecOf("telink-8273")!.Passive!,
            Convert.FromHexString("BB00104006014F")));

    // ---------- TelinkKeyboard（A83 Air 等） ----------

    private static readonly byte[] TelinkKbFrame = Convert.FromHexString("000110050403FF06014F");

    [Fact]
    public void Telink键盘按零售布局取电量()
        => Assert.Equal(79, Engine.Parse(Dev(0x4814), "telink-keyboard", TelinkKbFrame)!.Value.Percent);

    /// <summary>V700diy 少一档：电量在 <c>[8]</c>（PID 决定走哪个布局，JSON 里是设备级 override）。</summary>
    [Fact]
    public void Telink键盘V700变体偏移差一位()
    {
        var v700 = Convert.FromHexString("000110050403FF024F");
        Assert.Equal(79, Engine.Parse(Dev(0x4805), "telink-keyboard", v700)!.Value.Percent);
        // 同一个 PID 集合之外仍按零售布局解析，偏移对不上就被 0..100 闸挡掉（报 null 电量）
        Assert.Null(Engine.Parse(Dev(0x4814), "telink-keyboard", v700)!.Value.Percent);
    }

    [Fact]
    public void Telink键盘标记不对时不解析()
        => Assert.Null(Engine.Parse(Dev(0x4814), "telink-keyboard",
            Convert.FromHexString("000210050403FF06014F")));

    // ---------- CH585M（ESK900 系列） ----------

    private static readonly byte[] Ch585mFrame = Convert.FromHexString("000110050403FF0601024F");

    [Fact]
    public void CH585M按自己的偏移取电量()
        => Assert.Equal(79, Engine.Parse(Dev(0x482A), "ch585m", Ch585mFrame)!.Value.Percent);

    [Fact]
    public void CH585M电量越界时丢弃()
        // 结构匹配但电量越界：解释器报 Percent=null（链路位仍要能取），插件据此当"没读到"
        => Assert.Null(Engine.Parse(Dev(0x482A), "ch585m",
            Convert.FromHexString("000110050403FF060102FF"))!.Value.Percent);

    // ---------- GCOME_AP（Ace 68 Turbo / VT68） ----------

    [Fact]
    public void GcomeAp解析充电中()
    {
        var r = FrameReader.Read(Engine.SpecOf("gcome-ap")!.Passive!, Convert.FromHexString("FAFB0700014F"));
        Assert.Equal(79, r!.Value.Percent);
        Assert.Equal(ChargingState.Charging, r.Value.Charging);
    }

    [Fact]
    public void GcomeAp充电码为0时判放电()
        => Assert.Equal(ChargingState.Discharging, FrameReader.Read(
            Engine.SpecOf("gcome-ap")!.Passive!, Convert.FromHexString("FAFB0700004F"))!.Value.Charging);

    [Fact]
    public void GcomeAp满电一律判充满()
        => Assert.Equal(ChargingState.Full, FrameReader.Read(
            Engine.SpecOf("gcome-ap")!.Passive!, Convert.FromHexString("FAFB07000164"))!.Value.Charging);

    [Fact]
    public void GcomeAp帧头不对时不解析()
        => Assert.Null(FrameReader.Read(Engine.SpecOf("gcome-ap")!.Passive!,
            Convert.FromHexString("FAFB0800014F")));

    // ---------- 双 report ID ----------

    [Fact]
    public void 鼠标状态帧同时认07与08()
    {
        var frame8 = Convert.FromHexString("08100500190019014F000301001246010202CD");
        Assert.Equal(79, FrameReader.Read(Status, frame8)!.Value.Percent);
    }

    [Fact]
    public void 其它reportID不算状态帧()
        => Assert.Null(FrameReader.Read(Status,
            Convert.FromHexString("09100500190019014F000301001246010202CD")));

    // ---------- 目录 ----------

    [Fact]
    public void 目录条数与官方解析结果一致()
        => Assert.Equal(195, Engine.DeviceCount);

    [Fact]
    public void 本机VT3按无线PID命中型号与协议族()
    {
        var entry = Engine.DeviceOf(MakeInfo(0x24AE, 0x1412));
        Assert.NotNull(entry);
        Assert.Equal("VT3", entry!.Model);
        Assert.Equal(DeviceKind.Mouse, entry.Kind);
        Assert.Equal("status-report7", entry.Protocol);
        Assert.Equal(0x4612, entry.PairedPid);
    }

    [Fact]
    public void 目录命中时显示名用官方型号()
        => Assert.Equal("雷柏 VT3", Engine.DisplayNameOf(MakeInfo(0x24AE, 0x1412)));

    [Fact]
    public void 未收录的PID没有显示名()
        => Assert.Null(Engine.DisplayNameOf(MakeInfo(0x24AE, 0xFFFF)));

    [Fact]
    public void 其它厂商的相同PID不会被误命中()
        => Assert.Null(Engine.DeviceOf(MakeInfo(0x3837, 0x1412)));

    // ---------- 认领判据 ----------

    [Fact]
    public void 目录命中的设备按自己的协议判形态()
    {
        var spec = Engine.Resolve(MakeInfo(0x24AE, 0x1412, 0xFF00, 0x0002, 19, 19));
        Assert.Equal("status-report7", spec?.Name);
    }

    [Fact]
    public void 目录命中但形态不符时不认领()
    {
        // 同一台鼠标的升级接口（32 字节）不是状态通道
        Assert.Null(Engine.Resolve(MakeInfo(0x24AE, 0x1412, 0xFF00, 0x0002, 32, 32)));
    }

    [Fact]
    public void 未收录设备按注册顺序回落形态()
        => Assert.Equal("status-report7",
            Engine.Resolve(MakeInfo(0x24AE, 0x9999, 0xFF00, 0x0002, 19, 19))?.Name);

    [Fact]
    public void 别家VID一律不认领()
        => Assert.Null(Engine.Resolve(MakeInfo(0x046D, 0xC548, 0xFF00, 0x0001, 19, 19)));

    [Fact]
    public void 未适配的协议不认领()
    {
        // telink-base 声明 implementation=none：目录收录但没有电量通道
        var info = MakeInfo(0x24AE, 0x4827, 0xFF00, 0x000E, 12, 12);
        Assert.Null(Engine.Resolve(info));
    }

    /// <summary>构造一个 HID 接口描述；只填判据用得到的字段（HidDeviceInfo 是 sealed + init-only）。</summary>
    private static VigilWatt.Core.Hid.HidDeviceInfo MakeInfo(ushort vid, ushort pid,
        ushort usagePage = 0xFF00, ushort usage = 0x0002,
        ushort inputLength = 19, ushort outputLength = 19) => new()
        {
            VendorId = vid,
            ProductId = pid,
            UsagePage = usagePage,
            Usage = usage,
            InputReportLength = inputLength,
            OutputReportLength = outputLength,
            DevicePath = $@"\\?\hid#vid_{vid:x4}&pid_{pid:x4}#test",
            Product = "",
        };

    // ---------- 指针遥测（PR-B）：只对已验证的 VT3（0x1412）生效 ----------

    [Fact]
    public void R27_真机VT3帧解出DPI档位与回报率设置()
    {
        var before = DateTimeOffset.Now;
        var r = Engine.Parse(Dev(), "status-report7", NormalFrame);
        var after = DateTimeOffset.Now;

        Assert.NotNull(r);
        var p = r!.Value.Pointer;
        Assert.Equal(3200, p.DpiX.Value);          // [3..4] = 0x0C80
        Assert.Equal(3200, p.DpiY.Value);          // [5..6] = 0x0C80
        Assert.Equal(4, p.DpiLevel.Value);         // [2] = 0x04
        Assert.Equal(1000, p.ReportRateSettingHz.Value);   // [11] = 0x01

        // 来源是设备上报；码表映射不降级成 Inferred
        Assert.Equal(TelemetrySource.DeviceReported, p.DpiX.Source);
        Assert.Equal(TelemetrySource.DeviceReported, p.ReportRateSettingHz.Source);

        // 观测时刻由引擎在边界处盖，必须是真实时间（default 会让它永不过期）
        Assert.InRange(p.DpiX.ObservedAt, before, after);
        Assert.False(p.IsStale(after));
    }

    [Fact]
    public void R28_未验证型号不产出指针遥测()
    {
        // 同一套 status-report7 的 VT2S Air Max（0x4B13）没有 override：电量照旧，遥测全空
        var r = Engine.Parse(Dev(0x4B13, @"\\?\hid#vid_24ae&pid_4b13#test"),
            "status-report7", WiredFrame);

        Assert.NotNull(r);
        Assert.Equal(100, r!.Value.Percent);
        Assert.False(r.Value.Pointer.HasAny);
    }

    [Fact]
    public void R29_协议本体不带指针字段()
    {
        // 指针字段只挂在 1412 的设备级 override 上；写进协议本体会让同族 98 台设备被顺带认领
        Assert.Null(Engine.SpecOf("status-report7")!.Passive!.Pointer);
    }

    [Theory]
    [InlineData(0x08, 125)]
    [InlineData(0x04, 250)]
    [InlineData(0x02, 500)]
    [InlineData(0x01, 1000)]
    [InlineData(0x84, 2000)]     // 132
    [InlineData(0x82, 4000)]     // 130
    [InlineData(0x81, 8000)]     // 129
    public void R30_回报率码表逐档映射(byte code, int expectedHz)
    {
        var r = Engine.Parse(Dev(), "status-report7", WithByte(NormalFrame, 11, code));

        Assert.Equal(expectedHz, r!.Value.Pointer.ReportRateSettingHz.Value);
        Assert.Equal(TelemetrySource.DeviceReported, r.Value.Pointer.ReportRateSettingHz.Source);
    }

    [Fact]
    public void R31_码表外的回报率码不冒充()
    {
        var r = Engine.Parse(Dev(), "status-report7", WithByte(NormalFrame, 11, 0x09));

        Assert.False(r!.Value.Pointer.ReportRateSettingHz.HasValue);
        Assert.Null(r.Value.Pointer.ReportRateSettingHz.Value);   // 不是 0
        Assert.Equal(3200, r.Value.Pointer.DpiX.Value);           // 同帧其它字段不受影响
    }

    [Fact]
    public void R32_全零帧不产生DPI与回报率()
    {
        // 休眠/占位帧：结构合法（reportId 对、长度够）但整帧清零
        var frame = new byte[19];
        frame[0] = 0x07;

        var r = Engine.Parse(Dev(), "status-report7", frame);

        Assert.NotNull(r);
        Assert.Null(r!.Value.Percent);                              // 0% 被既有判据挡掉
        Assert.False(r.Value.Pointer.DpiX.HasValue);                // 0 < min 50
        Assert.False(r.Value.Pointer.ReportRateSettingHz.HasValue); // 0 不在码表
    }

    [Fact]
    public void R33_档位越界不冒充()
    {
        var r = Engine.Parse(Dev(), "status-report7", WithByte(NormalFrame, 2, 7));

        Assert.False(r!.Value.Pointer.DpiLevel.HasValue);
    }

    [Fact]
    public void R34_帧结构不符时不产出任何读数()
    {
        Assert.Null(Engine.Parse(Dev(), "status-report7", WithByte(NormalFrame, 0, 0x04)));
    }

    [Fact]
    public void R35_电量与遥测同帧共存互不影响()
    {
        var r = Engine.Parse(Dev(), "status-report7", NormalFrame);

        Assert.Equal(94, r!.Value.Percent);
        Assert.Equal(ChargingState.Discharging, r.Value.Charging);
        Assert.Equal(LinkState.Up, r.Value.Link);
        Assert.Equal(3200, r.Value.Pointer.DpiX.Value);
    }

    // ---------- 设备能力声明（PR-C）：只读配置查询 ----------

    [Fact]
    public void R36_本机VT3声明三项读取能力_回报率与写能力为未验证()
    {
        var info = MakeInfo(0x24AE, 0x1412);

        Assert.Equal(CapabilityState.Supported, Engine.CapabilityOf(info, DeviceCapability.BatteryRead));
        Assert.Equal(CapabilityState.Supported, Engine.CapabilityOf(info, DeviceCapability.DpiRead));
        Assert.Equal(CapabilityState.Supported, Engine.CapabilityOf(info, DeviceCapability.DpiLevelRead));

        // [11] 只有单点观测、没验证过"改回报率设置后 [11] 随之变化"，证据不足即 unverified（rapoo.md §1.1）
        Assert.Equal(CapabilityState.Unverified, Engine.CapabilityOf(info, DeviceCapability.RateRead));

        // 能读不等于能写：两项写能力没有声明，也不许从读能力推断出来
        Assert.Equal(CapabilityState.Unverified, Engine.CapabilityOf(info, DeviceCapability.DpiWrite));
        Assert.Equal(CapabilityState.Unverified, Engine.CapabilityOf(info, DeviceCapability.RateWrite));
    }

    [Fact]
    public void R37_VT2S两个PID都只声明电量读取()
    {
        // 依据 rapoo.md §5 实测表：有线 4B13 与 2.4G 1B13 都有真机电量读数
        foreach (var pid in new ushort[] { 0x1B13, 0x4B13 })
        {
            var info = MakeInfo(0x24AE, pid);
            Assert.Equal(CapabilityState.Supported, Engine.CapabilityOf(info, DeviceCapability.BatteryRead));
            Assert.Equal(CapabilityState.Unverified, Engine.CapabilityOf(info, DeviceCapability.DpiRead));
            Assert.Equal(CapabilityState.Unverified, Engine.CapabilityOf(info, DeviceCapability.RateRead));
        }
    }

    [Fact]
    public void R38_VT3有线PID没有独立证据_保持未验证()
    {
        // 0x4612 的 caps 是空数组：同一只鼠标的另一条枚举，但没有单独的真机记录
        var info = MakeInfo(0x24AE, 0x4612);

        Assert.Equal(CapabilityState.Unverified, Engine.CapabilityOf(info, DeviceCapability.BatteryRead));
        Assert.Equal(CapabilityState.Unverified, Engine.CapabilityOf(info, DeviceCapability.DpiRead));
    }

    [Fact]
    public void R39_共用status_report7的其他型号不继承VT3的能力()
    {
        // VT3 Air MAX V2 走同一套协议，但没插过机：一项都不能跟着 Supported
        var info = MakeInfo(0x24AE, 0x1B0C);

        foreach (var capability in Enum.GetValues<DeviceCapability>())
            Assert.Equal(CapabilityState.Unverified, Engine.CapabilityOf(info, capability));
    }

    [Fact]
    public void R40_未收录设备与别家VID都返回未验证()
    {
        Assert.Equal(CapabilityState.Unverified,
            Engine.CapabilityOf(MakeInfo(0x24AE, 0xFFFF), DeviceCapability.BatteryRead));
        Assert.Equal(CapabilityState.Unverified,
            Engine.CapabilityOf(MakeInfo(0x3837, 0x1412), DeviceCapability.BatteryRead));

        // 未收录设备的"默认 Unverified"不能顺带把非法枚举值也放过
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Engine.CapabilityOf(MakeInfo(0x24AE, 0xFFFF), (DeviceCapability)99));
    }

    [Fact]
    public void R41_能力查询不动解析状态也不改解析结果()
    {
        var info = MakeInfo(0x24AE, 0x1412);
        var dev = Dev(path: @"\\?\hid#capability-41");
        var charging = WithByte(NormalFrame, 7, 0x02);

        Assert.Equal(ChargingState.Discharging, Engine.Parse(dev, "status-report7", charging)!.Value.Charging);
        Assert.Equal(CapabilityState.Supported, Engine.CapabilityOf(info, DeviceCapability.DpiRead));
        // 去抖计数没被清：第二帧照旧判充电
        Assert.Equal(ChargingState.Charging, Engine.Parse(dev, "status-report7", charging)!.Value.Charging);

        var before = Engine.Parse(dev, "status-report7", NormalFrame)!.Value;
        _ = Engine.CapabilityOf(info, DeviceCapability.BatteryRead);
        var after = Engine.Parse(dev, "status-report7", NormalFrame)!.Value;
        Assert.Equal(before.Percent, after.Percent);
        Assert.Equal(before.Link, after.Link);
        Assert.Equal(before.Pointer.DpiX.Value, after.Pointer.DpiX.Value);
    }

    [Fact]
    public void R42_能力查询不打开设备句柄()
    {
        // 路径指向不存在的设备：只要走 CreateFile 就会抛，能正常返回说明它只读配置
        var info = new VigilWatt.Core.Hid.HidDeviceInfo
        {
            DevicePath = @"\\?\hid#vid_24ae&pid_1412#nope",
            VendorId = 0x24AE,
            ProductId = 0x1412,
        };

        Assert.Equal(CapabilityState.Supported, Engine.CapabilityOf(info, DeviceCapability.BatteryRead));
        Assert.Equal(CapabilityState.Unverified, Engine.CapabilityOf(info, DeviceCapability.RateWrite));
    }
}
