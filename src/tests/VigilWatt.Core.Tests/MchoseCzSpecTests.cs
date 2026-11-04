using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Protocol;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： cz-block（迈从通用 2.4G dongle 的分块读）的规格加载与位域解析。
/// 2026/11/04 wenliheng
/// 样本帧取自本机 0x3837:0x3033 真机；协议来源与判据见 hardware/mchose.md §2.4。
/// </summary>
public sealed class MchoseCzSpecTests
{
    private const string Protocols = "VigilWatt.Core.Plugins.Mchose.specs.mchose.protocols.json";
    private const string Devices = "VigilWatt.Core.Plugins.Mchose.mchose-catalog.json";

    /// <summary>真机状态块首段（0..0x37）：[0x20] 同时承载充电位与电量。</summary>
    private static readonly byte[] Block =
    [
        0x50, 0x00, 0xAA, 0xBB, 0x01, 0x01, 0x00, 0x01, 0x02, 0x64, 0x00, 0x00, 0x00, 0x00, 0xFF, 0x55,
        0x00, 0x00, 0x00, 0x00, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x64, 0x01, 0x01, 0x01, 0xFF, 0x00, 0x00,
        0x5A, 0x00, 0x02, 0x06, 0x00, 0x01, 0x01, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0x10, 0x10, 0x10, 0x10,
        0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x10,
    ];

    private static FrameSpec Response
        => ProtocolSpecLoader.Load(Protocols, Devices).Protocols["cz-block"].Active!.Response;

    /// <summary>按真机应答布局组一帧：reportID 槽 + <c>AA 05 00 xx size off</c> + 56 字节块。</summary>
    private static byte[] FrameWith(byte status, int magicIndex = 2, byte magic = 0xAA)
    {
        var block = (byte[])Block.Clone();
        block[0x20] = status;
        block[magicIndex] = magic;

        var frame = new byte[65];
        frame[0] = 0x00;                       // report ID 槽（本机该接口 report ID = 0）
        frame[1] = 0xAA;
        frame[2] = 0x05;
        frame[5] = 0x38;                       // 本段长度
        Array.Copy(block, 0, frame, 9, block.Length);
        return frame;
    }

    // ── 规格加载 ──────────────────────────────────────────────────────

    [Fact]
    public void CZ1_规格按接口指纹认领且只发一条读帧()
    {
        var spec = ProtocolSpecLoader.Load(Protocols, Devices).Protocols["cz-block"];

        Assert.Equal(65329, spec.Interface.UsagePage!.Value.Eq);     // 0xFF31
        Assert.Equal(116, spec.Interface.Usage!.Value.Eq);           // 0x74
        Assert.Equal(65, spec.Interface.OutputLength!.Value.Eq);
        Assert.True(spec.Pollable);

        // 官方 web 驱动对这颗 dongle 发的就是这一帧：55 05 00 [off+size] size offLo offHi，其余补零
        Assert.Equal(65, spec.Active!.Query.Length);
        Assert.Equal(new byte[] { 0x00, 0x55, 0x05, 0x00, 0x38, 0x38, 0x00, 0x00, 0x00 }, spec.Active.Query[..9]);
        Assert.Equal(65, spec.Active.Response.Length!.Value.Eq);
    }

    [Fact]
    public void CZ2_电量与充电位同处一字节_掩码分别是低7位与bit7()
    {
        var response = Response;

        Assert.Equal(41, response.Percent!.Offset);                  // 0x20 + 数据段起点 9
        Assert.Equal(0x7F, response.Percent.Mask);
        Assert.Equal(41, response.Charge!.Offset);
        Assert.Equal(0x80, response.Charge.Mask);
        Assert.Equal(100, response.FullAt!.Value);
    }

    // ── 帧解析：三态 ──────────────────────────────────────────────────

    [Fact]
    public void CZ3_真机放电帧_低7位是电量()
    {
        var r = FrameReader.Read(Response, FrameWith(0x5A));         // 真机 90%、未接线

        Assert.Equal(90, r!.Value.Percent);
        Assert.Equal(ChargingState.Discharging, r.Value.Charging);
    }

    [Fact]
    public void CZ4_bit7置位判充电_电量不受充电位影响()
    {
        var r = FrameReader.Read(Response, FrameWith(0xCE));         // 78 | 0x80

        Assert.Equal(78, r!.Value.Percent);
        Assert.Equal(ChargingState.Charging, r.Value.Charging);
    }

    [Fact]
    public void CZ5_满电判充满_优先于充电位()
    {
        var r = FrameReader.Read(Response, FrameWith(0x64));

        Assert.Equal(100, r!.Value.Percent);
        Assert.Equal(ChargingState.Full, r.Value.Charging);
    }

    [Fact]
    public void CZ6_魔数不符不算状态帧()
    {
        // 块首 0x50 不是电量，AA BB 才是"这是一块合法状态块"的判据（见 mchose.md §2.4）
        Assert.Null(FrameReader.Read(Response, FrameWith(0x5A, magicIndex: 2, magic: 0x00)));
        Assert.Null(FrameReader.Read(Response, FrameWith(0x5A, magicIndex: 3, magic: 0x00)));
    }

    [Fact]
    public void CZ7_mask为0属配置错误_加载期就拒()
    {
        const string protocols = """
            {
              "vendors": ["0x3837"],
              "protocols": {
                "p": {
                  "kind": "keyboard",
                  "interface": { "usagePage": 65329 },
                  "active": {
                    "query": { "length": 65 },
                    "response": { "length": { "eq": 65 }, "fields": { "percent": { "offset": 9, "mask": 0 } } }
                  }
                }
              }
            }
            """;

        Assert.Throws<InvalidOperationException>(() => ProtocolSpecLoader.LoadFromJson(protocols, """{ "devices": [] }"""));
    }
}
