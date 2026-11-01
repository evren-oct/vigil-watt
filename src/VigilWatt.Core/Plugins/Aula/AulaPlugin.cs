using System.Runtime.CompilerServices;
using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Compx;
using VigilWatt.Core.Plugins.Mchose;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Aula;

/// <summary>
/// 功 能： 狼蛛 2.4G 插件：F99 / F99 Pro / HERO 68 键盘（会话）与 L9 Ultra 鼠标（查询）；G9 耳机是独立的被动插件。
/// 2026/10/19 15:10:00 xliu；2026/10/23 扩展 HERO 68 Air；2026/10/25 并入 L9 Ultra。
/// 协议与验证范围见 docs/hardware/aula.md；与迈从 G75 / 凌豹 K98 共用 3554:FA09，靠会话 ACK 型号分流（§1.1）。
/// </summary>
public sealed class AulaPlugin : BatteryPluginBase
{
    public const ushort AulaVid = 0x3554;
    public const ushort F99ProPid = 0xFA09;

    /// <summary>L9 Ultra 的接收器（`38A6:28FD`），与 F99 Pro 不同号段、协议无关。</summary>
    public const ushort L9Vid = 0x38A6;
    public const ushort L9Pid = 0x28FD;

    private const string ProtocolF99 = "aula-v1";
    private const string ProtocolHero = "aula-hero68-air";
    private const string ProtocolL9 = "aula-l9-status85";

    /// <summary>F99 Pro 会话 ACK 的型号码；凌豹 K98 是 0x0212，靠它避免抢错同 VID/PID 的接口。</summary>
    public const ushort F99ProModelCode = 0x0158;

    /// <summary>F99（非 Pro）的型号码，见 docs/hardware/aula.md §1.2。</summary>
    public const ushort F99ModelCode = 0x00A4;

    private const string SettingsKey = "Aula";
    private readonly ConditionalWeakTable<HidDeviceHandle, AulaHero68Transport> _heroTransports = new();

    public override string Name => SettingsKey;
    public override string DisplayName => "狼蛛";
    public override string DisplayNameEn => "AULA";
    /// <summary>一个插件管键盘与鼠标：具体图标由 <see cref="ResolveKind"/> 按协议给。</summary>
    public override DeviceKind Kind => DeviceKind.Unknown;

    protected override DeviceKind ResolveKind(string protocol)
        => protocol == ProtocolL9 ? DeviceKind.Mouse : DeviceKind.Keyboard;

    /// <summary>失败后 10 秒内不再开会话。不进 15 秒发现，写入留在 60 秒主轮询。</summary>
    protected override TimeSpan ProbeCooldown => TimeSpan.FromSeconds(10);

    /// <summary>没有周期性心跳，沉默无意义；查询失败由插件自己报账。</summary>
    protected override OfflinePolicy Offline { get; } = new()
    {
        Silence = TimeSpan.Zero,
        CountPollFailures = false,
    };

    protected override bool IsCandidate(HidDeviceInfo info)
        => IsHeroInterface(info) || IsF99Interface(info) || IsL9Interface(info);

    private static bool IsF99Interface(HidDeviceInfo info)
        => info.VendorId == AulaVid && info.ProductId == F99ProPid
           && info.UsagePage == 0xFF02 && info.Usage == 0x0002
           && info.InputReportLength >= MchoseV1Codec.FrameLength
           && info.OutputReportLength >= MchoseV1Codec.FrameLength;

    /// <summary>F99 与 F99 Pro 共用一套帧格式，只有型号码不同（aula.md §1.2）。</summary>
    private static bool IsF99Model(ushort model)
        => model == F99ProModelCode || model == F99ModelCode;

    /// <summary>L9 接收器 4 个厂商接口里只有 0xFFA8 应答，其余不发帧也不认领。</summary>
    private static bool IsL9Interface(HidDeviceInfo info)
        => info.VendorId == L9Vid && info.ProductId == L9Pid
           && info.UsagePage == 0xFFA8
           && info.OutputReportLength >= 3 && info.InputReportLength >= 11;

    private static bool IsHeroInterface(HidDeviceInfo info)
        => AulaHero68Codec.IsSupportedInterface(info.VendorId, info.ProductId, info.UsagePage, info.Usage,
            info.InputReportLength, info.OutputReportLength, info.OutputReportId);

    protected override async Task<(string Protocol, int? Percent, ChargingState Charging)?> ProbeAsync(
        HidDeviceHandle handle)
    {
        if (!IsHeroInterface(handle.Info)) return await base.ProbeAsync(handle).ConfigureAwait(false);
        var transport = _heroTransports.GetValue(handle, h => new AulaHero68Transport(h, () => WritesEnabled));
        if (!await transport.IdentifyAsync().ConfigureAwait(false)) return null;
        var read = await transport.ReadBatteryAsync().ConfigureAwait(false);
        return read is { } value ? (ProtocolHero, value.Percent, value.Charging) : null;
    }

