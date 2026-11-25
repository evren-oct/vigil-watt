using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Protocol;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 通用 schema 扩展——percent.map / link.table / requirePercent / chargeWhenMissing / 动态查询模板。
/// 2026/11/18 22:10:00 20211
/// 规则见 docs/dev/protocol-json.md；这里只钉住解释器行为，不测具体品牌。
/// </summary>
public sealed class ProtocolSchemaTests
{
    private const string EmptyDevices = """{ "devices": [] }""";

    private static ProtocolSpec Load(string frameJson)
    {
        var protocols = $$"""
            {
              "vendors": ["0x1234"],
              "protocols": {
                "p": {
                  "kind": "gamepad",
                  "interface": { "usagePage": 1 },
                  "passive": {{frameJson}}
                }
              }
            }
            """;
        return ProtocolSpecLoader.LoadFromJson(protocols, EmptyDevices).Protocols["p"];
    }

    private static byte[] Frame(int length, params (int Index, byte Value)[] bytes)
    {
        var frame = new byte[length];
        foreach (var (index, value) in bytes) frame[index] = value;
        return frame;
    }

    [Fact]
    public void PercentMap_ArrayHitAndMiss()
    {
        var spec = Load("""
            {
              "reportId": [4],
              "length": { "min": 24 },
              "validPercent": { "min": 0 },
              "fields": { "percent": { "offset": 1, "mask": 7, "map": [0, 50, 80, 100, 100] } }
            }
            """).Passive!;

        Assert.Equal(80, FrameReader.Read(spec, Frame(24, (0, 4), (1, 2)))!.Value.Percent);
        Assert.Equal(0, FrameReader.Read(spec, Frame(24, (0, 4), (1, 0)))!.Value.Percent);
        Assert.Null(FrameReader.Read(spec, Frame(24, (0, 4), (1, 5)))!.Value.Percent);
    }

    [Fact]
    public void PercentMap_ObjectHit()
    {
        var spec = Load("""
            {
              "reportId": [4],
              "length": { "min": 24 },
              "validPercent": { "min": 0 },
              "fields": { "percent": { "offset": 1, "mask": 7, "map": { "0": 0, "2": 80 } } }
            }
            """).Passive!;

        Assert.Equal(80, FrameReader.Read(spec, Frame(24, (0, 4), (1, 2)))!.Value.Percent);
        Assert.Null(FrameReader.Read(spec, Frame(24, (0, 4), (1, 1)))!.Value.Percent);
    }

    [Fact]
    public void PercentMap_MissDoesNotBecomeZero()
    {
        var spec = Load("""
            {
              "reportId": [4],
              "length": { "min": 24 },
              "validPercent": { "min": 0 },
              "fields": { "percent": { "offset": 1, "map": [50, 80] } }
            }
            """).Passive!;

        Assert.Null(FrameReader.Read(spec, Frame(24, (0, 4), (1, 2)))!.Value.Percent);
    }

    [Fact]
    public void RequirePercent_RejectsZeroAndOutOfRange()
    {
        var spec = Load("""
            {
              "reportId": [2],
              "length": { "min": 8 },
              "requirePercent": true,
              "fields": { "percent": { "offset": 2 } }
            }
            """).Passive!;

        Assert.Null(FrameReader.Read(spec, Frame(8, (0, 2), (2, 0))));
        Assert.Null(FrameReader.Read(spec, Frame(8, (0, 2), (2, 101))));
        Assert.Equal(93, FrameReader.Read(spec, Frame(8, (0, 2), (2, 93)))!.Value.Percent);
    }

    [Fact]
    public void LinkTable_HitMissAndOnMiss()
    {
        var spec = Load("""
            {
              "reportId": [4],
              "length": { "min": 24 },
              "fields": {
                "percent": { "offset": 1 },
                "link": { "offset": 23, "table": { "0x50": "down", "0x10": "down" }, "onMiss": "up" }
              }
            }
            """).Passive!;

        Assert.Equal(LinkState.Down, FrameReader.Read(spec, Frame(24, (0, 4), (23, 0x50)))!.Value.Link);
        Assert.Equal(LinkState.Down, FrameReader.Read(spec, Frame(24, (0, 4), (23, 0x10)))!.Value.Link);
        Assert.Equal(LinkState.Up, FrameReader.Read(spec, Frame(24, (0, 4), (23, 0x58)))!.Value.Link);
        Assert.Equal(LinkState.Up, FrameReader.Read(spec, Frame(24, (0, 4), (23, 0x00)))!.Value.Link);
    }

