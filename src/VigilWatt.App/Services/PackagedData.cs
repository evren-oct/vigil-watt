using Microsoft.Data.Sqlite;
using VigilWatt.Core.Utils;

namespace VigilWatt.App.Services;

/// <summary>
/// 功 能： 首次启动 MSIX 时导入旧版设置和历史快照。
/// 2026/11/08 11:30:00 pupprise
/// </summary>
internal static class PackagedData
{
    public static void ImportLegacyFiles()
    {
        var legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VigilWatt");
        ImportLegacyFiles(legacy, AppPaths.RootDir);
    }

    internal static void ImportLegacyFiles(string legacy, string target)
    {
        if (string.Equals(legacy, target, StringComparison.OrdinalIgnoreCase)) return;

        Directory.CreateDirectory(target);
        ImportFile("settings.json", legacy, target, File.Copy);
        ImportFile("history.db", legacy, target, BackupHistory);
    }

    private static void ImportFile(string name, string legacy, string target, Action<string, string> copy)
    {
        var source = Path.Combine(legacy, name);
        var destination = Path.Combine(target, name);
        if (File.Exists(destination) || !File.Exists(source)) return;

        var temporary = destination + ".migration-" + Guid.NewGuid().ToString("N");
        try
        {
            copy(source, temporary);
            File.Move(temporary, destination);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static void BackupHistory(string source, string destination)
    {
        using var existing = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = source,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        using var backup = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = destination,
            Pooling = false,
        }.ToString());
        existing.Open();
        backup.Open();
        existing.BackupDatabase(backup);
    }
}