    /// <summary>探测：L9 发一条状态查询；F99 Pro 开会话等 0A 推送。</summary>
    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
    {
        if (IsL9Interface(handle.Info))
        {
            var l9 = QueryL9Async(handle, TimeSpan.FromMilliseconds(1200)).GetAwaiter().GetResult();
            return l9 is { } l ? (ProtocolL9, l.Percent, l.Charging) : null;
        }

        var read = TryRead(handle);
        return read is { } value ? (ProtocolF99, value.Percent, value.Charging) : null;
    }

    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev)
    {
        if (!CanWrite(dev)) return null;

        var read = dev.Protocol switch
        {
            ProtocolHero => _heroTransports.GetValue(dev.Handle, h => new AulaHero68Transport(h, () => WritesEnabled))
                .ReadBatteryAsync().GetAwaiter().GetResult(),
            ProtocolL9 => QueryL9Async(dev.Handle, TimeSpan.FromMilliseconds(800)).GetAwaiter().GetResult(),
            _ => TryRead(dev.Handle),
        };
        if (read.HasValue) ReportPollAnswered(dev);
        else ReportPollFailure(dev);
        return read;
    }

    /// <summary>发开会话帧，等 0A 推送或 4A 响应；拿不到返回 null。ACK 型号不是 F99 系列就让位。</summary>
    private static (int? Percent, ChargingState Charging)? TryRead(HidDeviceHandle handle)
    {
        if (handle.Info.OutputReportLength < MchoseV1Codec.FrameLength) return null;
        if (!handle.WriteOutput(MchoseV1Codec.BuildFrame(MchoseV1Codec.SessionCmd, 0x01),
                TimeSpan.FromMilliseconds(500)))
            return null;

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(4);
        while (DateTime.UtcNow < deadline)
        {
            var input = handle.ReadInput(TimeSpan.FromMilliseconds(300));
            if (input == null) continue;

            // 3554:FA09 上还叠着迈从 G75 与凌豹 K98：型号不是 F99 系列就让位（mchose.md §5、lingbao.md §2）
            if (CompxSessionAck.TryGetModelCode(input, out ushort model))
            {
                if (!IsF99Model(model))
                {
                    Log.Once(LogLevel.Info, $"{handle.Info.DevicePath}|not-f99",
                        $"AULA 跳过：会话 ACK 型号=0x{model:X4}，不是 F99/F99 Pro 原始={Log.Hex(input)}");
                    return null;
                }
                continue;
            }

            var push = MchoseV1Codec.ParsePush(input);
            if (push.Kind == V1PushKind.Battery)
            {
                Log.Info($"AULA 会话电量推送: 电量={push.Percent} 标志=0x{push.ChargeFlag:X2} 原始={Log.Hex(input)}");
                return (push.Percent, ParseCharge(push.ChargeFlag, push.Percent));
            }

            if (MchoseV1Codec.ParseBatteryResponse(input, out int pct, out byte flag) == V1ResponseKind.Battery)
            {
                Log.Info($"AULA 4A 响应: 电量={pct} 状态字节=0x{flag:X2} 原始={Log.Hex(input)}");
                return (pct, ParseCharge(flag, pct));
            }

            if (Log.IsEnabled(LogLevel.Debug)) Log.Debug($"AULA 输入帧: {Log.Hex(input)}");
        }
        return null;
    }

    private static ChargingState ParseCharge(byte flag, int percent)
        => ChargingStateMap.FromChargingFlag((flag & 0x10) != 0, percent);

    protected override string BuildDisplayName(HidDeviceInfo info)
        => IsHeroInterface(info) ? "AULA HERO 68 Air"
           : IsL9Interface(info) ? "狼蛛 L9 Ultra"
           : "AULA F99 Pro";

    /// <summary>L9：发一条 0x81 状态查询，在窗口内等回包。</summary>
    private static async Task<(int? Percent, ChargingState Charging)?> QueryL9Async(
        HidDeviceHandle handle, TimeSpan window)
    {
        if (!handle.WriteOutput(AulaL9Codec.BuildStatusQuery(handle.Info.OutputReportLength),
                TimeSpan.FromMilliseconds(200)))
            return null;

        var deadline = DateTime.UtcNow + window;
        while (DateTime.UtcNow < deadline)
        {
            var remaining = deadline - DateTime.UtcNow;
            var slice = remaining < TimeSpan.FromMilliseconds(250) ? remaining : TimeSpan.FromMilliseconds(250);
            if (slice <= TimeSpan.Zero) break;

            var input = await handle.ReadInputAsync(slice).ConfigureAwait(false);
            if (input == null) continue;
            if (!AulaL9Codec.TryParse(input, out int percent, out var charging)) continue;

            Log.InfoOnChange("aula-l9", $"{percent}|{charging}",
                $"狼蛛 L9 状态回包: {percent}% {charging} 原始={Log.Hex(input)}");
            return (percent, charging);
        }
        return null;
    }
}
