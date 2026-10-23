using System.IO.Compression;
using System.Text;
using System.Text.Json;
using VigilWatt.Core.Diagnostics;
using VigilWatt.Core.Plugins;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能： 验证诊断包的隐私边界、日志截断、共享读取与失败保护。
/// 2026/10/23 22:43:07 Administrator
/// </summary>
public sealed class DiagnosticArchiveTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "VigilWattTests", "diagnostic-" + Guid.NewGuid().ToString("N"));
    private const string DeviceId = "ble:BluetoothLE#BluetoothLEaa:bb:cc:dd:ee:ff-12:34:56:78:90:ab";
    private const string DevicePath = @"\\?\hid#vid_046d&pid_c543#secret-serial#{00000000-0000-0000-0000-000000000000}";

    public DiagnosticArchiveTests() => Directory.CreateDirectory(_directory);

    private static DiagnosticSnapshot Snapshot() => new(
        DateTimeOffset.Parse("2026-10-23T12:00:00Z"), "1.2.3", "Windows 10.0.26200", "X64",
        "China Standard Time", "Info", "Debug", new Dictionary<string, bool> { ["Logitech"] = false },
        new[] { new DiagnosticDevice(DeviceId, "TestOwner 的键盘", "Ble", "BLE GATT", true, 91, "Unknown", DateTimeOffset.UtcNow) },
        null, new[] { new DiagnosticHidInterface(DevicePath, "VID_046D&PID_C543", 0xFF00, 2, 20, 20, 0, 0x11, "USB Receiver", "Logitech", null) });

    private Dictionary<string, string> Export(DiagnosticSnapshot? snapshot = null)
    {
        string destination = Path.Combine(_directory, "diagnostic.zip");
        DiagnosticArchive.Export(destination, snapshot ?? Snapshot(), _directory, new[] { "TestOwner", "Workstation42" });
        using var zip = ZipFile.OpenRead(destination);
        return zip.Entries.ToDictionary(e => e.FullName, e =>
        {
            using var reader = new StreamReader(e.Open());
            return reader.ReadToEnd();
        });
    }

    [Fact]
    public void Export_OnlyIncludesDeclaredEntriesAndRedactsAllIdentityFields()
    {
        File.WriteAllText(Path.Combine(_directory, "app-2026-10-23.log"),
            $"设备 {DeviceId}\n接口 {DevicePath}\n用户 TestOwner 机器 Workstation42\n" +
            @"保存于 C:\Users\TestOwner\private folder\secret.txt" + "\nMAC=AA:BB:CC:DD:EE:FF\n" +
            "原始=00 11 22 33 44 55 66 77 88 99 AA BB CC DD EE FF\n");
        File.WriteAllText(Path.Combine(_directory, "settings.json"), "PRIVATE-SETTINGS");
        File.WriteAllText(Path.Combine(_directory, "history.db"), "PRIVATE-HISTORY");

        var entries = Export();
        Assert.Equal(new[] { "devices.json", "environment.json", "hid-interfaces.json", "logs/recent.log", "report.md" }, entries.Keys.OrderBy(k => k));
        string all = string.Join("\n", entries.Values);
        foreach (string secret in new[] { DeviceId, DevicePath, "secret-serial", "TestOwner", "Workstation42", "private folder", "AA:BB:CC:DD:EE:FF", "PRIVATE-SETTINGS", "PRIVATE-HISTORY", "00 11 22 33 44 55 66 77" })
            Assert.DoesNotContain(secret, all, StringComparison.OrdinalIgnoreCase);
        using var devices = JsonDocument.Parse(entries["devices.json"]);
        string anonymousId = devices.RootElement[0].GetProperty("Id").GetString()!;
        Assert.Contains(anonymousId, entries["logs/recent.log"]);
        Assert.Contains("VID_046D&PID_C543", entries["hid-interfaces.json"]);
        Assert.Contains("不代表整个进程零写入", entries["report.md"]);
    }

    [Theory]
    [InlineData("旧设备 ble:BluetoothLE#BluetoothLEaa:bb:cc:dd:ee:ff 读取失败", "aa:bb:cc:dd:ee:ff")]
    [InlineData(@"节点 USB\VID_1234&PID_5678\SERIAL987", "SERIAL987")]
    [InlineData(@"共享 \\server\home folder\private.txt", "private.txt")]
    [InlineData("旧标识 aabbccddeeff", "aabbccddeeff")]
    [InlineData("电量=84% 原始=13 0A 01 64", "13 0A 01 64")]
    public void Redactor_AlsoCoversIdentifiersAbsentFromCurrentSnapshot(string text, string secret)
    {
        var redactor = new DiagnosticRedactor(Array.Empty<string>(), Array.Empty<string>());
        Assert.DoesNotContain(secret, redactor.Redact(text), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Pseudonyms_AreStableWithinOnePackageAndDifferentAcrossPackages()
    {
        var first = new DiagnosticRedactor(Array.Empty<string>(), Array.Empty<string>());
        var second = new DiagnosticRedactor(Array.Empty<string>(), Array.Empty<string>());
        Assert.Equal(first.Pseudonym(DeviceId), first.Pseudonym(DeviceId.ToUpperInvariant()));
        Assert.NotEqual(first.Pseudonym(DeviceId), second.Pseudonym(DeviceId));
    }

    [Fact]
    public void Export_ReadsLatestLogSliceWhileWriterRemainsOpen()
    {
        string log = Path.Combine(_directory, "app-2026-10-23.log");
        using var writer = new FileStream(log, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        var bytes = Encoding.UTF8.GetBytes("old-marker\n" + new string('x', DiagnosticArchive.MaxLogBytes) + "\nlatest-marker\n");
        writer.Write(bytes);
        writer.Flush();
        var entries = Export();
        Assert.Contains("latest-marker", entries["logs/recent.log"]);
        Assert.DoesNotContain("old-marker", entries["logs/recent.log"]);
        Assert.True(Encoding.UTF8.GetByteCount(entries["logs/recent.log"]) < DiagnosticArchive.MaxLogBytes);
        Assert.Contains("末尾切片", entries["report.md"]);
    }

    [Fact]
    public void Export_OnlyReadsTwoDatedLogsAndDoesNotTreatOtherFilesAsLogs()
    {
        File.WriteAllText(Path.Combine(_directory, "app-2026-10-21.log"), "excluded-old-log");
        File.WriteAllText(Path.Combine(_directory, "app-2026-10-22.log"), "yesterday-marker");
        File.WriteAllText(Path.Combine(_directory, "app-2026-10-23.log"), "today-marker");
        File.WriteAllText(Path.Combine(_directory, "app-secret.log"), "excluded-unrelated-file");
        var logs = Export()["logs/recent.log"];
        Assert.Contains("yesterday-marker", logs);
        Assert.Contains("today-marker", logs);
        Assert.DoesNotContain("excluded-", logs);
    }

    [Fact]
    public void Export_WithoutLogsStillProducesUsefulSnapshotAndWarning()
    {
        var entries = Export();
        Assert.Contains("没有可读取的按日日志", entries["report.md"]);
        Assert.Contains("尚未扫描", entries["report.md"]);
        using var environment = JsonDocument.Parse(entries["environment.json"]);
        Assert.Equal("Info", environment.RootElement.GetProperty("ActiveLogLevel").GetString());
        Assert.Equal("Debug", environment.RootElement.GetProperty("ConfiguredLogLevel").GetString());
    }

    [Fact]
    public void Export_FailedReplacementPreservesExistingFileAndRemovesOnlyItsTemporaryFile()
    {
        string destination = Path.Combine(_directory, "existing.zip");
        File.WriteAllText(destination, "keep-existing-file");
        using (var locked = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var error = Record.Exception(() => DiagnosticArchive.Export(destination, Snapshot(), _directory, Array.Empty<string>()));
            Assert.True(error is IOException or UnauthorizedAccessException, $"期望文件替换失败，实际为 {error?.GetType().Name ?? "无异常"}");
        }
        Assert.Equal("keep-existing-file", File.ReadAllText(destination));
        Assert.Empty(Directory.GetFiles(_directory, ".vigilwatt-diag-*.tmp"));
    }

    [Fact]
    public void ClaimsSnapshot_DoesNotChangeWhenRegistryIsReleased()
    {
        var claims = new DeviceClaimRegistry();
        Assert.True(claims.TryClaim("test-interface", "TestPlugin"));
        var snapshot = claims.Snapshot();
        claims.ReleaseAll("TestPlugin");
        Assert.Equal("TestPlugin", snapshot["test-interface"]);
        Assert.Empty(claims.Snapshot());
    }

    [Fact]
    public void Export_LimitsLogSizeAfterRedactionExpandsText()
    {
        string text = string.Concat(Enumerable.Repeat("aabbccddeeff\n", 45000)) + "last-log-marker\n";
        File.WriteAllText(Path.Combine(_directory, "app-2026-10-22.log"), text);
        File.WriteAllText(Path.Combine(_directory, "app-2026-10-23.log"), text);
        var entries = Export();
        Assert.True(Encoding.UTF8.GetByteCount(entries["logs/recent.log"]) <= DiagnosticArchive.MaxLogBytes);
        Assert.Contains("last-log-marker", entries["logs/recent.log"]);
        Assert.DoesNotContain("aabbccddeeff", entries["logs/recent.log"]);
    }

    [Fact]
    public void Export_RejectsNonZipDestinationWithoutChangingExistingFile()
    {
        string destination = Path.Combine(_directory, "settings.json");
        File.WriteAllText(destination, "keep-settings");
        Assert.Throws<ArgumentException>(() => DiagnosticArchive.Export(destination, Snapshot(), _directory, Array.Empty<string>()));
        Assert.Equal("keep-settings", File.ReadAllText(destination));
        Assert.Empty(Directory.GetFiles(_directory, ".vigilwatt-diag-*.tmp"));
    }

    public void Dispose()
    {
        string expectedParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "VigilWattTests"));
        if (string.Equals(Path.GetDirectoryName(Path.GetFullPath(_directory)), expectedParent, StringComparison.OrdinalIgnoreCase))
            Directory.Delete(_directory, recursive: true);
    }
}
