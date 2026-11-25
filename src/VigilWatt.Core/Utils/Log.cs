using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace VigilWatt.Core.Utils;

/// <summary>
/// 功 能： 极简文件日志，按**本地日期**每天一个文件写到 %LOCALAPPDATA%\VigilWatt\logs\app-yyyy-MM-dd.log。
/// 2026/10/06 15:53:36 xliu
/// **格式契约**（行首本地时间、标签宽 6、一行一事件、UTF-8 无 BOM、首行时间基准）见 docs/dev/logging.md。
/// </summary>
public static class Log
{
    private static readonly object Gate = new();

    /// <summary>无 BOM 的 UTF-8 —— 格式契约的一部分，别改成 <c>Encoding.UTF8</c>（它会带 BOM）。</summary>
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>异常堆栈后续行的缩进前缀，保证 <c>grep "^20"</c> 只命中真正的日志行。</summary>
    private const string ContinuationPrefix = "        | ";

    private const string ErrorFileName = "log-error.txt";

    /// <summary>按日文件名前后缀：<c>app-yyyy-MM-dd.log</c>（本地日期）。</summary>
    private const string FileNamePrefix = "app-";
    private const string FileNameSuffix = ".log";

    /// <summary>历史日志保留天数，超期文件在启动/跨天时删除；0 或负数 = 不清理。启动时由 AppSettings.LogRetentionDays 赋值，**重启生效**。</summary>
    public static int RetentionDays { get; set; } = 14;

    /// <summary>兜底文件上限，超过就截断重来，避免兜底通道自己涨爆。</summary>
    private const long MaxErrorBytes = 64 * 1024;

    /// <summary>连续写失败达到该次数才开始写兜底文件。</summary>
    private const int FailureThreshold = 3;

    /// <summary>打开失败后的退避时间：期间不再重试，避免每行日志都重跑一遍系统调用。</summary>
    private static readonly TimeSpan RetryBackoff = TimeSpan.FromSeconds(30);

    /// <summary>兜底文件的写入节流间隔。</summary>
    private static readonly TimeSpan FallbackThrottle = TimeSpan.FromSeconds(30);

    private static string _logDir = Path.Combine(AppPaths.RootDir, "logs");

    private static StreamWriter? _writer;

    /// <summary>当前 writer 对应的本地日期；与写入日期不符即换文件（跨天）。</summary>
    private static DateTime _currentDate = DateTime.MinValue;

    private static int _consecutiveFailures;
    private static DateTime _nextRetryUtc = DateTime.MinValue;
    private static DateTime _lastFallbackUtc = DateTime.MinValue;
    private static bool _disabled;   // 连兜底都写不进去 → 彻底放弃，不递归

    /// <summary>最低输出级别（默认 <see cref="LogLevel.Info"/>）。启动时由 AppSettings.LogLevel 赋值；**重启生效**，不追求热更新（避免跨线程读静态字段）。</summary>
    public static LogLevel MinLevel { get; set; } = LogLevel.Info;

    /// <summary>热路径短路用：低于当前级别的调用可以跳过 <c>$"..."</c> 插值开销。</summary>
    public static bool IsEnabled(LogLevel level) => level >= MinLevel;

    public static void Debug(string message) => Write(LogLevel.Debug, "DBG ", message);
    public static void Info(string message) => Write(LogLevel.Info, "INFO", message);
    public static void Warn(string message) => Write(LogLevel.Warn, "WARN", message);

    // ---------- 降噪：状态变化才记 / 节流 ----------
    // 轮询型代码（每 15 秒 / 每分钟重跑）常在同一状态上重复打日志把文件冲爆。
    // 三个入口把"什么时候才值得记"从各调用点收拢到这里：
    //   - InfoOnChange：状态变了才记（托盘提示文字、HID 设备集合指纹）
    //   - InfoThrottled：同一 key 在给定间隔内最多记一条
    //   - Once / ResetOnce：同一 key 只记一条，直到显式复位（"设备睡着导致探测反复失败"这类
    //     "信息量只有一次、但会随节拍无限重复"的事件）
    // 见 docs/dev/logging.md §4。

    private static readonly ConcurrentDictionary<string, string> InfoStateByKey = new();
    private static readonly ConcurrentDictionary<string, long> ThrottleTicksByKey = new();

    /// <summary><see cref="Once"/> 在状态表里占位用的前缀，与 <see cref="InfoOnChange"/> 的状态值隔离。</summary>
    private const string OnceKeyPrefix = "once|";

