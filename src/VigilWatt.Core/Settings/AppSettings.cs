using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Settings;
/// <summary>
/// 功 能： 应用设置的加载、保存和默认值。
/// 2026/10/06 15:53:36 xliu
/// </summary>

public class AppSettings
{
    /// <summary>低于该电量弹提醒，0 关闭。</summary>
    public int LowBatteryThreshold { get; set; } = 20;

    public bool RemainingTimeEstimateEnabled { get; set; } = true;

    /// <summary>采样间隔（分钟）：无变化时也定时写一条历史。</summary>
    public int SampleIntervalMinutes { get; set; } = 5;

    /// <summary>历史保留天数，过期清理。</summary>
    public int HistoryRetentionDays { get; set; } = 90;

    public bool Autostart { get; set; }

    public bool WidgetEnabled { get; set; }

    public double WidgetX { get; set; } = 60;

    public double WidgetY { get; set; } = 60;

    /// <summary>小组件一屏最多显示几个设备，超出靠滚轮滚动。合法范围见 <c>VigilWatt.App.Services.WidgetMaxDevicesOptions</c>（1~5，默认 3）；Core 不引用 App，故这里只留数字与默认值，收敛由设置页/小组件负责。</summary>
    public int WidgetMaxDevices { get; set; } = 3;

    /// <summary>小组件显示方式。取值见 <c>VigilWatt.App.Services.WidgetDisplayMode</c>：Topmost（浮在其它窗口上方）/ Normal（普通窗口）/ Desktop（压在其它窗口下方）；未知取值按 Normal。</summary>
    public string WidgetDisplayMode { get; set; } = "Normal";

    /// <summary>是否把电量贴片钉在任务栏上。默认关闭——它会占住任务栏一小块地方，用户明确要时才开。</summary>
    public bool TaskbarBatteryEnabled { get; set; }

    /// <summary>任务栏贴片显示内容。取值见 <c>VigilWatt.App.Services.TaskbarBatteryMode</c>：All（全部在线设备）/ Lowest（仅最低电量一台）；未知取值按 All。</summary>
    public string TaskbarBatteryMode { get; set; } = "All";

    /// <summary>任务栏贴片最多显示几台设备，超出显示 "+N"。合法范围见 <c>VigilWatt.App.Services.TaskbarBatteryOptions</c>（1~5，默认 3）。</summary>
    public int TaskbarBatteryMaxDevices { get; set; } = 3;

    /// <summary>任务栏贴片相对"托盘左侧默认位"的水平偏移（物理像素，正数向右）。用户拖动后写入，重启沿用。</summary>
    public int TaskbarBatteryOffsetX { get; set; }

    /// <summary>记下 <see cref="TaskbarBatteryOffsetX"/> 时任务栏的 DPI（96=100%）。0 = 旧配置尚未绑定，首次读到任务栏 DPI 时只记数不改像素。</summary>
    public int TaskbarBatteryOffsetDpi { get; set; }

    public bool StartMinimized { get; set; } = true;

    public bool TrayIconEnabled { get; set; } = true;

    /// <summary>窗口背景材质。取值见 <c>VigilWatt.App.Services.WindowBackdrop</c>：Auto（跟随系统能力）/ Mica（云母，仅 Win11，不满足自动降级）/ Acrylic（磨砂，Win10 1809+）/ Solid（纯色分层底）；未知取值按 Auto。</summary>
    public string WindowBackdrop { get; set; } = "Auto";

    /// <summary>2.4G 插件开关（品牌级）。会写帧的插件以此作为写入授权；纯被动插件（如 Inphic）只用它控制监控。
    /// 不再有独立的"写入许可"开关（CR-37）。会写帧的插件默认关闭（见各项注释）。</summary>
    public Dictionary<string, bool> PluginEnabled { get; set; } = new()
    {
        ["Logitech"] = false,   // 罗技 HID++ 的发现与读数全靠写帧（自建闸门见 CR-01）
        ["Mchose"] = false,   // 迈从键盘和鼠标读取需主动查询，默认关闭
        ["Rapoo"] = true,     // 雷柏走纯被动心跳读取，零写入，默认开启
        ["Betop"] = true,     // 北通手柄：发官方同款只读状态查询（cmd=0x5），默认开启
        ["Inphic"] = true,    // 英菲克 report 0x03 被动监听，不发送任何帧
        ["Hecate"] = true,    // HECATE/漫步者电竞：厂商集合 0xFF1C/0x0092 被动状态帧，零写入（见 docs/hardware/protocols.md §7）
        ["Delux"] = false,    // 与英菲克同为 1D57:FA65，只读 0x000A；本机尚未扫到该接收器，默认关
        ["Aula"] = false,     // 狼蛛 F99 Pro：要写 0x13 会话帧才能读电量，默认关闭
        ["DualSense"] = true, // 索尼手柄蓝牙 HID 只读，不发送报告（见 docs/hardware/dualsense.md）
        ["Ganss"] = false,   // GANSS GS3104T-Pro：发固定查询帧才能读电量（会写），默认关闭
        ["Cherry"] = false,   // 官方同款 report 0x04 电量查询，需要主动写帧
        ["Haute"] = true,     // 只读取固件和状态 Feature Report
        ["Razer"] = false,    // 官方同款 report 0x02 电量/充电查询，需要主动写帧
    };

