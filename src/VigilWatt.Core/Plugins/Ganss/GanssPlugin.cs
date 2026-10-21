using System.Diagnostics;
using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Ganss;

/// <summary>
/// 功 能： 0x05AC:024F 键盘族的 2.4G 电量查询插件（GANSS GS3104T-Pro + AULA F108 Pro）。
/// 2026/10/19 19:45:00 xliu
/// 2026/10/21 扩展：AULA F108 Pro 与 GS3104T-Pro 在 HID 层**完全同指纹**——同 VID/PID、同
/// <c>0xFF60/0x0061</c>、同 33 字节、同 reportId 0、连接口号都是 <c>mi_03</c>——所以不另开插件
/// （两个插件抢同一接口只会打架），改为按产品名区分显示名。见 docs/hardware/protocols.md §14。
/// </summary>
public sealed class GanssPlugin : BatteryPluginBase
{
    public override string Name => "Ganss";

    /// <summary>一个协议两个品牌，插件开关共用；具体是哪台设备由 <see cref="BuildDisplayName"/> 决定。</summary>
    public override string DisplayName => "高斯 / 狼蛛";
    public override string DisplayNameEn => "GANSS / AULA";
    public override DeviceKind Kind => DeviceKind.Keyboard;
    protected override OfflinePolicy Offline { get; } = new() { CountPollFailures = false };

    protected override bool IsCandidate(HidDeviceInfo info)
    {
        if (GanssCodec.IsSupportedInterface(info.VendorId, info.ProductId, info.DevicePath, info.Product,
                info.UsagePage, info.Usage, info.InputReportLength, info.OutputReportLength,
                info.OutputReportId))
            return true;

        // 判据全部硬绑，换固件或批次就静默认领失败；同 VID/PID 留一条诊断，否则用户只看到"键盘没出现"。
        if (info.VendorId == GanssCodec.VendorId && info.ProductId == GanssCodec.ProductId)
            Log.InfoThrottled($"ganss-nomatch:{info.DevicePath}",
                $"{DisplayName} 接口判据不符，未认领: {info} path={info.DevicePath}", TimeSpan.FromMinutes(10));
        return false;
    }

    // 基类只走 ProbeAsync（本类已覆写）；保留转接，万一将来改回同步探测路径也不会失效。
    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
        => ProbeAsync(handle).GetAwaiter().GetResult();

    protected override async Task<(string Protocol, int? Percent, ChargingState Charging)?> ProbeAsync(
        HidDeviceHandle handle)
    {
        var percent = await QueryAsync(handle).ConfigureAwait(false);
        if (!percent.HasValue) return null;
        var protocol = GanssCodec.LooksLikeAulaF108(handle.Info.Product) ? "aula-f108-v1" : "ganss-gs3104t-pro-v1";
        return (protocol, percent, ChargingState.Unknown);
    }

    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev)
    {
        if (!CanWrite(dev)) return null;
        var percent = QueryAsync(dev.Handle).GetAwaiter().GetResult();
        if (!percent.HasValue)
        {
            ReportPollFailure(dev);
            return null;
        }
        ReportPollAnswered(dev);
        return (percent, ChargingState.Unknown);
    }

    private async Task<int?> QueryAsync(HidDeviceHandle handle)
    {
        // 写点再次校验开关和完整接口，禁止把同 VID/PID 的其它集合当作厂商通道。
        if (!WritesEnabled || !IsCandidate(handle.Info)) return null;
        if (!handle.WriteOutput(GanssCodec.CreateQuery(), TimeSpan.FromMilliseconds(500)))
            return null;
        var elapsed = Stopwatch.StartNew();
        var window = TimeSpan.FromMilliseconds(750);
        while (elapsed.Elapsed < window)
        {
            var remaining = window - elapsed.Elapsed;
            if (remaining <= TimeSpan.Zero) break;
            var input = await handle.ReadInputAsync(remaining).ConfigureAwait(false);
            if (input == null) break;
            if (!GanssCodec.TryParse(input, out int percent))
            {
                Log.Debug($"{DisplayName} 忽略非电量响应: {Log.Hex(input)}");
                continue;
            }
            Log.Info($"{BuildDisplayName(handle.Info)} 电量查询: {percent}%");
            return percent;
        }
        Log.Warn($"{DisplayName} 电量查询未收到有效响应");
        return null;
    }

    /// <summary>两个品牌共用一套协议，设备行上显示各自真实的型号。</summary>
    protected override string BuildDisplayName(HidDeviceInfo info)
        => GanssCodec.LooksLikeAulaF108(info.Product) ? "狼蛛 F108 Pro" : "GANSS GS3104T-Pro";
}