    /// <summary>
    /// 只有 <paramref name="state"/> 与上次不同时才打一条 Info，并返回"是否发生变化"（调用点据此决定是否顺带 dump 一批 Debug 明细，HidDeviceScanner 就这么用）。
    /// 当前级别高于 Info 时**不更新状态**（用户切回 Info 档仍能看到最新状态）；并发下最坏多打一条——去重非正确性关键，不值得为它上锁。
    /// </summary>
    public static bool InfoOnChange(string key, string state, string message)
    {
        if (!IsEnabled(LogLevel.Info)) return false;
        if (InfoStateByKey.TryGetValue(key, out var prev) && prev == state) return false;
        InfoStateByKey[key] = state;
        Info(message);
        return true;
    }

    /// <summary><paramref name="message"/> 自身即状态：变了才打，并返回是否变化。</summary>
    public static bool InfoOnChange(string key, string message) => InfoOnChange(key, message, message);

    /// <summary>
    /// 同一 <paramref name="key"/> 只记一条（按 <paramref name="level"/> 判启用），直到 <see cref="ResetOnce"/> 复位。
    /// 返回本次是否真的记了。用于"同一条路径的设备睡着 → 探测反复失败"这类会随发现节拍无限重复的事件。
    /// </summary>
    public static bool Once(LogLevel level, string key, string message)
    {
        if (!IsEnabled(level)) return false;
        if (!InfoStateByKey.TryAdd(OnceKeyPrefix + key, "")) return false;
        Write(level, TagOf(level), message);
        return true;
    }

    /// <summary>复位 <see cref="Once"/> 的"记过"标记，让下次再发生时重新记一条（"状态变化才记"的另一半）。</summary>
    public static void ResetOnce(string key) => InfoStateByKey.TryRemove(OnceKeyPrefix + key, out _);

    /// <summary>级别 → 行首标签。与 <see cref="Debug"/> / <see cref="Info"/> 等入口保持同一张表。</summary>
    private static string TagOf(LogLevel level) => level switch
    {
        LogLevel.Debug => "DBG ",
        LogLevel.Warn => "WARN",
        LogLevel.Error => "ERR ",
        _ => "INFO",
    };

    /// <summary>同一 <paramref name="key"/> 在 <paramref name="interval"/> 内最多打一条 Info。</summary>
    public static void InfoThrottled(string key, string message, TimeSpan interval)
        => Throttled(LogLevel.Info, key, message, interval);

    /// <summary>同一 <paramref name="key"/> 在 <paramref name="interval"/> 内最多打一条 Warn（推流型设备的重复告警用）。</summary>
    public static void WarnThrottled(string key, string message, TimeSpan interval)
        => Throttled(LogLevel.Warn, key, message, interval);

    private static void Throttled(LogLevel level, string key, string message, TimeSpan interval)
    {
        if (!IsEnabled(level)) return;
        var now = DateTime.UtcNow.Ticks;
        // 级别进 key：同一 key 的 Info 与 Warn 互不压制。
        string slot = $"{(int)level}|{key}";
        if (ThrottleTicksByKey.TryGetValue(slot, out var last) && now - last < interval.Ticks) return;
        ThrottleTicksByKey[slot] = now;
        Write(level, TagOf(level), message);
    }

    /// <summary>错误日志。带异常时把堆栈拆成多行、每行加缩进前缀，保证"一行一条"不被破坏。</summary>
    public static void Error(string message, Exception? ex = null)
    {
        if (!IsEnabled(LogLevel.Error)) return;
        if (ex is null)
        {
            Emit("ERR ", Sanitize(message));
            return;
        }

        var sb = new StringBuilder();
        sb.Append(Sanitize(message)).Append(" :: ");
        var lines = ex.ToString().Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        sb.Append(lines[0]);
        for (int i = 1; i < lines.Length; i++)
            sb.Append(Environment.NewLine).Append(ContinuationPrefix).Append(lines[i]);
        Emit("ERR ", sb.ToString());
    }

    /// <summary>把字节数组格式化成十六进制，协议探测时用。</summary>
    public static string Hex(ReadOnlySpan<byte> bytes)
    {
        var sb = new StringBuilder(bytes.Length * 3);
        foreach (var b in bytes) sb.Append(b.ToString("X2")).Append(' ');
        return sb.ToString().TrimEnd();
    }