    [Fact]
    public void ChargeWhenMissing_DischargingOrUnknown()
    {
        var discharging = Load("""
            {
              "reportId": [4],
              "length": { "min": 24 },
              "chargeWhenMissing": "discharging",
              "fields": { "percent": { "offset": 1 } }
            }
            """).Passive!;
        var unknown = Load("""
            {
              "reportId": [4],
              "length": { "min": 24 },
              "fields": { "percent": { "offset": 1 } }
            }
            """).Passive!;

        var frame = Frame(24, (0, 4), (1, 80));
        Assert.Equal(ChargingState.Discharging, FrameReader.Read(discharging, frame)!.Value.Charging);
        Assert.Equal(ChargingState.Unknown, FrameReader.Read(unknown, frame)!.Value.Charging);
    }

    [Fact]
    public void QueryFrame_BuildsFromCaps()
    {
        var spec = new QuerySpec
        {
            Header = 0x15,
            ReportIdFromCaps = true,
            PadToInterfaceOutputLength = true,
            FallbackLength = 64,
        };

        var withId = QueryRunner.BuildFrame(spec, 2, 64)!;
        Assert.Equal(64, withId.Length);
        Assert.Equal(0x02, withId[0]);
        Assert.Equal(0x15, withId[1]);

        var withoutId = QueryRunner.BuildFrame(spec, 0, 64)!;
        Assert.Equal(0x15, withoutId[0]);

        Assert.Null(QueryRunner.BuildFrame(spec, null, 64));
        Assert.Equal(64, QueryRunner.BuildFrame(spec, 2, 1)!.Length);
    }

    [Fact]
    public void QueryFrame_FixedFormIsReturnedUnchanged()
    {
        var fixedBytes = new byte[] { 0x00, 0x55, 0x30 };
        var spec = new QuerySpec { Fixed = fixedBytes };
        Assert.Same(fixedBytes, QueryRunner.BuildFrame(spec, 2, 64));
    }

    [Fact]
    public void QueryRunner_WriteFailureReturnsNull()
    {
        var info = new HidDeviceInfo
        {
            DevicePath = $"test-{Guid.NewGuid():N}",
            InputReportLength = 8,
            OutputReportLength = 8,
        };
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(path, []);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite, 0, FileOptions.DeleteOnClose);
        using var handle = new HidDeviceHandle(info, stream);
        var active = new ActiveSpec
        {
            Query = new QuerySpec { Fixed = [0x01] },
            WindowMs = 10,
            Response = new FrameSpec { ReportId = [1], Length = new IntMatch(1, null, null, null, null) },
        };

        Assert.Null(QueryRunner.Run(handle, active));
    }

    [Fact]
    public void NewBetopDevice_CanBeAddedByJsonOnly()
    {
        const string protocols = """
            {
              "vendors": ["0x20BC"],
              "protocols": {
                "betop-status15": {
                  "kind": "gamepad",
                  "interface": { "usagePage": 255, "usage": 3, "inputLength": 64 },
                  "passive": {
                    "reportId": [2],
                    "length": { "min": 64 },
                    "prefix": [ { "offset": 1, "value": 21 } ],
                    "fields": { "percent": { "offset": 2 } }
                  }
                }
              }
            }
            """;
        const string devices = """
            {
              "brand": "北通",
              "devices": [
                { "vid": "0x20bc", "pid": "0x9abc", "model": "测试型号",
                  "kind": "gamepad", "protocol": "betop-status15" }
              ]
            }
            """;

        var engine = ProtocolEngine.FromJson(protocols, devices);
        var info = new HidDeviceInfo
        {
            DevicePath = "test-json-only",
            VendorId = 0x20BC,
            ProductId = 0x9ABC,
            UsagePage = 0x00FF,
            Usage = 0x0003,
            InputReportLength = 64,
            OutputReportLength = 64,
        };
        Assert.Equal("测试型号", engine.DeviceOf(info)!.Model);
        Assert.True(engine.Matches(info));

        var frame = new byte[64];
        frame[0] = 0x02;
        frame[1] = 0x15;
        frame[2] = 77;
        var outcome = engine.Parse(new DeviceRef("id", info.DevicePath, info.VendorId, info.ProductId),
            "betop-status15", frame);
        Assert.Equal(77, outcome!.Value.Percent);
    }
}
