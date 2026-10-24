using System.Collections.Concurrent;
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

    /// <summary>开机自启。**默认关闭**，且这里只是保存时的快照——真正生效的是 HKCU Run 键（`AutostartService`），改这个字段不会开关自启。</summary>
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

    /// <summary>任务栏贴片显示在哪块屏幕。空或 ":primary:" = 跟随主屏；否则为显示器稳定身份（monitorDevicePath，见 docs/dev/monitor-identity.md）。</summary>
    public string TaskbarBatteryScreen { get; set; } = "";

    /// <summary>上次选中屏幕的友好名（如 "DELL S2719DC"）。只用于那块屏没插时仍能显示它的名字，不参与匹配。</summary>
    public string TaskbarBatteryScreenLabel { get; set; } = "";

    /// <summary>启动后是否只留在托盘（不显示主窗口）。**默认 false**：启动就显示主界面，
    /// 否则双击后看不到窗口，很容易被误判成"没启动起来"。</summary>
    public bool StartMinimized { get; set; } = false;

    /// <summary>窗口背景材质。取值见 <c>VigilWatt.App.Services.WindowBackdrop</c>：Auto（跟随系统能力）/ Mica（云母，仅 Win11，不满足自动降级）/ Acrylic（磨砂，Win10 1809+）/ Solid（纯色分层底）；未知取值按 Auto。</summary>
    public string WindowBackdrop { get; set; } = "Auto";

    /// <summary>2.4G 插件开关（品牌级）。会写帧的插件以此作为写入授权；纯被动插件（如 Inphic）只用它控制监控。
    /// 不再有独立的"写入许可"开关（CR-37）。会写帧的插件默认关闭（见各项注释）。</summary>
    public Dictionary<string, bool> PluginEnabled { get; set; } = new()
    {
        ["Logitech"] = false,   // 罗技 HID++ 的发现与读数全靠写帧（自建闸门见 CR-01）
        ["Mchose"] = false,   // 迈从 dongle 协议未破解，探测写入可能干扰设备
        ["Rapoo"] = true,     // 雷柏走纯被动心跳读取，零写入，默认开启
        ["Betop"] = true,     // 北通手柄：发官方同款只读状态查询（cmd=0x5），默认开启
        ["Ajazz"] = false,
        ["Thunderobot"] = false,
        ["Inphic"] = true,    // 英菲克 report 0x03 被动监听，不发送任何帧
        ["Darkrim"] = false,  // 寸渊影弑：发官方同款 GetBasicInfo(0x09) 才拿得到电量，会写帧，默认关（§16）
        ["GameSir"] = true,   // 盖世小鸡：每轮发一帧官方心跳开启状态流，60 秒突发读一次，默认开（§17）
        ["Hecate"] = true,    // HECATE/漫步者电竞：厂商集合 0xFF1C/0x0092 被动状态帧，零写入（见 docs/hardware/hecate.md §1）
        ["Delux"] = false,    // 与英菲克同为 1D57:FA65，只读 0x000A；本机尚未扫到该接收器，默认关
        ["Aula"] = false,     // 狼蛛键盘电量查询需要发帧，默认关闭
        ["Akg"] = false,      // AKG 接收器电量查询需要发帧，默认关闭
        ["DualSense"] = true, // 索尼手柄蓝牙 HID 只读，不发送报告（见 docs/hardware/dualsense.md）
        ["Ganss"] = false,   // GANSS GS3104T-Pro：发固定查询帧才能读电量（会写），默认关闭
        ["Cherry"] = false,   // CHERRY PIXIU75：64 字节块通道读块 0x1A，要写帧，默认关闭
        ["Pulsar"] = false,   // 派世 X2 V2：17 字节 STATUS/POWER 查询，要写帧，默认关闭
        ["Eweadn"] = false,   // 前行者 X87 键盘 / S9 鼠标：两套协议都要发查询帧，默认关闭（eweadn.md §1）
    };

    /// <summary>是否把本机（笔记本）电池也纳入监控。默认关闭——它是"另一类设备"，不像外设那样需要盯着，用户明确要时才开（见设置页「监控本机电量」）。
    /// 注意：纯 bool，老 settings.json 缺该字段时反序列化直接用属性默认值 false，不需要像 <see cref="PluginEnabled"/> 那样手动补键。</summary>
    public bool MonitorSystemBattery { get; set; } = false;

    public Dictionary<string, DeviceCustomization> DeviceCustomizations { get; set; } = new();

    /// <summary>用户"永久移除"的设备（黑名单）：Id → 名称与时间。命中则不认领、不探测、不进列表，只能手动解除。
    /// 与 <see cref="DismissedDevices"/> 是两回事：那个是临时的，设备一上线就失效。
    /// 用并发字典是因为删除发生在 UI 线程、查询发生在插件轮询线程，普通 Dictionary 边读边写会抛。</summary>
    public ConcurrentDictionary<string, BlockedDevice> BlockedDevices { get; set; } = new();

    /// <summary>删除过条目的设备：Id → 删除时刻（Unix ms）。插件每轮会重新上报仍在位的离线条目，
    /// 靠它挡住；设备重新上线时自动清除（用户删的是"离线条目"，设备回来了就该显示）。</summary>
    public ConcurrentDictionary<string, long> DismissedDevices { get; set; } = new();

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

    /// <summary>Save 的串行闸：轮询线程与 UI 线程可能同时写同一份 .tmp。</summary>
    private static readonly object SaveGate = new();
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
            _cached.BlockedDevices ??= new ConcurrentDictionary<string, BlockedDevice>();
            _cached.DismissedDevices ??= new ConcurrentDictionary<string, long>();

            // 老 settings.json 缺新插件的开关键时补默认值：反序列化会用 JSON 里的字典**整体替换**默认字典，
            // 缺键的插件会被 DeviceManager/设置页的 TryGetValue 判成"关闭"，新增插件（如 Betop）装上去默认不工作、用户还以为是功能没实现。
            // 这里按默认表补齐缺失键（缺键才补，不覆盖用户已保存的值）。
            var defaults = new AppSettings();
            if (_cached.PluginEnabled == null)
                _cached.PluginEnabled = defaults.PluginEnabled;
            else
                foreach (var (name, on) in defaults.PluginEnabled)
                    _cached.PluginEnabled.TryAdd(name, on);

            // 反向清理：插件被删掉后遗留的键（如已删除的 Elan 插件）留在配置里只会误导人以为它还在。
            // 默认表就是"当前存在的插件"的唯一清单，不在这张表里的键一律丢弃；下次保存时从 settings.json 消失。
            // 对降级场景无害：老版本会按自己的默认表重新补键。
            if (_cached.PluginEnabled != null)
                foreach (var stale in _cached.PluginEnabled.Keys
                             .Where(k => !defaults.PluginEnabled.ContainsKey(k)).ToList())
                    _cached.PluginEnabled.Remove(stale);

            return _cached;
        }
    }

    /// <summary>
    /// 原子写：先写同目录临时文件并**刷盘**，再整体 rename 覆盖（CR-20）。
    /// 旧实现直接 <c>File.WriteAllText</c> 覆盖原文件，进程写一半崩溃/断电会留下截断的 JSON，
    /// 下次 <see cref="Load"/> 反序列化失败就**静默回落默认值**——用户全部设置丢失且无任何提示。
    /// 同卷 rename 在 NTFS 上是原子替换；返回是否真的落盘（删除设备要"先写名单再动状态"，写失败必须能回滚）。
    /// </summary>
    public bool Save()
    {
        // 删除设备会在轮询线程上"上线即解除标记"顺带 Save，与 UI 线程撞同一份 .tmp 会互相踩掉
        lock (SaveGate)
        {
            var tmp = SettingsPath + ".tmp";
            LastSaveError = "";
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
                return true;
            }
            catch (Exception ex)
            {
                // 日志本身可能就写不进去（同一目录），所以把原因同时留在属性里给 UI 弹窗用
                LastSaveError = $"{ex.GetType().Name}: {ex.Message}";
                Log.Error("settings.json 保存失败", ex);
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* 清不掉临时文件不致命，忽略 */ }
                return false;
            }
        }
    }

    /// <summary>最近一次 <see cref="Save"/> 失败的原因（空串 = 没失败过）。
    /// 存在的理由：失败原因要能显示给用户——只写进日志时，日志本身写不进去就彻底失明了。</summary>
    public static string LastSaveError { get; private set; } = "";
}