    /// <summary>把日志重定向到指定目录，并立即释放旧文件句柄、复位失败状态与去重表。
    /// 单测用：不重定向的话测试会往真实的 %LOCALAPPDATA% 日志目录里写脏数据。</summary>
    public static void RedirectTo(string logDir)
    {
        lock (Gate)
        {
            CloseWriterQuietly();
            _currentDate = DateTime.MinValue;
            _consecutiveFailures = 0;
            _nextRetryUtc = DateTime.MinValue;
            _lastFallbackUtc = DateTime.MinValue;
            _disabled = false;
            _logDir = logDir;
            InfoStateByKey.Clear();
            ThrottleTicksByKey.Clear();
        }
    }

    /// <summary>退出前收尾：刷盘并释放句柄。挂在**真实退出路径**上（见 TrayService.Exit）。</summary>
    public static void Shutdown()
    {
        lock (Gate)
        {
            try { _writer?.Flush(); } catch { /* 忽略 */ }
            CloseWriterQuietly();
        }
    }

    /// <summary>
    /// 启动自检：试开当天的日志文件，失败时把**真实原因**交出来。
    /// <see cref="Emit"/> 会把写入失败全部吞掉（这是对的，日志不能反过来搞挂应用），
    /// 代价是用户那边只看到"一行日志都没有"，无从判断是没启动还是写不进去。启动时探一次即可区分。
    /// </summary>
    public static bool TryProbeWriter(out string error)
    {
        lock (Gate)
        {
            error = "";
            try
            {
                Directory.CreateDirectory(_logDir);
                var path = Path.Combine(_logDir, FileNameFor(DateTime.Now.Date));
                using var probe = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                return true;
            }
            catch (Exception ex)
            {
                error = $"{_logDir}{Environment.NewLine}{ex.GetType().Name}: {ex.Message}";
                return false;
            }
        }
    }

    private static void Write(LogLevel level, string tag, string message)
    {
        if (level < MinLevel) return;
        Emit(tag, Sanitize(message));
    }

