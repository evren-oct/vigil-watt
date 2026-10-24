using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;
using Microsoft.Data.Sqlite;

namespace VigilWatt.Core.Storage;

public record HistorySample(long Id, string DeviceId, string Name, DeviceKind Kind, int Percent, bool Charging, DateTimeOffset Timestamp);

/// <summary>单台设备的历史聚合（历史管理页列表用）。</summary>
public record DeviceHistoryStat(string DeviceId, string Name, DeviceKind Kind, int Count, DateTimeOffset First, DateTimeOffset Last);

/// <summary>整库概览（历史管理页顶部卡片用）。空库时 First/Last 为 null。</summary>
public record HistoryStats(int TotalCount, int DeviceCount, DateTimeOffset? First, DateTimeOffset? Last, long FileBytes);

/// <summary>单台设备的历史概况（删除设备确认框用）。没有记录时 First/Last 为 null。</summary>
public record DeviceHistorySummary(int Count, DateTimeOffset? First, DateTimeOffset? Last);

/// <summary>
/// 功 能： 电量历史 SQLite 存储。
/// 2026/10/06 15:53:36 xliu
/// 库文件默认在 %LOCALAPPDATA%\VigilWatt\history.db。
/// </summary>
public class HistoryStore : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly object _writeGate = new();
    private readonly string _dbPath;

    /// <param name="dbPath">库文件路径；null = 默认 %LOCALAPPDATA%\VigilWatt\history.db。
    /// 显式传入供单测用临时库，避免碰真实数据。</param>
    public HistoryStore(string? dbPath = null)
    {
        _dbPath = dbPath ?? Path.Combine(AppPaths.RootDir, "history.db");
        var dir = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        _connection = new SqliteConnection($"Data Source={_dbPath}");
        _connection.Open();
        EnsureSchema();
        Log.Info($"history.db 就绪: {_dbPath}");
    }

    /// <summary>库里的 UTC 毫秒 → 本地时区。FromUnixTimeMilliseconds 给的是 +00:00 偏移，
    /// UI 直接按偏移格式化就会显示成 UTC（本地时间少 8 小时），见 docs/dev/runtime-pitfalls.md。</summary>
    private static DateTimeOffset LocalTime(long unixMs) => DateTimeOffset.FromUnixTimeMilliseconds(unixMs).ToLocalTime();

    private void EnsureSchema()
    {
        // WAL：读不再阻塞写（UI 查询与后台采样互不卡）。busy_timeout：偶发写冲突时等一会儿而不是直接报错。
        // synchronous=NORMAL 是 WAL 下的常规折中（崩溃最多丢最近一条采样，不损坏库）。
        using (var pragma = _connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000; PRAGMA synchronous=NORMAL;";
            pragma.ExecuteNonQuery();
        }

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS samples (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                device_id TEXT NOT NULL,
                name TEXT NOT NULL,
                kind INTEGER NOT NULL DEFAULT 0,
                percent INTEGER NOT NULL,
                charging INTEGER NOT NULL,
                ts INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_samples_device_ts ON samples(device_id, ts);
            """;
        cmd.ExecuteNonQuery();

        // 旧版 history.db 没有设备类型列；新增列默认 Unknown，保留已有采样数据。
        using var columns = _connection.CreateCommand();
        columns.CommandText = "PRAGMA table_info(samples)";
        bool hasKind = false;
        using (var reader = columns.ExecuteReader())
            while (reader.Read())
                if (string.Equals(reader.GetString(1), "kind", StringComparison.OrdinalIgnoreCase))
                    hasKind = true;
        if (!hasKind)
        {
            using var migrate = _connection.CreateCommand();
            migrate.CommandText = "ALTER TABLE samples ADD COLUMN kind INTEGER NOT NULL DEFAULT 0";
            migrate.ExecuteNonQuery();
        }
    }

    public void Insert(DeviceStatus device)
    {
        if (device.BatteryPercent is not { } percent) return;
        lock (_writeGate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText =
                "INSERT INTO samples(device_id, name, kind, percent, charging, ts) VALUES($id, $name, $kind, $p, $c, $ts)";
            cmd.Parameters.AddWithValue("$id", device.Id);
            cmd.Parameters.AddWithValue("$name", device.DisplayName);
            cmd.Parameters.AddWithValue("$kind", (int)device.Kind);
            cmd.Parameters.AddWithValue("$p", percent);
            cmd.Parameters.AddWithValue("$c", DeviceStatusFormatter.IsChargingOrFull(device.Charging) ? 1 : 0);
            cmd.Parameters.AddWithValue("$ts", device.LastUpdated.ToUnixTimeMilliseconds());
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>取时间范围内的采样，用于画曲线。</summary>
    public List<HistorySample> Query(string deviceId, DateTimeOffset from, DateTimeOffset to)
    {
        var list = new List<HistorySample>();
        lock (_writeGate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText =
                "SELECT id, device_id, name, kind, percent, charging, ts FROM samples " +
                "WHERE device_id = $id AND ts BETWEEN $from AND $to ORDER BY ts";
            cmd.Parameters.AddWithValue("$id", deviceId);
            cmd.Parameters.AddWithValue("$from", from.ToUnixTimeMilliseconds());
            cmd.Parameters.AddWithValue("$to", to.ToUnixTimeMilliseconds());
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new HistorySample(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    (DeviceKind)reader.GetInt32(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5) == 1,
                    LocalTime(reader.GetInt64(6))));
            }
        }
        return list;
    }

    /// <summary>时间范围内有过采样的设备（历史页设备列表用：按库里的数据走，而不是当前在线设备）。
    /// 排序 = 最近有数据的在前。名称取窗口内最新非占位名（见 <see cref="PreferredNameSql"/>）。</summary>
    public List<(string DeviceId, string Name, DateTimeOffset LastSeen)> DistinctDevices(DateTimeOffset since)
    {
        var list = new List<(string, string, DateTimeOffset)>();
        lock (_writeGate)
        {
            using var cmd = _connection.CreateCommand();
            // MAX(name) 会把 ble:BluetoothLE#… 排到 Dell… 前面；窗口内优先非占位、再取最新。
            cmd.CommandText = $"""
                SELECT device_id, name, last_ts FROM (
                    SELECT device_id, name,
                           MAX(ts) OVER (PARTITION BY device_id) AS last_ts,
                           ROW_NUMBER() OVER (
                               PARTITION BY device_id
                               ORDER BY {PreferredNameSql} ASC, ts DESC, id DESC
                           ) AS rn
                    FROM samples
                    WHERE ts >= $since
                ) WHERE rn = 1
                ORDER BY last_ts DESC
                """;
            cmd.Parameters.AddWithValue("$since", since.ToUnixTimeMilliseconds());
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                list.Add((reader.GetString(0), reader.GetString(1),
                    LocalTime(reader.GetInt64(2))));
        }
        return list;
    }

    /// <summary>整库概览：总条数 / 设备数 / 最早 / 最新 / 库文件字节数。</summary>
    public HistoryStats GetStats()
    {
        lock (_writeGate)
        {
            int total = 0, devices = 0;
            long? first = null, last = null;
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*), COUNT(DISTINCT device_id), MIN(ts), MAX(ts) FROM samples";
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    total = reader.GetInt32(0);
                    devices = reader.GetInt32(1);
                    if (!reader.IsDBNull(2)) first = reader.GetInt64(2);
                    if (!reader.IsDBNull(3)) last = reader.GetInt64(3);
                }
            }
            return new HistoryStats(
                total, devices,
                first is { } f ? LocalTime(f) : null,
                last is { } l ? LocalTime(l) : null,
                FileBytes());
        }
    }

    /// <summary>每台设备的聚合（条数 / 名称 / 最早 / 最新），最近有数据的在前。
    /// 名称取最新非占位名（见 <see cref="PreferredNameSql"/>）。</summary>
    public List<DeviceHistoryStat> GetDeviceStats()
    {
        var list = new List<DeviceHistoryStat>();
        lock (_writeGate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"""
                SELECT device_id, name, kind, cnt, first_ts, last_ts FROM (
                    SELECT device_id, name, kind,
                           COUNT(*) OVER (PARTITION BY device_id) AS cnt,
                           MIN(ts) OVER (PARTITION BY device_id) AS first_ts,
                           MAX(ts) OVER (PARTITION BY device_id) AS last_ts,
                           ROW_NUMBER() OVER (
                               PARTITION BY device_id
                               ORDER BY {PreferredNameSql} ASC, ts DESC, id DESC
                           ) AS rn
                    FROM samples
                ) WHERE rn = 1
                ORDER BY last_ts DESC
                """;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                list.Add(new DeviceHistoryStat(
                    reader.GetString(0), reader.GetString(1), (DeviceKind)reader.GetInt32(2), reader.GetInt32(3),
                    LocalTime(reader.GetInt64(4)),
                    LocalTime(reader.GetInt64(5))));
        }
        return list;
    }

    /// <summary>ORDER BY 用：0 = 可显示名，1 = 占位（空 / 等于 device_id / 含 Bluetooth#）。全是占位时仍取最新一条。</summary>
    private const string PreferredNameSql =
        "CASE WHEN name = '' OR name = device_id OR INSTR(name, 'Bluetooth#') > 0 THEN 1 ELSE 0 END";

    /// <summary>单台设备的记录条数与时间范围。走 (device_id, ts) 索引；别用 <see cref="GetDeviceStats"/>，那是全表窗口函数。</summary>
    public DeviceHistorySummary GetDeviceSummary(string deviceId)
    {
        lock (_writeGate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*), MIN(ts), MAX(ts) FROM samples WHERE device_id = $id";
            cmd.Parameters.AddWithValue("$id", deviceId);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return new DeviceHistorySummary(0, null, null);
            return new DeviceHistorySummary(
                reader.GetInt32(0),
                reader.IsDBNull(1) ? null : LocalTime(reader.GetInt64(1)),
                reader.IsDBNull(2) ? null : LocalTime(reader.GetInt64(2)));
        }
    }

    /// <summary>删除指定设备的全部历史，返回删除条数。空集合直接返回 0（不拼 SQL）。</summary>
    public int DeleteDevices(IReadOnlyCollection<string> deviceIds)
    {
        if (deviceIds.Count == 0) return 0;
        lock (_writeGate)
        {
            // IN 列表按数量生成占位符、值逐个绑定参数——绝不把设备 Id 拼进 SQL 文本
            var placeholders = deviceIds.Select((_, i) => $"$id{i}").ToArray();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"DELETE FROM samples WHERE device_id IN ({string.Join(",", placeholders)})";
            int i = 0;
            foreach (var id in deviceIds) cmd.Parameters.AddWithValue($"$id{i++}", id);
            return cmd.ExecuteNonQuery();
        }
    }

    public int CleanupOlderThan(DateTimeOffset cutoff)
    {
        lock (_writeGate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM samples WHERE ts < $cutoff";
            cmd.Parameters.AddWithValue("$cutoff", cutoff.ToUnixTimeMilliseconds());
            return cmd.ExecuteNonQuery();
        }
    }

    /// <summary>库文件当前字节数；文件不存在或读不到时返回 0。</summary>
    public long FileBytes()
    {
        try
        {
            var info = new FileInfo(_dbPath);
            return info.Exists ? info.Length : 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>VACUUM 收缩库文件。**DELETE 只把页标成空闲、不缩小文件**——删完不 VACUUM 用户看到大小没变，会以为删除没生效。
    /// VACUUM 不能在事务里跑，这里也没有活动事务，直接执行。</summary>
    public void Compact()
    {
        lock (_writeGate)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "VACUUM";
            cmd.ExecuteNonQuery();
        }
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
