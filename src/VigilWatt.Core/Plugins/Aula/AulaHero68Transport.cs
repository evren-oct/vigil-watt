using System.Diagnostics;
using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Aula;

/// <summary>
/// 功 能： 在狼蛛写入开关内执行 HERO 68 Air 固定查询和逐片 ACK。
/// 2026/10/23 20:05:00 30149
/// 不重发超时命令；熔断交由 BatteryPluginBase，见 docs/hardware/aula.md §2。
/// </summary>
internal sealed class AulaHero68Transport(HidDeviceHandle handle, Func<bool> writesEnabled)
{
    private readonly AulaHero68PacketAssembler _assembler = new();
    private int _sync;
    private int _acknowledgements;

    public async Task<bool> IdentifyAsync()
    {
        var packet = await QueryAsync(AulaHero68Codec.CreateUuidQuery(), 0x82, 1, 6).ConfigureAwait(false);
        return packet != null && AulaHero68Codec.IsHero68Air(packet);
    }

    public async Task<(int? Percent, ChargingState Charging)?> ReadBatteryAsync()
    {
        var packet = await QueryAsync(AulaHero68Codec.CreateBatteryQuery(), 0x87, 0, 2).ConfigureAwait(false);
        if (packet == null || !AulaHero68Codec.TryParseBattery(packet, out int percent, out var charging))
            return null;
        Log.Info($"AULA HERO 68 Air 电量查询: 电量={percent} 充电={charging} 原始={Log.Hex(packet)}");
        return (percent, charging);
    }

    private async Task<byte[]?> QueryAsync(byte[] query, byte command, byte parameter, byte length)
    {
        _assembler.Reset();
        _acknowledgements = 0;
        byte[]? response = null;
        var elapsed = Stopwatch.StartNew();
        for (int index = 0; index < AulaHero68Codec.MaxFragments; index++)
        {
            if (elapsed.Elapsed > TimeSpan.FromSeconds(3)) return Failure();
            int offset = index * AulaHero68Codec.FragmentPayloadLength;
            int count = Math.Min(AulaHero68Codec.FragmentPayloadLength, query.Length - offset);
            _sync = (_sync + 1) & 7;
            var sent = AulaHero68Codec.CreateFragment(query.AsSpan(offset, count),
                AulaHero68Codec.MaxFragments, index + 1, _sync);
            if (!Write(sent)) return Failure();

            bool acknowledged = false;
            var ackWait = Stopwatch.StartNew();
            while (ackWait.Elapsed < TimeSpan.FromMilliseconds(500))
            {
                var input = await handle.ReadInputAsync(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
                if (input == null) continue;
                Log.Debug($"AULA HERO 68 Air 输入: {Log.Hex(input)}");
                if (AulaHero68Codec.IsMatchingAck(input, sent))
                {
                    acknowledged = true;
                    break;
                }
                if (!Receive(input, command, parameter, length, ref response)) return Failure();
            }
            if (!acknowledged) return Failure();
        }

        while (response == null && elapsed.Elapsed < TimeSpan.FromSeconds(3))
        {
            var input = await handle.ReadInputAsync(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
            if (input != null) Log.Debug($"AULA HERO 68 Air 输入: {Log.Hex(input)}");
            if (input != null && !Receive(input, command, parameter, length, ref response)) return Failure();
        }
        return response ?? Failure();
    }

    private bool Receive(byte[] input, byte command, byte parameter, byte length, ref byte[]? response)
    {
        if (!AulaHero68Codec.TryParseFragment(input, out _, out _, out _, out int payloadLength)
            || payloadLength == 0) return true;
        if (++_acknowledgements > 15) return false;
        if (!Write(AulaHero68Codec.CreateAck(input))) return false;
        var packet = _assembler.Accept(input);
        if (packet != null && AulaHero68Codec.IsResponse(packet, command, parameter, length))
            response = packet;
        return true;
    }

    private bool Write(byte[] report)
    {
        var info = handle.Info;
        // 每片及 ACK 都重新检查开关，查询期间关闭插件也不能继续写。
        if (!writesEnabled() || !AulaHero68Codec.IsSupportedInterface(info.VendorId, info.ProductId,
                info.UsagePage, info.Usage, info.InputReportLength, info.OutputReportLength, info.OutputReportId))
            return false;
        Log.Debug($"AULA HERO 68 Air 输出: {Log.Hex(report)}");
        return handle.WriteOutput(report, TimeSpan.FromMilliseconds(500));
    }

    private static byte[]? Failure()
    {
        Log.Warn("AULA HERO 68 Air 查询未收到有效应答，停止本轮写入");
        return null;
    }
}