    /// <summary>保证"一个逻辑事件 = 一行"：把换行/回车/控制符转义成可见形式，避免设备名等外部字符串（HID 产品字符串/BLE 名称）伪造出无时间戳的假日志行。
    /// 绝大多数消息不含控制符，走快路径零分配；含控制符才逐字符转义。</summary>
    private static string Sanitize(string message)
    {
        if (!HasControlChar(message)) return message;

        var sb = new StringBuilder(message.Length + 8);
        for (int i = 0; i < message.Length; i++)
        {
            char c = message[i];
            switch (c)
            {
                case '\r':
                    // CRLF 视作一个换行；孤立的 CR 也按换行处理（同样是行分隔语义）
                    if (i + 1 < message.Length && message[i + 1] == '\n') i++;
                    sb.Append("\\n");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\t':
                    sb.Append(c);   // 制表符不破坏"一行"，原样保留
                    break;
                default:
                    // 只有真正的控制符才转义；普通字符（含中文）必须原样保留
                    if (c < ' ' || c == '\u007F')
                        sb.Append("\\x").Append(((int)c).ToString("X2"));
                    else
                        sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    private static bool HasControlChar(string s)
    {
        foreach (var c in s)
            if (c < ' ' || c == '\u007F') return true;
        return false;
    }

    private static void Emit(string tag, string message)
    {
        if (_disabled) return;

        var now = DateTime.Now;
        var line = $"{now:yyyy-MM-dd HH:mm:ss.fff} [{tag}] {message}{Environment.NewLine}";
        lock (Gate)
        {
            if (!EnsureWriter(now.Date)) { OnFailure(); return; }

            try
            {
                _writer!.Write(line);
                _consecutiveFailures = 0;
            }
            catch
            {
                CloseWriterQuietly();
                _nextRetryUtc = DateTime.UtcNow + RetryBackoff;
                OnFailure();
            }
        }
    }

    /// <summary>确保 writer 对应 <paramref name="today"/> 的按日文件；首次写入或跨天时切换。
    /// 返回 false 表示本行丢弃（退避中，或刚失败并已记兜底）。</summary>
    private static bool EnsureWriter(DateTime today)
    {
        if (_writer != null && _currentDate == today) return true;
        if (_writer != null) CloseWriterQuietly();   // 跨天：先放下昨天的句柄，再开今天的新文件
        if (_disabled) return false;
        if (DateTime.UtcNow < _nextRetryUtc) return false;   // 退避中：不要每行都重跑系统调用

        try
        {
            Directory.CreateDirectory(_logDir);
            CleanupOldFiles(today);

            var path = Path.Combine(_logDir, FileNameFor(today));
            bool isNew = !File.Exists(path) || new FileInfo(path).Length == 0;
            _writer = new StreamWriter(
                new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
                Utf8NoBom)
            {
                AutoFlush = true,
            };
            _currentDate = today;
            if (isNew) WriteTimeBasis();
            _consecutiveFailures = 0;
            _nextRetryUtc = DateTime.MinValue;
            return true;
        }
        catch
        {
            CloseWriterQuietly();
            _nextRetryUtc = DateTime.UtcNow + RetryBackoff;
            return false;   // 由调用方记一次 OnFailure（退避中被丢弃的行也会计数）
        }
    }

    /// <summary>按日文件名：<c>app-yyyy-MM-dd.log</c>（用本地日期，与行首时间戳同一时区）。</summary>
    private static string FileNameFor(DateTime date) => $"{FileNamePrefix}{date:yyyy-MM-dd}{FileNameSuffix}";

    /// <summary>删除超过保留天数的 <c>app-yyyy-MM-dd.log</c>；旧版单文件 <c>app.log</c>/<c>app.N.log</c> 已不再使用，一并清掉。
    /// 仅在打开新文件时调用（启动/跨天），不在每行写入时跑。</summary>
    private static void CleanupOldFiles(DateTime today)
    {
        try
        {
            var cutoff = today.AddDays(-RetentionDays);
            foreach (var file in Directory.EnumerateFiles(_logDir, "app*.log"))
            {
                // 无法解析出日期的（旧版 app.log / app.N.log）也删——新版不会再写它们，留着只会让人以为日志没更新
                bool keep = TryParseDate(Path.GetFileName(file), out var date)
                            && (RetentionDays <= 0 || date.Date > cutoff);
                if (!keep) TryDelete(file);
            }
        }
        catch { /* 清理失败不影响正常写入 */ }
    }

    /// <summary>从 <c>app-yyyy-MM-dd.log</c> 解析日期；旧版 <c>app.log</c>/<c>app.N.log</c> 返回 false。</summary>
    private static bool TryParseDate(string fileName, out DateTime date)
    {
        date = default;
        if (fileName.Length <= FileNamePrefix.Length + FileNameSuffix.Length) return false;
        if (!fileName.StartsWith(FileNamePrefix, StringComparison.OrdinalIgnoreCase)) return false;
        if (!fileName.EndsWith(FileNameSuffix, StringComparison.OrdinalIgnoreCase)) return false;
        var mid = fileName[FileNamePrefix.Length..^FileNameSuffix.Length];
        return DateTime.TryParseExact(mid, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* 被占用或权限不足，下次再清 */ }
    }

    /// <summary>每个日志文件新建时写一行"时间基准"：声明行首是**本地时间**、附上本机时区与 UTC offset，并说明 history.db 的 ts 是 UTC 毫秒（N4）。
    /// 该行带完整时间戳前缀，不破坏 grep -a "^20" 之类的既有考古命令；仅在文件为空时出现一次。</summary>
    private static void WriteTimeBasis()
    {
        var offset = TimeZoneInfo.Local.GetUtcOffset(DateTime.Now);
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var text = $"本地时间 {TimeZoneInfo.Local.Id} (UTC{sign}{offset.Duration():hh\\:mm})；history.db 的 ts 为 UTC 毫秒";
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [INFO] 时间基准: {text}{Environment.NewLine}";
        _writer!.Write(line);
    }

    /// <summary>写失败时记一笔；连续失败达阈值就写兜底文件（带节流 + 上限）。兜底自身失败则彻底放弃。</summary>
    private static void OnFailure()
    {
        _consecutiveFailures++;
        if (_disabled || _consecutiveFailures < FailureThreshold) return;

        var now = DateTime.UtcNow;
        if (now - _lastFallbackUtc < FallbackThrottle) return;
        _lastFallbackUtc = now;

        try
        {
            Directory.CreateDirectory(_logDir);
            var path = Path.Combine(_logDir, ErrorFileName);
            if (File.Exists(path) && new FileInfo(path).Length > MaxErrorBytes)
                File.Delete(path);
            File.AppendAllText(
                path,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 连续 {_consecutiveFailures} 次写入日志失败，日志暂不可用{Environment.NewLine}",
                Utf8NoBom);
        }
        catch
        {
            _disabled = true;   // 兜底也失败 → 彻底放弃，不递归、不再刷
        }
    }

    private static void CloseWriterQuietly()
    {
        try { _writer?.Dispose(); } catch { /* 忽略 */ }
        _writer = null;
    }
}
