namespace VigilWatt.Core.Plugins.Aula;

/// <summary>
/// 功 能： 有界重组 HERO 68 Air 的 8K 响应，忽略重传并拒绝乱序分片。
/// 2026/10/23 20:05:00 30149
/// </summary>
internal sealed class AulaHero68PacketAssembler
{
    private readonly byte[] _packet = new byte[AulaHero68Codec.PacketLength];
    private int _total;
    private int _current;
    private int _sync;
    private int _length;

    public void Reset() => _total = _current = _sync = _length = 0;

    public byte[]? Accept(ReadOnlySpan<byte> report)
    {
        if (!AulaHero68Codec.TryParseFragment(report, out int total, out int current, out int sync, out int length)
            || length == 0) return null;
        if (current == _current && sync == _sync && total == _total) return null;
        if (current == 1)
        {
            Reset();
            _total = total;
        }
        if (total != _total || current != _current + 1 || _length + length > _packet.Length
            || (current > 1 && sync != ((_sync + 1) & 7)))
        {
            Reset();
            return null;
        }
        report.Slice(5, length).CopyTo(_packet.AsSpan(_length));
        _length += length;
        _current = current;
        _sync = sync;
        if (current != total) return null;
        return _length == _packet.Length ? _packet.ToArray() : null;
    }
}
