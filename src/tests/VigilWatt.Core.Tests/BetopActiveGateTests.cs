using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins;
using VigilWatt.Core.Plugins.Protocol;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 北通主动查询安全门控——未验证设备只被动监听，已验证设备保持查询。
/// 2026/11/19 10:00:00 20211
/// 门控由目录 capabilities.batteryRead 决定，见 docs/hardware/safety.md §3 与 betop.md §1.3。
/// </summary>
public sealed class BetopActiveGateTests
{
    private const string ProtocolsResource = "VigilWatt.Core.Plugins.Betop.specs.betop.protocols.json";
    private const string CatalogResource = "VigilWatt.Core.Plugins.Betop.betop-catalog.json";

    private static readonly ProtocolEngine Engine = ProtocolEngine.Load(ProtocolsResource, CatalogResource);

    /// <summary>固定查询帧的测试规格：真实北通查询要读 HID caps，临时文件句柄读不到，只能这样测主动分支。</summary>
    private static readonly ProtocolEngine GateEngine = ProtocolEngine.FromJson(GateProtocols, GateDevices);

    private const string GateProtocols = """
        {
          "vendors": ["0x20BC"],
          "protocols": {
            "gate-active-only": {
              "kind": "gamepad",
              "implementation": "data",
              "pollable": true,
              "probeWindowMs": 30,
              "interface": { "usagePage": 255, "usage": 3, "inputLength": 64 },
              "active": {
                "query": { "length": 64, "hex": "02 15" },
                "windowMs": 50,
                "attempts": 1,
                "response": {
                  "reportId": [2],
                  "tryOffsets": [0, 1],
                  "length": { "min": 64 },
                  "prefix": [ { "offset": 1, "value": 21 } ],
                  "requirePercent": true,
                  "fields": {
                    "percent": { "offset": 2 },
                    "charge": { "offset": 4, "mask": 15, "nonZero": true }
                  }
                }
              }
            }
          }
        }
        """;

    private const string GateDevices = """
        {
          "brand": "北通",
          "devices": [
            { "vid": "0x20bc", "pid": "0x5158", "model": "测试授权", "kind": "gamepad",
              "protocol": "gate-active-only",
              "capabilities": { "batteryRead": "supported" } },
            { "vid": "0x20bc", "pid": "0x516f", "model": "测试未验证", "kind": "gamepad",
              "protocol": "gate-active-only" }
          ]
        }
        """;

    private static HidDeviceInfo Info(
        ushort pid, ushort vid = 0x20BC, ushort page = 0x00FF, ushort usage = 0x0003, ushort length = 64)
        => new()
        {
            DevicePath = $"test-{vid:X4}-{pid:X4}-{page:X4}-{usage:X4}-{length}",
            VendorId = vid,
            ProductId = pid,
            UsagePage = page,
            Usage = usage,
            InputReportLength = length,
            OutputReportLength = length,
        };

    private static DeviceRef RefOf(HidDeviceInfo info)
        => new("test", info.DevicePath, info.VendorId, info.ProductId);

    private static byte[] Status15(byte percent, byte charge = 0x50)
    {
        var frame = new byte[64];
        frame[0] = 0x02;
        frame[1] = 0x15;
        frame[2] = percent;
        frame[4] = charge;
        return frame;
    }

    private static byte[] Zeus(byte raw1, byte raw23)
    {
        var frame = new byte[24];
        frame[0] = 0x04;
        frame[1] = raw1;
        frame[23] = raw23;
        return frame;
    }

    private static byte[] ActiveExchange(byte percent)
    {
        var input = new byte[128];
        Array.Copy(Status15(percent), 0, input, 64, 64);
        return input;
    }

    [Fact]
    public async Task UnverifiedProbe_DoesNotWrite()
    {
        var info = Info(0x516F);
        using var fake = new TrackingHandle(info, []);

        var probe = await GateEngine.ProbeAsync(fake.Handle, writesEnabled: true, activeAllowed: false);

        Assert.Null(probe);
        Assert.Equal(0, fake.Length);
    }

    [Fact]
    public void UnverifiedPoll_ReturnsNotAttemptedWithoutWrite()
    {
        var info = Info(0x516F);
        using var fake = new TrackingHandle(info, []);

        var outcome = GateEngine.Poll(
            fake.Handle, RefOf(info), "gate-active-only", writesEnabled: true, activeAllowed: false);

        Assert.Equal(PollAttempt.NotAttempted, outcome.Attempt);
        Assert.Equal(0, fake.Length);
    }

    [Fact]
    public async Task UnverifiedPassiveFrame_IsStillRead()
    {
        var info = Info(0x516F);
        using var fake = new TrackingHandle(info, Status15(93));

        var probe = await Engine.ProbeAsync(fake.Handle, writesEnabled: true, activeAllowed: false);

        Assert.NotNull(probe);
        Assert.Equal("betop-status15", probe.Value.Protocol);
        Assert.Equal(93, probe.Value.Percent);
        Assert.Equal(ChargingState.Discharging, probe.Value.Charging);
        Assert.Equal(64, fake.Length);
    }

    [Fact]
    public async Task VerifiedProbe_StillSendsQueryAndParsesReply()
    {
        var info = Info(0x5158);
        using var fake = new TrackingHandle(info, ActiveExchange(78));

        var probe = await GateEngine.ProbeAsync(fake.Handle, writesEnabled: true, activeAllowed: true);

        Assert.NotNull(probe);
        Assert.Equal(78, probe.Value.Percent);
        Assert.Equal(ChargingState.Discharging, probe.Value.Charging);
    }

