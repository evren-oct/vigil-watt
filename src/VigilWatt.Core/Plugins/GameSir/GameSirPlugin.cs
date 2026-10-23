using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.GameSir;

/// <summary>
/// 功 能： 盖世小鸡 (GameSir) G7 Pro CE 2.4G 手柄插件：心跳开流 + 突发读电量。
/// 2026/10/19 21:45:00 zc165
/// 帧布局与"心跳维持状态流"的实测依据见 docs/hardware/gamesir.md §1。
/// </summary>
public sealed class GameSirPlugin : BatteryPluginBase
{
    /// <summary>盖世小鸡 USB 厂商 ID。</summary>
    public const ushort GameSirVid = 0x3537;

    /// <summary>配置通道的 usage_page（官方设备表 UsagePage 65520 = 0xFFF0）。</summary>
    public const ushort ConfigUsagePage = 0xFFF0;

    public override string Name => "GameSir";
    public override string DisplayName => "盖世小鸡";
    public override string DisplayNameEn => "GAMESIR";
    public override DeviceKind Kind => DeviceKind.Gamepad;

    /// <summary>心跳要写帧，不参与 15 秒快速发现（走 <see cref="ProbeCooldown"/> 控制重试）。</summary>
    public override bool SupportsFastDiscovery => false;

    /// <summary>探测失败后 30 秒再试：手柄开机/唤醒后不必等满一轮。</summary>
    protected override TimeSpan ProbeCooldown => TimeSpan.FromSeconds(30);

    /// <summary>探测会发心跳帧，故声明写入（受插件开关 + 熔断闸门约束）。</summary>
    protected override bool ProbeWrites => true;

    /// <summary>
    /// 离线判定：读不到就是读不到——心跳发过但窗口内没有状态帧，按失败计数走熔断（3 次≈3 分钟）。
    /// <c>CountPollFailures = true</c>（默认）：本插件每轮确实发帧，null 即失联。
    /// <c>Silence = 0</c>：没有常驻被动流，沉默不是判据。
    /// </summary>
    protected override OfflinePolicy Offline { get; } = new()
    {
        Silence = TimeSpan.Zero,
        CountPollFailures = true,
    };

    /// <summary>
    /// 候选接口：盖世小鸡 VID + 配置通道 usage_page 0xFFF0。
    ///
    /// **不按 PID 白名单硬卡**：同一支 G7 Pro CE 在不同固件/配对模式下会换 PID
    /// （实测见过 0x10B9 与 0x0575）。VID 已锁定厂商，再按厂商页区分通道，误伤别家硬件风险为零。
    /// 该设备的鼠标/键盘/consumer 集合都在 usage_page 0x0001，据此天然排除。
    /// </summary>
    protected override bool IsCandidate(HidDeviceInfo info)
    {
        if (info.VendorId != GameSirVid) return false;
        return info.UsagePage == ConfigUsagePage;
    }

    /// <summary>
    /// 探测 = 发一帧心跳，窗口内等状态帧。
    ///
    /// **为什么能脱离官方软件**：真机实测心跳 `0F F2 00` 就是状态流的开关——
    /// 不发时 0x12 为 0 帧，发一帧立刻涌出约 250 帧/秒、并维持约 2 秒。
    /// 官方客户端每秒发心跳维持流，本插件只在轮询时突发发一帧，读取后让流自然停止。
    /// </summary>
    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
        => ProbeAsync(handle).GetAwaiter().GetResult();

    protected override async Task<(string Protocol, int? Percent, ChargingState Charging)?> ProbeAsync(
        HidDeviceHandle handle)
    {
        var found = await HeartbeatAndReadAsync(handle, TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        if (found is not { } value) return null;

        return (ProtocolName, value.Percent, GameSirCodec.MapCharging(value.Percent, value.Charging));
    }

    /// <summary>轮询 = 发一帧心跳并读 2 秒。写入被开关/熔断挡住时不发帧、不记账。</summary>
    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev)
    {
        if (!CanWrite(dev)) return null;

        var read = HeartbeatAndReadAsync(dev.Handle, TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        if (read == null)
        {
            ReportPollFailure(dev);
            return null;
        }

        ReportPollAnswered(dev);
        return (read.Value.Percent, GameSirCodec.MapCharging(read.Value.Percent, read.Value.Charging));
    }

    /// <summary>协议名（心跳开流 + 状态帧读数）。</summary>
    private const string ProtocolName = "gamesir-state12";

    /// <summary>
    /// 发一帧心跳，然后在窗口内读状态帧，取第一条有效读数。
    ///
    /// 状态流约 250 帧/秒，窗口只需几十毫秒就能拿到读数；这里给到 2 秒是为了容忍
    /// 手柄刚唤醒、或第一帧电量还是占位值的情况。
    /// </summary>
    private static async Task<(int Percent, bool Charging)?> HeartbeatAndReadAsync(
        HidDeviceHandle handle, TimeSpan window)
    {
        var info = handle.Info;
        int length = info.OutputReportLength >= 3 ? info.OutputReportLength : 64;
        var frame = GameSirCodec.BuildHeartbeat(length);

        if (!handle.WriteOutput(frame, TimeSpan.FromMilliseconds(500)))
        {
            Log.Info($"盖世小鸡 心跳写失败（len={length}）: {info.DevicePath}");
            return null;
        }

        var deadline = DateTime.UtcNow + window;
        int frames = 0;
        var reportIds = new Dictionary<byte, int>();
        byte? firstId = null;
        while (DateTime.UtcNow < deadline)
        {
            var remaining = deadline - DateTime.UtcNow;
            var slice = remaining < TimeSpan.FromMilliseconds(200) ? remaining : TimeSpan.FromMilliseconds(200);
            if (slice <= TimeSpan.Zero) break;

            var input = await handle.ReadInputAsync(slice).ConfigureAwait(false);
            if (input == null) continue;
            frames++;

            byte id = input.Length > 0 ? input[0] : (byte)0;
            firstId ??= id;
            reportIds[id] = reportIds.TryGetValue(id, out var n) ? n + 1 : 1;

            if (!GameSirCodec.TryParseState(input, out var state)) continue;

            Log.Info($"盖世小鸡 状态帧: 电量={state.Percent}% 充电={state.Charging} 原始={Log.Hex(input)}");
            return (state.Percent, state.Charging);
        }

        // 记 report ID 分布：收到几千帧却解析不出时，才能立刻看出是"接到了别的集合"还是"布局变了"。
        var dist = string.Join(",", reportIds.OrderByDescending(kv => kv.Value).Take(4)
            .Select(kv => $"0x{kv.Key:X2}×{kv.Value}"));
        Log.Info($"盖世小鸡 心跳后无有效状态帧（收到 {frames} 帧，report={dist}，首帧 id=0x{(firstId ?? 0):X2}）: {info.DevicePath}");
        return null;
    }

    /// <summary>显示名：产品串优先（dongle 常报 "Xbox 360 Controller for Windows"，故不可用时退回型号）。</summary>
    protected override string BuildDisplayName(HidDeviceInfo info)
    {
        var model = GameSirCodec.CleanProductName(info.Product);
        if (string.IsNullOrEmpty(model) || model.Contains("Xbox 360", StringComparison.OrdinalIgnoreCase))
            model = "G7 Pro";
        return $"盖世小鸡 {model}";
    }
}
