namespace VigilWatt.Core.Plugins.Akg;

/// <summary>
/// 功 能： 按有效片段长度拼接有界 RACE 消息。
/// 2026/10/23 21:07:10 30149
/// </summary>
internal sealed class AkgN9PacketAssembler
{
    private readonly List<byte> _pending = new();

    public bool TryAccept(ReadOnlySpan<byte> report, out List<byte[]> messages)
    {
        messages = new();
        if (!AkgN9Codec.TryGetPayloadLength(report, out int length)) return false;
        // 零长度后的字节是设备旧缓存，包含看似有效的电量消息也必须忽略。
        for (int i = 0; i < length; i++) _pending.Add(report[3 + i]);
        while (_pending.Count > 0)
        {
            if (_pending[0] != 5) return false;
            if (_pending.Count < 4) break;
            if (_pending[1] is not (0x5B or 0x5C or 0x5D)) return false;
            int size = 4 + _pending[2] + (_pending[3] << 8);
            if (size < 6 || size > AkgN9Codec.MaxMessageLength) return false;
            if (_pending.Count < size) break;
            messages.Add(_pending.GetRange(0, size).ToArray());
            _pending.RemoveRange(0, size);
        }
        return true;
    }
}
