using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.GameSir;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： GameSirCodec 的帧解析回归——字段偏移以官方 Connect 1.16.5 的解析代码为准。
/// 2026/10/19 19:55:00 zc165
/// 依据见 docs/hardware/gamesir.md §1.2。
/// </summary>
public class GameSirCodecTests
{
    /// <summary>按官方布局造一帧手柄状态流：report 0x12，[35]=充电状态、[36]=电量。</summary>
    private static byte[] BuildState(byte chargeState, byte battery)
    {
        var frame = new byte[64];
        frame[0] = GameSirCodec.StateReportId;
        frame[35] = chargeState;
        frame[36] = battery;
        return frame;
    }

    [Fact]
    public void ParsesBatteryAndChargeFromDedicatedBytes()
    {
        var frame = BuildState(chargeState: 1, battery: 87);

        Assert.True(GameSirCodec.TryParseState(frame, out var state));
        Assert.Equal(87, state.Percent);
        Assert.True(state.Charging);
    }

    [Fact]
    public void DischargingWhenChargeStateIsZero()
    {
        var frame = BuildState(chargeState: 0, battery: 55);

        Assert.True(GameSirCodec.TryParseState(frame, out var state));
        Assert.Equal(55, state.Percent);
        Assert.False(state.Charging);
    }

    [Fact]
    public void FullChargeIsNotReportedAsCharging()
    {
        // 官方只认 chargeState == 1 为充电中；100% 且 chargeState=0 由 ChargingStateMap 收尾成"已充满"。
        var frame = BuildState(chargeState: 0, battery: 100);

        Assert.True(GameSirCodec.TryParseState(frame, out var state));
        Assert.Equal(100, state.Percent);
        Assert.False(state.Charging);
        Assert.Equal(ChargingState.Full, GameSirCodec.MapCharging(state.Percent, state.Charging));
    }

    [Fact]
    public void HighBitPercent_IsNotTreatedAsChargingFlag()
    {
        // 回归：0xC8 = 200 若按"bit7=充电位"拆包会变成"充电中 72%"。
        // 官方状态流不做这个拆包，故 200 应被当作越界值直接拒绝。
        var frame = BuildState(chargeState: 0, battery: 0xC8);
        Assert.False(GameSirCodec.TryParseState(frame, out _));
    }

    [Fact]
    public void ZeroPercent_IsRejectedAsNotReady()
    {
        var frame = BuildState(chargeState: 0, battery: 0);
        Assert.False(GameSirCodec.TryParseState(frame, out _));
    }

    [Fact]
    public void OutOfRangePercent_IsRejected()
    {
        var frame = BuildState(chargeState: 0, battery: 127);
        Assert.False(GameSirCodec.TryParseState(frame, out _));
    }

    [Fact]
    public void IgnoresFramesWithOtherReportIds()
    {
        var frame = BuildState(chargeState: 0, battery: 50);
        frame[0] = 0x11;
        Assert.False(GameSirCodec.TryParseState(frame, out _));
    }

    [Fact]
    public void RejectsTruncatedFrame()
    {
        var frame = BuildState(chargeState: 0, battery: 50);
        Assert.False(GameSirCodec.TryParseState(frame.AsSpan(0, 30).ToArray(), out _));
    }

    [Fact]
    public void RejectsFrameWithoutReportIdPrefix()
    {
        // 帧首字节不是 report ID 就不是本协议帧——不做"猜偏移"的兼容（没有证据支持第二种布局）。
        var frame = BuildState(chargeState: 0, battery: 66);
        frame[0] = 0x00;
        Assert.False(GameSirCodec.TryParseState(frame, out _));
    }

    [Fact]
    public void BuildHeartbeat_MatchesOfficialFrameAndPadsToLength()
    {
        // 官方构造器 bm(e=0) = [15, 242, e] → report 0x0F、cmd 0xF2、值 0x00，其余补零。
        var frame = GameSirCodec.BuildHeartbeat(64);

        Assert.Equal(64, frame.Length);
        Assert.Equal(0x0F, frame[0]);
        Assert.Equal(0xF2, frame[1]);
        Assert.Equal(0x00, frame[2]);
        Assert.All(frame.Skip(3), b => Assert.Equal(0, b));
    }

    [Fact]
    public void BuildHeartbeat_RejectsTooShortLength()
        => Assert.Throws<ArgumentOutOfRangeException>(() => GameSirCodec.BuildHeartbeat(2));

    [Theory]
    [InlineData(100, false, ChargingState.Full)]
    [InlineData(100, true, ChargingState.Charging)]
    [InlineData(50, true, ChargingState.Charging)]
    [InlineData(50, false, ChargingState.Discharging)]
    public void MapCharging_FollowsSharedRules(int percent, bool charging, ChargingState expected)
        => Assert.Equal(expected, GameSirCodec.MapCharging(percent, charging));

    [Theory]
    [InlineData("GameSir G7 Pro", "G7 Pro")]
    [InlineData("GAMESIR T4 Pro", "T4 Pro")]
    [InlineData("Xbox 360 Controller for Windows", "Xbox 360 Controller for Windows")]
    [InlineData("", "")]
    public void CleanProductName_StripsVendorPrefix(string input, string expected)
        => Assert.Equal(expected, GameSirCodec.CleanProductName(input));

    /// <summary>真机帧（2026-10-23，PID 0x0575 / Col03）：布局与 0x10B9 相同，两种 PID 共用一套偏移。</summary>
    [Fact]
    public void ParsesRealFrameFromPid0575()
    {
        var frame = new byte[64];
        frame[0] = GameSirCodec.StateReportId;
        frame[1] = 0x7F;
        frame[2] = 0x80;
        frame[3] = 0x7F;
        frame[4] = 0x80;
        frame[5] = 0x0F;
        frame[GameSirCodec.ChargeStateOffset] = 0x00;
        frame[GameSirCodec.BatteryOffset] = 0x64;

        Assert.True(GameSirCodec.TryParseState(frame, out var state));
        Assert.Equal(100, state.Percent);
        Assert.False(state.Charging);
    }
}