    /// <summary>是否把本机（笔记本）电池也纳入监控。默认关闭——它是"另一类设备"，不像外设那样需要盯着，用户明确要时才开（见设置页「监控本机电量」）。
    /// 注意：纯 bool，老 settings.json 缺该字段时反序列化直接用属性默认值 false，不需要像 <see cref="PluginEnabled"/> 那样手动补键。</summary>
    public bool MonitorSystemBattery { get; set; } = false;

    public Dictionary<string, DeviceCustomization> DeviceCustomizations { get; set; } = new();

    /// <summary>日志级别。取值见 <see cref="VigilWatt.Core.Utils.LogLevelNames"/>（Off/Debug/Info/Warn/Error），默认 Info；未知取值按 Info。**重启生效**（启动时赋给 Log.MinLevel）。</summary>
    public string LogLevel { get; set; } = VigilWatt.Core.Utils.LogLevelNames.Default;

    /// <summary>按日日志的保留天数，超期文件在启动/跨天时清理；0 = 不清理。**重启生效**（启动时赋给 Log.RetentionDays）。</summary>
    public int LogRetentionDays { get; set; } = 14;

    [JsonIgnore]
    private static readonly string SettingsPath = Path.Combine(AppPaths.RootDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>进程内缓存：Load 只读一次磁盘（旧版每次 Load 都读文件，插件每轮轮询都会命中）。</summary>
    private static readonly object CacheGate = new();
    private static AppSettings? _cached;

    public static AppSettings Load()
    {
        lock (CacheGate)
        {
            if (_cached != null) return _cached;

            AppSettings? loaded = null;
            try
            {
                if (File.Exists(SettingsPath))
                    loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOpts);
            }
            catch (Exception ex)
            {
                Log.Warn($"settings.json 读取失败，用默认配置: {ex.Message}");
            }

            _cached = loaded ?? new AppSettings();
            _cached.DeviceCustomizations ??= new Dictionary<string, DeviceCustomization>();

            // 老 settings.json 缺新插件的开关键时补默认值：反序列化会用 JSON 里的字典**整体替换**默认字典，
            // 缺键的插件会被 DeviceManager/设置页的 TryGetValue 判成"关闭"，新增插件（如 Betop）装上去默认不工作、用户还以为是功能没实现。
            // 这里按默认表补齐缺失键（缺键才补，不覆盖用户已保存的值）。
            _cached.NormalizePluginSettings();

            return _cached;
        }
    }

    /// <summary>
    /// 原子写：先写同目录临时文件并**刷盘**，再整体 rename 覆盖（CR-20）。
    /// 旧实现直接 <c>File.WriteAllText</c> 覆盖原文件，进程写一半崩溃/断电会留下截断的 JSON，
    /// 下次 <see cref="Load"/> 反序列化失败就**静默回落默认值**——用户全部设置丢失且无任何提示。
    /// 同卷 rename 在 NTFS 上是原子替换，读端只会看到"旧的完整文件"或"新的完整文件"。
    /// </summary>
    public void Save()
    {
        NormalizePluginSettings();
        var tmp = SettingsPath + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var bytes = new UTF8Encoding(false).GetBytes(JsonSerializer.Serialize(this, JsonOpts));
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(flushToDisk: true);
            }
            File.Move(tmp, SettingsPath, overwrite: true);
            lock (CacheGate) _cached = this;
        }
        catch (Exception ex)
        {
            Log.Error("settings.json 保存失败", ex);
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* 清不掉临时文件不致命，忽略 */ }
        }
    }

    internal void NormalizePluginSettings()
    {
        var defaults = new AppSettings();
        PluginEnabled ??= new Dictionary<string, bool>();
        // 废弃的型号开关不能自动开启品牌写入许可，保留用户的 Mchose 选择。
        PluginEnabled.Remove("MchoseMouse");
        foreach (var (name, enabled) in defaults.PluginEnabled)
            PluginEnabled.TryAdd(name, enabled);
        // 反向清理：插件被删掉后遗留的键（如已删除的 Elan 插件）留在配置里只会误导人以为它还在。
        foreach (var stale in PluginEnabled.Keys
                     .Where(k => !defaults.PluginEnabled.ContainsKey(k)).ToList())
            PluginEnabled.Remove(stale);
    }
}
