using VigilWatt.Core.Devices;
using VigilWatt.Core.Models;
using VigilWatt.Core.Storage;
using VigilWatt.Core.Utils;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： HistoryStore 的删除与统计能力（历史管理页依赖它）。
/// 2026/10/06 16:58:42 xliu
/// 用临时库跑真 SQLite—— "DELETE 不还文件空间"、"VACUUM 不丢数据"这类行为只有真库能验。
/// </summary>
public sealed class HistoryStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _dbPath;

    public HistoryStoreTests()
    {
        // 不重定向的话 HistoryStore 构造里的 Log.Info 会写进真实 app.log（污染用户日志）
        Log.RedirectTo(Path.Combine(Path.GetTempPath(), "VigilWattTests", "logs"));
        _dir = Path.Combine(Path.GetTempPath(), "VigilWattTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "history.db");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* 句柄未释放时忽略，临时目录由系统回收 */ }
    }

    private HistoryStore NewStore() => new(_dbPath);

    private static DeviceStatus Dev(string id, string name, int? percent, DateTimeOffset ts,
        bool charging = false) => new()
    {
        Id = id,
        Name = name,
        Source = DeviceSource.Hid24G,
        BatteryPercent = percent,
        Charging = charging ? ChargingState.Charging : ChargingState.Discharging,
        LastUpdated = ts,
    };

    private static readonly DateTimeOffset T0 = new(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void H1_空库统计全零且时间范围为空()
    {
        using var store = NewStore();

        var stats = store.GetStats();

        Assert.Equal(0, stats.TotalCount);
        Assert.Equal(0, stats.DeviceCount);
        Assert.Null(stats.First);
        Assert.Null(stats.Last);
        Assert.True(stats.FileBytes > 0, "空库文件本身也应已创建");
        Assert.Empty(store.GetDeviceStats());
    }

    [Fact]
    public void H2_插入后统计条数设备数与时间范围正确()
    {
        using var store = NewStore();
        store.Insert(Dev("kb", "键盘", 90, T0));
        store.Insert(Dev("kb", "键盘", 80, T0.AddHours(1)));
        store.Insert(Dev("mouse", "鼠标", 50, T0.AddHours(2)));

        var stats = store.GetStats();

        Assert.Equal(3, stats.TotalCount);
        Assert.Equal(2, stats.DeviceCount);
        Assert.NotNull(stats.First);
        Assert.NotNull(stats.Last);
        Assert.Equal(T0, stats.First.Value);
        Assert.Equal(T0.AddHours(2), stats.Last.Value);
    }

    [Fact]
    public void H3_无电量读数不写库()
    {
        using var store = NewStore();
        store.Insert(Dev("kb", "键盘", null, T0));

        Assert.Equal(0, store.GetStats().TotalCount);
    }

    [Fact]
    public void H4_按设备聚合条数与时间范围_最近有数据的排前面()
    {
        using var store = NewStore();
        store.Insert(Dev("kb", "键盘", 90, T0));
        store.Insert(Dev("kb", "键盘", 80, T0.AddHours(1)));
        store.Insert(Dev("mouse", "鼠标", 50, T0.AddHours(5)));

        var stats = store.GetDeviceStats();

        Assert.Equal(2, stats.Count);
        Assert.Equal("mouse", stats[0].DeviceId);

        var kb = stats.Single(s => s.DeviceId == "kb");
        Assert.Equal("键盘", kb.Name);
        Assert.Equal(2, kb.Count);
        Assert.Equal(T0, kb.First);
        Assert.Equal(T0.AddHours(1), kb.Last);
    }

    [Fact]
    public void H5_只删指定设备_其他设备不受影响()
    {
        using var store = NewStore();
        store.Insert(Dev("kb", "键盘", 90, T0));
        store.Insert(Dev("kb", "键盘", 88, T0.AddHours(1)));
        store.Insert(Dev("mouse", "鼠标", 50, T0.AddHours(2)));

        int deleted = store.DeleteDevices(new[] { "kb" });

        Assert.Equal(2, deleted);
        var stats = store.GetStats();
        Assert.Equal(1, stats.TotalCount);
        Assert.Equal(1, stats.DeviceCount);
        Assert.Empty(store.Query("kb", T0.AddDays(-1), T0.AddDays(1)));
        Assert.Single(store.Query("mouse", T0.AddDays(-1), T0.AddDays(1)));
    }

    [Fact]
    public void H6_批量删除多台设备()
    {
        using var store = NewStore();
        store.Insert(Dev("kb", "键盘", 90, T0));
        store.Insert(Dev("mouse", "鼠标", 50, T0));
        store.Insert(Dev("pad", "手柄", 70, T0));

        int deleted = store.DeleteDevices(new[] { "kb", "mouse" });

        Assert.Equal(2, deleted);
        Assert.Equal(1, store.GetStats().TotalCount);
        Assert.Equal("pad", store.GetDeviceStats().Single().DeviceId);
    }

    [Fact]
    public void H7_空集合是空操作不清库()
    {
        using var store = NewStore();
        store.Insert(Dev("kb", "键盘", 90, T0));

        Assert.Equal(0, store.DeleteDevices(Array.Empty<string>()));
        Assert.Equal(1, store.GetStats().TotalCount);
    }

    [Fact]
    public void H8_删不存在的设备返回零()
    {
        using var store = NewStore();
        store.Insert(Dev("kb", "键盘", 90, T0));

        Assert.Equal(0, store.DeleteDevices(new[] { "nope" }));
        Assert.Equal(1, store.GetStats().TotalCount);
    }

    [Fact]
    public void H9_VACUUM收缩后剩余数据完好()
    {
        using var store = NewStore();
        // 行数刻意压小：Insert 是逐条自动提交（每条一次 fsync），几百条会让整个测试套件从 20ms 涨到几十秒
        for (int i = 0; i < 30; i++) store.Insert(Dev("kb", "键盘", 100 - i % 50, T0.AddMinutes(i)));
        store.Insert(Dev("mouse", "鼠标", 42, T0.AddMinutes(300)));

        store.DeleteDevices(new[] { "kb" });
        store.Compact();

        Assert.Equal(1, store.GetStats().TotalCount);
        var remaining = store.Query("mouse", T0.AddDays(-1), T0.AddDays(1));
        Assert.Single(remaining);
        Assert.Equal(42, remaining[0].Percent);
    }

    [Fact]
    public void H10_全清并VACUUM后文件不膨胀()
    {
        using var store = NewStore();
        for (int i = 0; i < 60; i++) store.Insert(Dev("kb", "键盘", 100 - i % 50, T0.AddMinutes(i)));
        long before = store.FileBytes();

        store.DeleteDevices(new[] { "kb" });
        store.Compact();

        long after = store.FileBytes();
        Assert.Equal(0, store.GetStats().TotalCount);
        Assert.True(after <= before, $"VACUUM 后 {after} 字节应不大于删除前 {before} 字节");
    }

    [Fact]
    public void H11_聚合名取最新非占位名_不取字典序MAX()
    {
        using var store = NewStore();
        const string id = "ble:BluetoothLE#BluetoothLEc8:cb:9e:d2:19:b8-c6:b8:c5:38:c4:a2";
        // 真实名在前；更晚写入的占位 Id 字典序更大（b > D），旧 MAX(name) 会选错
        store.Insert(Dev(id, "Dell WK717Keyboard", 80, T0));
        store.Insert(Dev(id, id, 75, T0.AddHours(1)));
        store.Insert(Dev(id, "Dell WK717Keyboard", 70, T0.AddHours(2)));

        var stats = store.GetDeviceStats().Single();
        Assert.Equal("Dell WK717Keyboard", stats.Name);

        var distinct = store.DistinctDevices(T0.AddDays(-1)).Single();
        Assert.Equal("Dell WK717Keyboard", distinct.Name);
    }

    [Fact]
    public void H12_两次真实改名取时间上更晚的()
    {
        using var store = NewStore();
        store.Insert(Dev("mouse", "旧名", 90, T0));
        store.Insert(Dev("mouse", "新名", 85, T0.AddHours(3)));

        Assert.Equal("新名", store.GetDeviceStats().Single().Name);
        Assert.Equal("新名", store.DistinctDevices(T0).Single().Name);
    }

    [Fact]
    public void H13_单台概况只统计该设备的条数与时间范围()
    {
        using var store = NewStore();
        store.Insert(Dev("kb", "键盘", 90, T0));
        store.Insert(Dev("kb", "键盘", 88, T0.AddHours(2)));
        store.Insert(Dev("mouse", "鼠标", 50, T0.AddHours(5)));

        var kb = store.GetDeviceSummary("kb");
        Assert.Equal(2, kb.Count);
        Assert.Equal(T0, kb.First);
        Assert.Equal(T0.AddHours(2), kb.Last);

        // 删除确认框靠这三个值写文案：没有记录时必须给 0 和两个 null，不能抛
        var none = store.GetDeviceSummary("nope");
        Assert.Equal(0, none.Count);
        Assert.Null(none.First);
        Assert.Null(none.Last);
    }

    [Fact]
    public void H14_读回的时间戳带本地时区_直接格式化不会显示成UTC()
    {
        using var store = NewStore();
        store.Insert(Dev("kb", "键盘", 90, T0));
        var local = T0.ToLocalTime();

        var sample = store.Query("kb", T0.AddDays(-1), T0.AddDays(1)).Single();
        var deviceStat = store.GetDeviceStats().Single();
        var summary = store.GetDeviceSummary("kb");
        var distinct = store.DistinctDevices(T0.AddDays(-1)).Single();

        // 设备列表的「更新于」直接格式化 LastUpdated，偏移是 UTC 就会比本地时间少 8 小时
        foreach (var actual in new[]
                 {
                     sample.Timestamp, deviceStat.First, deviceStat.Last,
                     summary.First!.Value, summary.Last!.Value, distinct.LastSeen,
                 })
        {
            Assert.Equal(local.Offset, actual.Offset);
            Assert.Equal(local.ToString("HH:mm:ss"), actual.ToString("HH:mm:ss"));
        }
    }

    /// <summary>流式扫描与「Query + Evaluate」必须等价：它是估算的主路径，两条路径靠这条用例对拍（CR-79）。</summary>
    [Fact]
    public void H15_流式扫描与整表评估给出同一结果()
    {
        using var store = NewStore();
        const string id = "hid:dev1";
        store.Insert(Dev(id, "dev1", 100, T0));
        store.Insert(Dev(id, "dev1", 96, T0.AddMinutes(10)));
        store.Insert(Dev(id, "dev1", 95, T0.AddMinutes(20)));
        store.Insert(Dev(id, "dev1", 94, T0.AddMinutes(20)));                    // 同 ts：后写的那条赢
        store.Insert(Dev(id, "dev1", 90, T0.AddMinutes(30), charging: true));
        store.Insert(Dev(id, "dev1", 92, T0.AddMinutes(40)));
        store.Insert(Dev(id, "dev1", 90, T0.AddMinutes(50)));
        store.Insert(Dev(id, "dev1", 88, T0.AddMinutes(60)));

        var from = T0.AddHours(-1);
        var to = T0.AddHours(2);
        var samples = store.Query(id, from, to);
        var scan = store.ScanDischarge(id, from, to);

        Assert.Equal(7, scan.Total);            // 8 行、7 个时间点
        Assert.True(scan.HadCharge);
        Assert.Equal(3, scan.Discharge);        // 只看最后一次充电之后的 92 / 90 / 88
        Assert.Equal(92, scan.FirstPercent);
        Assert.Equal(88, scan.LastPercent);
        Assert.Equal(T0.AddMinutes(40), scan.First);
        Assert.Equal(T0.AddMinutes(60), scan.Last);

        var expected = RemainingTimeEstimator.Evaluate(samples, 88, T0.AddMinutes(65));
        var actual = RemainingTimeEstimator.Evaluate(scan, 88);
        Assert.Equal(expected, actual);

        var none = store.ScanDischarge("不存在的设备", from, to);
        Assert.Equal(0, none.Total);
        Assert.Equal(0, none.Discharge);
    }

    /// <summary>版本号是估算缓存的唯一失效依据：漏推会让"历史变了却不重算"，多推只是白算一次。</summary>
    [Fact]
    public void H16_历史版本号只在写库与清理后变化()
    {
        using var store = NewStore();
        long empty = store.VersionOf("a");

        store.Insert(Dev("a", "A", 80, T0));
        long afterInsert = store.VersionOf("a");
        Assert.NotEqual(empty, afterInsert);
        Assert.Equal(afterInsert, store.VersionOf("a"));    // 没写就不变

        store.Insert(Dev("b", "B", 70, T0));
        Assert.Equal(afterInsert, store.VersionOf("a"));    // 别的设备写入不影响 a

        store.CleanupOlderThan(T0.AddDays(1));              // 全删：epoch 前进
        Assert.NotEqual(afterInsert, store.VersionOf("a"));
    }
}
