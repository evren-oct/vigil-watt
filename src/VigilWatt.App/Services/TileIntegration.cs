using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;
using Windows.ApplicationModel;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace VigilWatt.App.Services;

/// <summary>
/// 功 能： 由 VigilWatt 本体刷新并发布开始菜单磁贴。
/// 2026/11/08 11:25:00 pupprise
/// </summary>
internal sealed class TileIntegration : IDisposable
{
    [ComImport, Guid("2246EA2D-CAEA-4444-A3C4-6DE827E44313"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAppVisibility
    {
        [PreserveSig] int GetAppVisibilityOnMonitor(IntPtr monitor, out int mode);
        [PreserveSig] int IsLauncherVisible([MarshalAs(UnmanagedType.Bool)] out bool visible);
    }

    private const int DataMilliseconds = 15000;
    private const int CycleMilliseconds = 30000;
    private readonly EventWaitHandle _showWindow = new(false, EventResetMode.AutoReset, "VigilWatt_Msix_ShowWindow");
    private readonly Stopwatch _elapsed = new();
    private IAppVisibility? _shell;
    private DispatcherQueueTimer? _timer;
    private TileUpdater? _updater;
    private long _cycle = -1;
    private TileBatteryArt.Row[] _lastRows = [];
    private string? _lastBindings;
    private int _pendingCount = -1;
    private long _pendingSince;
    private bool _showingData;
    private bool _failureLogged;

    public static bool HasPackage { get; } = IsTilePackage();

    public void RequestShowWindow() => _showWindow.Set();

    public void Start(AppServices services)
    {

        try
        {
            var shellClass = Type.GetTypeFromCLSID(new Guid("7E5FE3D9-985F-4908-91F9-EE19F9FD1514"))!;
            _shell = (IAppVisibility)Activator.CreateInstance(shellClass)!;
            if (Environment.OSVersion.Version.Build < 22000)
                _updater = TileUpdateManager.CreateTileUpdaterForApplication();
            if (_updater != null) Log.Info($"动态磁贴就绪: {_updater.Setting}");
        }
        catch (Exception ex)
        {
            Log.Warn($"磁贴初始化失败 0x{ex.HResult:X8}: {ex.Message}");
        }

        _timer = services.MainWindow?.DispatcherQueue.CreateTimer();
        if (_timer == null) return;
        _timer.Interval = TimeSpan.FromMilliseconds(200);
        _timer.Tick += (_, _) =>
        {
            if (_showWindow.WaitOne(0))
                TrayService.ShowMainWindow();
            Update(services);
        };
        _timer.Start();
    }

    private void Update(AppServices services)
    {
        try
        {
            if (_updater == null || _shell == null || _shell.IsLauncherVisible(out bool visible) != 0) return;
            if (!visible)
            {
                if (_elapsed.IsRunning)
                {
                    _elapsed.Reset();
                    Clear();
                }
                return;
            }

            if (!_elapsed.IsRunning)
            {
                _elapsed.Restart();
                _cycle = -1;

                _ = services.Manager.RequestPollAsync();
                Log.Info("打开开始菜单，刷新磁贴设备数据");
            }

            if (_updater.Setting != NotificationSetting.Enabled) return;
            var elapsed = _elapsed.ElapsedMilliseconds;
            var cycle = elapsed / CycleMilliseconds;
            var phase = elapsed % CycleMilliseconds;
            if (phase >= DataMilliseconds)
            {
                Clear();
                return;
            }

            var threshold = services.Settings.LowBatteryThreshold;
            var rows = services.Manager.Devices.Where(d => d.IsOnline)
                .OrderBy(d => d.Source == DeviceSource.System)
                .ThenBy(d => !d.BatteryPercent.HasValue)
                .ThenBy(d => d.BatteryPercent)
                .ThenBy(d => d.Id, StringComparer.Ordinal)
                .Take(2)
                .Select(d => DevicePresentation.WithIconKind(DevicePresentation.Apply(d, services.Settings), services.Settings))
                .Select(d => new TileBatteryArt.Row(d.DisplayName, d.Kind, d.BatteryPercent,
                    d.Charging == ChargingState.Charging,
                    d.BatteryPercent is int level && threshold > 0 && level <= threshold
                        && !DeviceStatusFormatter.IsChargingOrFull(d.Charging)))
                .ToArray();
            if (_showingData && rows.Length != _lastRows.Length)
            {
                if (_pendingCount != rows.Length)
                {
                    _pendingCount = rows.Length;
                    _pendingSince = elapsed;
                }
                if (elapsed - _pendingSince < 400) return;
            }
            _pendingCount = -1;
            if (_lastBindings == null || !_lastRows.SequenceEqual(rows))
            {
                _lastBindings = TileBatteryArt.BuildBindings(rows);
                _lastRows = rows;
            }
            else if (_cycle == cycle && _showingData)
            {
                return;
            }

            var xml = new XmlDocument();
            xml.LoadXml("<tile><visual>" + _lastBindings + "</visual></tile>");
            _updater.Update(new TileNotification(xml) { ExpirationTime = DateTimeOffset.UtcNow.AddMilliseconds(DataMilliseconds - phase) });
            _cycle = cycle;

            _showingData = true;
            _failureLogged = false;
            Log.Info("磁贴已发布: " + string.Join("，", rows.Select(row => row.Description)));
        }
        catch (Exception ex)
        {
            if (_failureLogged) return;
            _failureLogged = true;
            Log.Warn($"磁贴发布失败 0x{ex.HResult:X8}: {ex.Message}");
        }
    }

    private void Clear()
    {
        if (!_showingData) return;
        _updater!.Clear();
        _showingData = false;
    }

    public void Dispose()
    {
        _timer?.Stop();
        if (_showingData)
        {
            try { Clear(); }
            catch (Exception ex) { Log.Warn("清理磁贴失败: " + ex.Message); }
        }
        _showWindow.Dispose();
    }

    private static bool IsTilePackage()
    {
        try { return Package.Current.Id.FullName.Length != 0; }
        catch (InvalidOperationException) { return false; }
    }
}
