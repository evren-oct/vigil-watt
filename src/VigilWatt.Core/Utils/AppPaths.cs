namespace VigilWatt.Core.Utils;

/// <summary>
/// 功 能： 统一管理应用数据根目录。
/// 2026/10/06 15:53:36 xliu；2026/11/01 加便携版一档（issue #81）
/// 四级取值，优先级从高到低：
/// 1. 环境变量 <see cref="DataDirVariable"/>——显式指定，调试/隔离时用；
/// 2. **Debug 构建：exe 旁边的 <c>data\</c>**——.NET 开发的常规做法，不污染 %LOCALAPPDATA%，
///    开发版与正式版各用一份 settings/history/logs，互不覆盖；
/// 3. **Release 便携版：解压根目录的 <c>data\</c>**——Velopack 便携包根目录带 `.portable` 标记；
/// 4. Release 安装版：<c>%LOCALAPPDATA%\VigilWatt</c>。
/// </summary>
public static class AppPaths
{
    public const string AppName = "VigilWatt";

    /// <summary>覆盖数据根目录的环境变量。优先级最高，用于临时把数据指到别处。</summary>
    public const string DataDirVariable = "VIGILWATT_DATA_DIR";

    /// <summary>Velopack 便携包在解压根目录放的标记文件；安装版没有它，据此分流。</summary>
    private const string PortableMarker = ".portable";

    public static readonly string RootDir = ResolveRootDir();

    /// <summary>非法环境变量（含非法字符等）当作没设，继续往下走。**这里不打日志**：
    /// Log 自己依赖 AppPaths，在静态初始化里调 Log 会绕成环。想确认实际路径看启动日志里的
    /// `history.db 就绪: ...` 即可。</summary>
    private static string ResolveRootDir()
    {
        var custom = Environment.GetEnvironmentVariable(DataDirVariable);
        if (!string.IsNullOrWhiteSpace(custom))
        {
            try { return Path.GetFullPath(custom.Trim()); }
            catch (Exception) { /* 退回下一级 */ }
        }

#if DEBUG
        return Path.Combine(AppContext.BaseDirectory, "data");
#else
        var installed = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);
        return ResolvePortableRoot() ?? installed;
#endif
    }

#if !DEBUG
    /// <summary>
    /// 便携版根目录（`&lt;root&gt;/current/` 的上一级）→ `&lt;root&gt;/data`；不是便携版或不可写则返回 null 退回安装版位置。
    /// 可写性必须探：解压到 Program Files 之类只读位置时，宁可用 %LOCALAPPDATA% 也不能让应用写不进去。
    /// </summary>
    private static string? ResolvePortableRoot()
    {
        try
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
            var root = current.Parent;
            if (root == null || !File.Exists(Path.Combine(root.FullName, PortableMarker))) return null;

            var data = Path.Combine(root.FullName, "data");
            Directory.CreateDirectory(data);
            // 探一次可写：便携包被解压到只读位置时 CreateDirectory 会抛，落到 catch 退回安装版位置。
            var probe = Path.Combine(data, ".writable");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return data;
        }
        catch (Exception)
        {
            return null;
        }
    }
#endif
}
