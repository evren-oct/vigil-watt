using System.Diagnostics;
using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Ganss;

/// <summary>
/// 功 能： 0x05AC:024F 键盘族的 2.4G 电量查询插件（GANSS GS3104T-Pro + 狼蛛 F108 Pro）。
/// 2026/10/19 19:45:00 xliu；2026/10/21 扩展 F108 Pro——两者 HID 层同指纹，只能按产品名分品牌。
/// 协议与验证范围见 docs/hardware/ganss.md；共用号段的陷阱见 docs/hardware/protocols.md §2。
/// </summary>
public sealed class GanssPlugin : BatteryPluginBase
{
    public override string Name => "Ganss";
    /// <summary>一个协议两个品牌、共用一个开关：具体是哪台由 <see cref="BuildDisplayName"/> 决定。</summary>
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

        // 五条判据全部硬绑，换固件或批次就静默认领失败；同 VID/PID 留一条诊断，否则用户只看到"键盘没出现"。
        if (info.VendorId == GanssCodec.VendorId && info.ProductId == GanssCodec.ProductId)
            Log.InfoThrottled($"ganss-nomatch:{info.DevicePath}",
                $"GANSS 接口判据不符，未认领: {info} path={info.DevicePath}", TimeSpan.FromMinutes(10));
        return false;
    }

    // 基类只走 ProbeAsync（本类已覆写）；保留转接，万一将来改回同步探测路径也不会失效。
    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
        => ProbeAsync(handle).GetAwaiter().GetResult();

    protected override async Task<(string Protocol, int? Percent, ChargingState Charging)?> ProbeAsync(
        HidDeviceHandle handle)
    {
        var percent = await QueryAsync(handle).ConfigureAwait(false);
        return percent.HasValue ? ("ganss-gs3104t-pro-v1", percent, ChargingState.Unknown) : null;
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
                Log.Debug($"GANSS 忽略非电量响应: {Log.Hex(input)}");
                continue;
            }
            Log.Info($"GANSS 电量查询: {percent}%");
            return percent;
        }
        Log.Warn("GANSS 电量查询未收到有效响应");
        return null;
    }

    protected override string BuildDisplayName(HidDeviceInfo info)
        => GanssCodec.LooksLikeAulaF108(info.Product) ? "狼蛛 F108 Pro" : "GANSS GS3104T-Pro";

    /// <summary>
    /// 设备 Id 归一到**型号粒度**（不再用路径哈希）。
    /// 这一族设备不报序列号（实测 6 条接口 SN 全空），所以"按 SN 配对"对它无效；
    /// 不归一的话，换 USB 口/重插接收器就会多出一个新 Id——列表里同一个设备冒两行、
    /// 历史断成两段、剩余时间估算重新从头算。取舍见 <see cref="GanssDeviceIdentity"/>。
    ///
    /// 判据用**路径里的接口编号**（<c>mi_03</c>），与 <see cref="IdFor"/> 完全同源——
    /// 黑名单键（<c>IdFor</c>）与上报 Id（<c>BuildDeviceId</c>）必须一致，
    /// 否则"永久移除这台设备"按一个键写、按另一个键判，删不掉。
    /// </summary>
    protected override string IdFor(string devicePath)
        => GanssDeviceIdentity.IdForPath(devicePath, Name.ToLowerInvariant()) ?? base.IdFor(devicePath);
}
