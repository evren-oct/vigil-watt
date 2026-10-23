using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace VigilWatt.Core.Diagnostics;

/// <summary>
/// 功 能： 对诊断字段和日志中的身份信息脱敏。
/// 2026/10/23 22:37:53 Administrator
/// 每包独立密钥避免通过已知 MAC 或序列号反查标识，范围见 docs/dev/diagnostics.md。
/// </summary>
public sealed class DiagnosticRedactor
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly Regex Paths = new(@"(?i)(?:[a-z]:\\|\\\\)[^\r\n""<>|]*", RegexOptions.None, RegexTimeout);
    private static readonly Regex DevicePaths = new(@"\b(?:HID|USB|BTHENUM|BTHLEDEVICE|BTHLE|BluetoothLE|Bluetooth)[\\#][^\r\n""<>|]*", RegexOptions.IgnoreCase, RegexTimeout);
    private static readonly Regex DeviceIds = new(@"\b(?:ble|btc|logitech|mchose|rapoo|betop|inphic|darkrim|gamesir|hecate|delux|aula|dualsense|ganss|cherry|pulsar|eweadn|ajazz|thunderobot):[^\s，。；（）]+", RegexOptions.IgnoreCase, RegexTimeout);
    private static readonly Regex LabeledReports = new(@"((?:原始|收到报文|收到数据|raw|input|output)\s*[:=：]\s*)(?:[0-9a-f]{2}(?:[ -]*[0-9a-f]{2})*)", RegexOptions.IgnoreCase, RegexTimeout);
    private static readonly Regex MacAddresses = new(@"\b(?:[0-9a-f]{2}[:-]){5}[0-9a-f]{2}\b|\b[0-9a-f]{12}\b", RegexOptions.IgnoreCase, RegexTimeout);
    private static readonly Regex RawReports = new(@"(?<!匿名-)\b[0-9a-f]{16,}\b|\b[0-9a-f]{2}(?:[ -]+[0-9a-f]{2}){7,}\b", RegexOptions.IgnoreCase, RegexTimeout);
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private readonly string[] _privateValues;
    private readonly KeyValuePair<string, string>[] _identifiers;

    public DiagnosticRedactor(IEnumerable<string> privateValues, IEnumerable<string> identifiers)
    {
        _privateValues = privateValues.Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(v => v.Length).ToArray();
        _identifiers = identifiers.Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(v => v.Length)
            .Select(v => new KeyValuePair<string, string>(v, Pseudonym(v))).ToArray();
    }

    public string Pseudonym(string value) => "匿名-" + Convert.ToHexString(
        HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(value.ToUpperInvariant())))[..16];

    public string Redact(string value)
    {
        foreach (var pair in _identifiers)
            value = value.Replace(pair.Key, pair.Value, StringComparison.OrdinalIgnoreCase);
        value = Paths.Replace(value, "[路径已脱敏]");
        value = DevicePaths.Replace(value, "[设备路径已脱敏]");
        value = LabeledReports.Replace(value, "$1[报文已省略]");
        value = DeviceIds.Replace(value, m => Pseudonym(m.Value));
        value = MacAddresses.Replace(value, "[MAC已脱敏]");
        value = RawReports.Replace(value, "[报文或标识已省略]");
        foreach (var secret in _privateValues)
            value = value.Replace(secret, "[身份已脱敏]", StringComparison.OrdinalIgnoreCase);
        return value;
    }

    public string Field(string value)
    {
        var clean = Redact(value).Replace('\r', ' ').Replace('\n', ' ');
        return clean.Length <= 1024 ? clean : clean[..1024] + "…";
    }
}
