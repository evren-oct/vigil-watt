using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.Sony;

/// <summary>
/// 功 能： 索尼 INZONE H9 II（接收器模式）插件 —— 走 0xFF04 的 SET_REPORT 查询电量。
/// 2026/10/24 23:45:00 imx1n
/// 真机验证：客户端关闭状态下仍可读到 78%，音量 40 与设备一致。
///
/// 与纯被动插件不同，**这个插件会向设备写帧**：开「索尼 INZONE」开关即授权发送电量查询帧。
/// 帧格式与坑见 <see cref="InzoneCodec"/> 与 docs/hardware/devices/inzone-h9.md。
/// </summary>
public sealed class InzoneH9Plugin : BatteryPluginBase
{
    public override string Name => "SonyInzone";
    public override string DisplayName => "索尼 INZONE";
    public override string DisplayNameEn => "SONY INZONE";
    public override DeviceKind Kind => DeviceKind.Headset;

    /// <summary>会写帧，但仍参与 15 秒发现节拍（否则耳机重新开机后最坏等一整轮 60 秒）。</summary>
    public override bool SupportsDiscoveryProbe => true;

    /// <summary>失败后 8 秒重探：耳机进盒/关机时不应答，回来后要能尽快认领。</summary>
    protected override TimeSpan ProbeCooldown => TimeSpan.FromSeconds(8);

    /// <summary>查询型插件：轮询空读＝设备失联，不按"沉默"判离线（本协议没有自发推流）。</summary>
    protected override OfflinePolicy Offline { get; } = new()
    {
        Confirm = TimeSpan.FromSeconds(6),
        Silence = TimeSpan.Zero,
    };

    /// <summary>
    /// 认领判据：VID/PID + `0xFF04` 接口 + 64 字节报文。
    /// 同一个 dongle 还有 `0xFF13`（61 字节）与音频接口，不能只按 VID 认。
    /// </summary>
    protected override bool IsCandidate(HidDeviceInfo info)
        => InzoneCodec.IsSupportedInterface(info.VendorId, info.ProductId, info.UsagePage, info.OutputReportLength);

    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
        => Query(handle);

    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev)
    {
        // R1：每个写帧点都要能被开关挡住。基类只在 Probe 阶段拦，轮询这里得自己问（含熔断退避）。
        if (!CanWrite(dev)) return null;

        var reading = Query(dev.Handle);
        return reading.HasValue ? (reading.Value.Percent, reading.Value.Charging) : null;
    }

    /// <summary>
    /// 一问一答：SET_REPORT 发电量查询，再从输入报告里挑出 `0x14/0x04` 的应答。
    /// 设备可能先推一帧无关通知，所以最多连读 3 帧。
    /// </summary>
    private static (string Protocol, int? Percent, ChargingState Charging)? Query(HidDeviceHandle handle)
    {
        if (!handle.SendOutputReport(InzoneCodec.BuildBatteryRequest()))
            return null;

        for (int i = 0; i < 3; i++)
        {
            var frame = handle.ReadInput(TimeSpan.FromMilliseconds(250));
            if (frame == null) break;
            if (InzoneCodec.TryParseBattery(frame, out int percent, out var charging))
            {
                if (percent >= 100 && charging == ChargingState.Charging)
                    charging = ChargingState.Full;
                return ("inzone-hid", percent, charging);
            }
        }

        return null;
    }
}