    [Fact]
    public void VerifiedPoll_KeepsActiveQueryBehavior()
    {
        var info = Info(0x5158);
        using var fake = new TrackingHandle(info, ActiveExchange(78));

        var outcome = GateEngine.Poll(
            fake.Handle, RefOf(info), "gate-active-only", writesEnabled: true, activeAllowed: true);

        Assert.Equal(PollAttempt.Answered, outcome.Attempt);
        Assert.Equal(78, outcome.Percent);
        Assert.Equal(ChargingState.Discharging, outcome.Charging);
    }

    [Fact]
    public async Task UnregisteredInterfaceMatch_DoesNotGetActiveQuery()
    {
        var info = Info(0x9999);
        Assert.NotNull(GateEngine.Resolve(info));
        Assert.Null(GateEngine.DeviceOf(info));
        Assert.Equal(CapabilityState.Unverified,
            GateEngine.CapabilityOf(info, DeviceCapability.BatteryRead));

        using var probeFake = new TrackingHandle(info, []);
        var probe = await GateEngine.ProbeAsync(
            probeFake.Handle, writesEnabled: true, activeAllowed: false);
        Assert.Null(probe);
        Assert.Equal(0, probeFake.Length);

        using var pollFake = new TrackingHandle(info, []);
        var outcome = GateEngine.Poll(
            pollFake.Handle, RefOf(info), "gate-active-only", writesEnabled: true, activeAllowed: false);
        Assert.Equal(PollAttempt.NotAttempted, outcome.Attempt);
        Assert.Equal(0, pollFake.Length);
    }

    [Fact]
    public async Task ZeusPassiveOnly_KeepsExistingBehavior()
    {
        var info = Info(0x5073, page: 0xFF00, usage: 0x0002, length: 24);
        Assert.False(Engine.SpecOf("betop-zeus-keyevent")!.Pollable);

        using var fake = new TrackingHandle(info, Zeus(0x02, 0x58));
        var probe = await Engine.ProbeAsync(fake.Handle, writesEnabled: true, activeAllowed: false);

        Assert.NotNull(probe);
        Assert.Equal("betop-zeus-keyevent", probe.Value.Protocol);
        Assert.Equal(80, probe.Value.Percent);
        Assert.Equal(24, fake.Length);

        var outcome = Engine.Poll(
            fake.Handle, RefOf(info), "betop-zeus-keyevent", writesEnabled: true, activeAllowed: false);
        Assert.Equal(PollAttempt.NotAttempted, outcome.Attempt);
    }

    [Fact]
    public async Task WritesDisabled_DoesNotWriteEvenWhenVerified()
    {
        var info = Info(0x5158);
        using var pollFake = new TrackingHandle(info, []);
        var outcome = GateEngine.Poll(
            pollFake.Handle, RefOf(info), "gate-active-only", writesEnabled: false, activeAllowed: true);
        Assert.Equal(PollAttempt.NotAttempted, outcome.Attempt);
        Assert.Equal(0, pollFake.Length);

        using var probeFake = new TrackingHandle(info, []);
        var probe = await GateEngine.ProbeAsync(
            probeFake.Handle, writesEnabled: false, activeAllowed: true);
        Assert.Null(probe);
        Assert.Equal(0, probeFake.Length);
    }

    [Theory]
    [InlineData(0x5158, CapabilityState.Supported)]
    [InlineData(0x5159, CapabilityState.Supported)]
    [InlineData(0x516F, CapabilityState.Unverified)]
    [InlineData(0x5191, CapabilityState.Unverified)]
    public void CatalogBatteryRead_DefinesTheGate(ushort pid, CapabilityState expected)
    {
        var info = Info(pid);
        Assert.Equal(expected, Engine.CapabilityOf(info, DeviceCapability.BatteryRead));
    }

    [Theory]
    [InlineData(CapabilityState.Supported, true)]
    [InlineData(CapabilityState.Unverified, false)]
    [InlineData(CapabilityState.Unsupported, false)]
    public void BatteryReadVerified_OnlyAcceptsSupported(CapabilityState state, bool expected)
    {
        var plugin = new CapabilityPlugin { State = state };
        Assert.Equal(expected, plugin.Verified(Info(0x5158)));
    }

    /// <summary>临时文件当输入；写帧会在当前末尾追加，长度变化就是"确实发过帧"。</summary>
    private sealed class TrackingHandle : IDisposable
    {
        private readonly FileStream _stream;

        public TrackingHandle(HidDeviceInfo info, byte[] initial)
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            _stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite,
                FileShare.Read, 0, FileOptions.DeleteOnClose);
            _stream.Write(initial);
            _stream.Flush();
            _stream.Position = 0;
            Handle = new HidDeviceHandle(info, _stream);
        }

        public HidDeviceHandle Handle { get; }
        public long Length => _stream.Length;

        public void Dispose() => Handle.Dispose();
    }

    private sealed class CapabilityPlugin : BatteryPluginBase
    {
        public CapabilityState State { get; set; }
        public bool Verified(HidDeviceInfo info) => BatteryReadVerified(info);

        public override string Name => "GateTest";
        public override string DisplayName => "GateTest";
        public override string DisplayNameEn => "GATETEST";
        public override DeviceKind Kind => DeviceKind.Gamepad;

        protected override CapabilitySet CapabilitiesOf(HidDeviceInfo info)
            => CapabilitySet.Create([new(DeviceCapability.BatteryRead, State)]);

        protected override bool IsCandidate(HidDeviceInfo info) => false;
        protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
            => null;
        protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev) => null;
    }
}
