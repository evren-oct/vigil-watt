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
    public void H14_归并设备身份保留每条采样并可重复执行()
    {
        using var store = NewStore();
        store.Insert(Dev("wireless", "鼠标", 96, T0));
        store.Insert(Dev("usb:1", "鼠标", 98, T0.AddMinutes(1), true));
        store.Insert(Dev("usb:6", "鼠标", 100, T0.AddMinutes(2), true));
        var original = store.GetStats();
        var aliases = new Dictionary<string, string>
        {
            ["wireless"] = "physical", ["usb:1"] = "physical", ["usb:6"] = "physical",
        };
        Assert.Equal(3, store.ReassignDeviceIds(aliases));
        Assert.Equal(0, store.ReassignDeviceIds(aliases));
        Assert.Equal(original.TotalCount, store.GetStats().TotalCount);
        Assert.Equal(1, store.GetStats().DeviceCount);
        var samples = store.Query("physical", T0, T0.AddHours(1));
        Assert.Equal(new[] { 96, 98, 100 }, samples.Select(s => s.Percent));
        Assert.Equal(new[] { T0, T0.AddMinutes(1), T0.AddMinutes(2) }, samples.Select(s => s.Timestamp));
        Assert.True(samples[1].Charging);
    }

    [Fact]
    public void H15_归并异常回滚整批更新()
    {
        using var store = NewStore();
        store.Insert(Dev("a", "鼠标", 80, T0));
        store.Insert(Dev("b", "鼠标", 90, T0));
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TRIGGER fail_merge BEFORE UPDATE ON samples WHEN OLD.device_id = 'b' BEGIN SELECT RAISE(ABORT, 'test rollback'); END";
            command.ExecuteNonQuery();
        }
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => store.ReassignDeviceIds(
            new Dictionary<string, string> { ["a"] = "physical", ["b"] = "physical" }));
        Assert.Equal(new[] { "a", "b" }, store.GetDeviceStats().Select(s => s.DeviceId).OrderBy(x => x));
        Assert.Equal(2, store.GetStats().TotalCount);
    }

    [Fact]
    public void H16_迁移后旧引用写入和查询也使用新身份()
    {
        using var store = NewStore();
        store.DeviceIdResolver = id => id == "old" ? "physical" : id;
        store.Insert(Dev("old", "鼠标", 80, T0));
        Assert.Equal("physical", store.GetDeviceStats().Single().DeviceId);
        Assert.Single(store.Query("old", T0, T0.AddHours(1)));
        Assert.Equal(1, store.GetDeviceSummary("old").Count);
        Assert.Equal(1, store.DeleteDevices(new[] { "old" }));
        Assert.Empty(store.GetDeviceStats());
    }
}
