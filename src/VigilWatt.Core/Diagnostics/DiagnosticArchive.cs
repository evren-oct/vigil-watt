using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace VigilWatt.Core.Diagnostics;

/// <summary>
/// 功 能： 将脱敏快照与有界日志切片导出为本地诊断包。
/// 2026/10/23 22:37:53 Administrator
/// 不调用设备 API；数据边界与包格式见 docs/dev/diagnostics.md。
/// </summary>
public static class DiagnosticArchive
{
    public const int MaxLogBytes = 1024 * 1024;
    public const int MaxArchiveContentBytes = 10 * 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static void Export(string destination, DiagnosticSnapshot snapshot, string logDirectory,
        IEnumerable<string> privateValues)
    {
        if (!string.Equals(Path.GetExtension(destination), ".zip", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("诊断包必须保存为 .zip 文件。", nameof(destination));
        var identifiers = snapshot.Devices.Select(d => d.Id).Concat(snapshot.HidInterfaces.Select(i => i.Path));
        var redactor = new DiagnosticRedactor(privateValues, identifiers);
        var devices = snapshot.Devices.Take(1000).Select(d => d with
        {
            Id = redactor.Pseudonym(d.Id), Name = redactor.Field(d.Name),
            Source = redactor.Field(d.Source), Protocol = redactor.Field(d.Protocol),
            Charging = redactor.Field(d.Charging),
        }).ToArray();
        var interfaces = snapshot.HidInterfaces.Take(1000).Select(i => i with
        {
            Path = redactor.Pseudonym(i.Path), Product = redactor.Field(i.Product),
            Manufacturer = redactor.Field(i.Manufacturer), VidPid = redactor.Field(i.VidPid),
            Owner = i.Owner == null ? null : redactor.Field(i.Owner),
        }).ToArray();
        var warnings = new List<string>();
        if (snapshot.Devices.Count > devices.Length || snapshot.HidInterfaces.Count > interfaces.Length)
            warnings.Add("设备或接口数量超过 1000，超出部分未导出。");
        var logs = redactor.Redact(ReadLogs(logDirectory, warnings));
        var logBytes = Utf8.GetBytes(logs);
        if (logBytes.Length > MaxLogBytes)
        {
            logs = Utf8.GetString(logBytes, logBytes.Length - MaxLogBytes, MaxLogBytes);
            int newline = logs.IndexOf('\n');
            logs = newline < 0 ? "" : logs[(newline + 1)..];
            warnings.Add("脱敏后的日志超过 1 MiB，仅保留末尾完整记录。");
        }
        var environment = new
        {
            snapshot.CapturedAt, AppVersion = redactor.Field(snapshot.AppVersion),
            OsVersion = redactor.Field(snapshot.OsVersion), Architecture = redactor.Field(snapshot.Architecture),
            TimeZone = redactor.Field(snapshot.TimeZone), ActiveLogLevel = redactor.Field(snapshot.ActiveLogLevel),
            ConfiguredLogLevel = redactor.Field(snapshot.ConfiguredLogLevel),
            Plugins = snapshot.Plugins.OrderBy(p => p.Key).Select(p => new { Name = redactor.Field(p.Key), Enabled = p.Value }),
            snapshot.HidCacheUpdatedAt, DevicesIncluded = devices.Length, InterfacesIncluded = interfaces.Length,
            Warnings = warnings,
        };
        var report = new StringBuilder()
            .AppendLine("# VigilWatt 诊断报告").AppendLine()
            .AppendLine($"采集时间：{snapshot.CapturedAt:O}")
            .AppendLine($"设备：{devices.Length} 台，在线 {devices.Count(d => d.IsOnline)} 台；HID 缓存：{interfaces.Length} 个接口。")
            .AppendLine($"HID 缓存更新时间：{snapshot.HidCacheUpdatedAt?.ToString("O") ?? "尚未扫描"}。")
            .AppendLine().AppendLine("## 数据边界").AppendLine()
            .AppendLine("- 导出仅读取现有内存状态、HID 元数据缓存、认领表与最近日志，不主动打开设备或发起 HID/GATT 请求。")
            .AppendLine("- 正常监控继续运行；本包未统计全进程硬件访问次数，不代表整个进程零写入。")
            .AppendLine("- 缓存与设备状态来自不同更新时刻；未出现的接口不等于系统不存在该设备，也不能据此断言没有电量通道。")
            .AppendLine("- 未导出历史数据库、完整配置、原始抓包或被动录帧。日志仅含最近两个按日文件的末尾切片，总计最多 1 MiB。")
            .AppendLine("- 设备 ID、接口路径使用每包独立匿名标识；已知用户名、计算机名、路径、MAC 与长报文被脱敏。产品名仍保留，请分享前自行检查。")
            .AppendLine("- 仅保存本地 ZIP，不自动上传。JSON 中 Owner 为空表示当前未认领，具体原因需结合插件开关和日志判断。")
            .AppendLine().AppendLine("## 采集提示").AppendLine();
        foreach (var warning in warnings) report.AppendLine("- " + warning);
        if (warnings.Count == 0) report.AppendLine("日志切片读取完成；当前日志级别以 environment.json 中 ActiveLogLevel 为准。");

        var fullPath = Path.GetFullPath(destination);
        var temporary = Path.Combine(Path.GetDirectoryName(fullPath)!, ".vigilwatt-diag-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using (var zip = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true))
                {
                    int remaining = MaxArchiveContentBytes;
                    WriteEntry(zip, "report.md", report.ToString(), ref remaining);
                    WriteEntry(zip, "environment.json", JsonSerializer.Serialize(environment, JsonOptions), ref remaining);
                    WriteEntry(zip, "devices.json", JsonSerializer.Serialize(devices, JsonOptions), ref remaining);
                    WriteEntry(zip, "hid-interfaces.json", JsonSerializer.Serialize(interfaces, JsonOptions), ref remaining);
                    WriteEntry(zip, "logs/recent.log", logs, ref remaining);
                }
                file.Flush(flushToDisk: true);
            }
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void WriteEntry(ZipArchive zip, string name, string content, ref int remaining)
    {
        var bytes = Utf8.GetBytes(content);
        if (bytes.Length > remaining) throw new InvalidOperationException("诊断内容超过 10 MiB，未覆盖目标文件。");
        remaining -= bytes.Length;
        using var stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        stream.Write(bytes);
    }

    private static string ReadLogs(string directory, List<string> warnings)
    {
        var result = new StringBuilder();
        try
        {
            if (!Directory.Exists(directory))
            {
                warnings.Add("日志目录不存在，未包含日志。");
                return "";
            }
            var files = Directory.EnumerateFiles(directory, "app-*.log")
                .Where(p => DateTime.TryParseExact(Path.GetFileNameWithoutExtension(p), "'app-'yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                .OrderByDescending(Path.GetFileName).Take(2).Reverse().ToArray();
            if (files.Length == 0) warnings.Add("没有可读取的按日日志。");
            foreach (var path in files)
            {
                try
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    int count = (int)Math.Min(stream.Length, MaxLogBytes / 2);
                    bool truncated = stream.Length > count;
                    stream.Seek(-count, SeekOrigin.End);
                    var bytes = new byte[count];
                    int read = 0;
                    while (read < count)
                    {
                        int n = stream.Read(bytes, read, count - read);
                        if (n == 0) break;
                        read += n;
                    }
                    var text = Utf8.GetString(bytes, 0, read);
                    if (truncated)
                    {
                        int newline = text.IndexOf('\n');
                        text = newline < 0 ? "" : text[(newline + 1)..];
                        warnings.Add("较长日志仅包含末尾切片，首条不完整记录已丢弃。");
                    }
                    result.AppendLine("--- " + Path.GetFileName(path) + " ---").AppendLine(text);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    warnings.Add($"部分日志无法读取（{ex.GetType().Name}），其余内容仍已导出。");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"无法枚举日志（{ex.GetType().Name}），仅导出状态快照。");
        }
        return result.ToString();
    }
}
