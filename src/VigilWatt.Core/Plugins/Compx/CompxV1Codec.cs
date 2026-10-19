namespace VigilWatt.Core.Plugins.Compx;

/// <summary>0A 推送帧（[13 0A …]）的解析结果类型。</summary>
internal enum V1PushKind
{
    /// <summary>不是 0A 推送帧。</summary>
    NotPush,
    Battery,
    BatteryInvalid,
    /// <summary>设备状态通知，**下标 6 不是电量**。</summary>
    StateNotify,
    /// <summary>其他类型的 0A 帧，忽略但值得记日志校准。</summary>
    Other,
}

/// <summary>4A 电量查询响应的解析结果类型。</summary>
internal enum V1ResponseKind
{
    /// <summary>不是 4A 响应。</summary>
    NotResponse,
    Battery,
    InvalidBattery,
}

/// <summary>0A 推送帧的解析结果。Battery 时看 Percent/ChargeFlag；StateNotify 时看 Payload。</summary>
internal readonly record struct V1Push(V1PushKind Kind, byte Type, byte Payload, int Percent, byte ChargeFlag);

/// <summary>
/// 功 能： Compx（中颖）键盘 V1 协议（report 0x13）的**纯帧编解码**，不碰句柄、不写日志，便于单测。
/// 2026/10/19 19:30:00 Bamlax
/// 迈从 G87 与凌豹 K98 共用同一套帧布局，见 docs/hardware/protocols.md §1、§1.6。
/// </summary>
internal static class CompxV1Codec
{
    public const byte ReportId = 0x13;
    public const byte SessionCmd = 0x05;
    public const byte BatteryQueryCmd = 0x4A;
    public const byte PushCmd = 0x0A;

    public const byte PushTypeBattery = 0x05;

    /// <summary>推送帧类型位：设备状态通知（实测=休眠/关机）。</summary>
    public const byte PushTypeState = 0x02;

    /// <summary>
    /// 状态通知 payload 的实测取值之一（与 <see cref="StatePayloadOne"/> 全天交替出现）。
    /// **语义未定论**，只当"值得立刻核实一下"的触发器用（见 MchosePlugin / LingbaoPlugin 的被动解析），
    /// 绝不作为离线证据。
    /// </summary>
    public const byte StatePayloadZero = 0x00;

    /// <summary>状态通知 payload 的另一个实测取值。语义同样未定论，当前不做处理，仅记录。</summary>
    public const byte StatePayloadOne = 0x01;

    /// <summary>无效电量下界：≤1 一律无效（真机休眠帧 payload 就是 0x01）。低电量阈值 20%，
    /// 正常放电到 1% 之前早就提醒过了，所以拒绝它不会漏掉任何真实告警。</summary>
    public const byte InvalidPercentFloor = 1;

    public const int FrameLength = 20;

    /// <summary>构造 V1 帧（补零到 20 字节，末字节 = 前 19 字节之和）。</summary>
    public static byte[] BuildFrame(byte cmd, byte param)
    {
        var packet = new byte[FrameLength];
        packet[0] = ReportId;
        packet[1] = cmd;
        packet[2] = param;
        packet[FrameLength - 1] = Checksum(packet);
        return packet;
    }

    /// <summary>校验和 = 前 19 字节之和 mod 256。</summary>
    public static byte Checksum(byte[] frame)
    {
        byte sum = 0;
        for (int i = 0; i < FrameLength - 1 && i < frame.Length; i++) sum += frame[i];
        return sum;
    }

    /// <summary>校验帧尾校验和是否匹配。</summary>
    public static bool IsChecksumValid(byte[] frame)
        => frame.Length >= FrameLength && frame[FrameLength - 1] == Checksum(frame);

    /// <summary>
    /// 解析 0A 推送帧。Windows 的 ReadInput 缓冲有时带 report ID 前缀（下标 0），
    /// 所以偏移 0/1 都试一次。
    /// </summary>
    public static V1Push ParsePush(byte[] input)
    {
        foreach (int off in new[] { 0, 1 })
        {
            if (input.Length < off + 8) continue;
            if (input[off] != ReportId || input[off + 1] != PushCmd) continue;

            byte type = input[off + 5];
            byte payload = input[off + 6];
            if (type == PushTypeState) return new V1Push(V1PushKind.StateNotify, type, payload, 0, 0);
            if (type != PushTypeBattery) return new V1Push(V1PushKind.Other, type, payload, 0, 0);

            byte pct = input[off + 6];
            byte flag = input[off + 7];
            if (pct <= InvalidPercentFloor || pct > 100)
                return new V1Push(V1PushKind.BatteryInvalid, type, payload, pct, flag);

            return new V1Push(V1PushKind.Battery, type, payload, pct, flag);
        }
        return new V1Push(V1PushKind.NotPush, 0, 0, 0, 0);
    }

    /// <summary>解析 4A 电量查询响应。</summary>
    public static V1ResponseKind ParseBatteryResponse(byte[] input, out int percent, out byte chargeFlag)
    {
        percent = 0;
        chargeFlag = 0;
        if (input.Length < FrameLength) return V1ResponseKind.NotResponse;
        if (input[0] != ReportId || input[1] != BatteryQueryCmd) return V1ResponseKind.NotResponse;

        byte pct = input[5];
        chargeFlag = input[6];
        if (pct <= InvalidPercentFloor || pct > 100)
        {
            percent = pct;
            return V1ResponseKind.InvalidBattery;
        }

        percent = pct;
        return V1ResponseKind.Battery;
    }
}
