using VigilWatt.Core.Devices;
using VigilWatt.Core.Models;
using VigilWatt.Core.Storage;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 剩余使用时间估算规则的回归测试。
/// 2026/10/18 16:02:29 xliu
/// </summary>
public sealed class RemainingTimeEstimatorTests
{
    [Fact]
    public void Estimate_UsesElapsedTimeAndNetDrop()
    {
        var now = DateTimeOffset.UtcNow;
        var samples = new[]
        {
            Sample(90, now.AddHours(-1)),
            Sample(88, now.AddMinutes(-30)),
            Sample(86, now),
        };

        var result = RemainingTimeEstimator.Estimate(samples, 86, now);

        Assert.Equal(TimeSpan.FromHours(21.5), result);
    }

    [Fact]
    public void Estimate_RequiresThreeSamplesAndMeasurableDrop()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Null(RemainingTimeEstimator.Estimate(
            [Sample(80, now.AddMinutes(-20)), Sample(79, now)], 79, now));
        Assert.Null(RemainingTimeEstimator.Estimate(
            [Sample(80, now.AddMinutes(-20)), Sample(80, now.AddMinutes(-10)), Sample(80, now)], 80, now));
        Assert.Equal(TimeSpan.FromMinutes(1580), RemainingTimeEstimator.Estimate(
            [Sample(80, now.AddMinutes(-20)), Sample(80, now.AddMinutes(-10)), Sample(79, now)], 79, now));
    }

    [Fact]
    public void Evaluate_ReportsWhyEstimateIsUnavailable()
    {
        var now = DateTimeOffset.UtcNow;

        var tooFew = RemainingTimeEstimator.Evaluate(
            [Sample(80, now.AddMinutes(-10)), Sample(79, now)], 79, now);
        Assert.Equal(RemainingTimeUnavailableReason.CollectingSamples, tooFew.Reason);
        Assert.Contains("采集点不足", tooFew.Explain());

        var charging = RemainingTimeEstimator.Evaluate(
            [Sample(90, now.AddHours(-2)), Sample(85, now.AddHours(-1), charging: true),
             Sample(84, now.AddMinutes(-30)), Sample(84, now)], 84, now);
        Assert.Equal(RemainingTimeUnavailableReason.CollectingAfterCharge, charging.Reason);

        var unchanged = RemainingTimeEstimator.Evaluate(
            [Sample(80, now.AddHours(-2)), Sample(80, now.AddHours(-1)), Sample(80, now)], 80, now);
        Assert.Equal(RemainingTimeUnavailableReason.WaitingForBatteryDrop, unchanged.Reason);
    }

    [Fact]
    public void Estimate_OnlyUsesSamplesAfterMostRecentCharge()
    {
        var now = DateTimeOffset.UtcNow;
        var samples = new[]
        {
            Sample(100, now.AddHours(-2)),
            Sample(90, now.AddMinutes(-90)),
            Sample(85, now.AddMinutes(-80), charging: true),
            Sample(90, now.AddMinutes(-40)),
            Sample(88, now.AddMinutes(-20)),
            Sample(86, now),
        };

        Assert.Equal(TimeSpan.FromMinutes(860), RemainingTimeEstimator.Estimate(samples, 86, now));
    }

    [Fact]
    public void Estimate_SortsSamplesAndDeduplicatesTimestampsByNetProgress()
    {
        var now = DateTimeOffset.UtcNow;
        var samples = new[]
        {
            Sample(86, now),
            Sample(88, now.AddMinutes(-30)),
            Sample(88, now.AddMinutes(-30)),
            Sample(90, now.AddHours(-1)),
        };

        Assert.Equal(TimeSpan.FromHours(21.5), RemainingTimeEstimator.Estimate(samples, 86, now));
    }

    [Fact]
    public void Estimate_RejectsStaleFutureAndInvalidCurrentReadings()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Null(RemainingTimeEstimator.Estimate(
            [Sample(90, now.AddDays(-92)), Sample(85, now.AddDays(-91)), Sample(80, now.AddDays(-90).AddMinutes(-1))], 80, now));
        Assert.Null(RemainingTimeEstimator.Estimate(
            [Sample(90, now.AddHours(-1)), Sample(88, now.AddMinutes(-30)), Sample(85, now.AddMinutes(1))], 85, now));
        Assert.Null(RemainingTimeEstimator.Estimate(
            [Sample(90, now.AddHours(-1)), Sample(88, now.AddMinutes(-30)), Sample(85, now)], 0, now));
    }

    [Fact]
    public void Estimate_UsesLongHistoryForSlowDischarge()
    {
        var now = DateTimeOffset.UtcNow;
        var samples = new[]
        {
            Sample(100, now.AddDays(-10)),
            Sample(99, now.AddDays(-5)),
            Sample(98, now),
        };

        Assert.Equal(TimeSpan.FromDays(490), RemainingTimeEstimator.Estimate(samples, 98, now));
    }

    [Theory]
    [InlineData(45, "45分钟")]
    [InlineData(90, "1小时30分钟")]
    [InlineData(120, "2小时")]
    [InlineData(1500, "1天1小时")]
    public void Format_UsesReadableChineseUnits(int minutes, string expected)
        => Assert.Equal(expected, RemainingTimeEstimator.Format(TimeSpan.FromMinutes(minutes)));

    [Theory]
    [InlineData(45, "45分钟")]              // 不足一天：不补括号
    [InlineData(120, "2小时")]
    [InlineData(1440, "1天（24小时）")]
    [InlineData(1500, "1天1小时（25小时）")]
    [InlineData(3250, "2天6小时（54小时）")]  // 分钟被截掉，总数也按 54 算，两个数字一致
    public void FormatWithTotalHours_AppendsHoursOnlyBeyondOneDay(int minutes, string expected)
        => Assert.Equal(expected, RemainingTimeEstimator.FormatWithTotalHours(TimeSpan.FromMinutes(minutes)));

    private static HistorySample Sample(int percent, DateTimeOffset timestamp, bool charging = false)
        => new(0, "device", "device", DeviceKind.Mouse, percent, charging, timestamp);
}
