namespace VigilWatt.Core.Plugins.Delux;

/// <summary>
/// 功 能： M900 Pro 底座充电时的电量失真过滤（真机实测见 protocols.md §7）。
/// 2026/10/19 08:30:00 sqcxz
/// 充电中(0x03)电量是固件装饰值（10 秒虚涨 2-3 点，久放爬到 100）；
/// 切回放电瞬间有假满电帧，之后约 6-10 秒虚高残留才落回真值。
/// 策略：充电期间保持最后可信值；0x03→0x01 过渡开窗丢帧；0x02 满电标志保持信任。
/// </summary>
internal sealed class DockChargeFilter
{
    /// <summary>过渡窗口：覆盖拿下底座后的假满电帧 + 虚高残留（帧间隔约 2 秒，丢约 4 帧）。</summary>
    private static readonly TimeSpan SettleWindow = TimeSpan.FromSeconds(8);

    private int? _reliable;
    private bool _charging;
    private DateTimeOffset _settleUntil;

    /// <summary>用已在展示的电量兜底（例如应用启动时鼠标已在底座上）。只播一次种。</summary>
    public void Seed(int? percent)
    {
        if (percent is >= 0 and <= 100) _reliable ??= percent;
    }

    /// <summary>输入原始 (flag, percent)，返回应上报的电量；充电/过渡期返回保持值。</summary>
    public int Apply(byte flag, int percent, DateTimeOffset now)
    {
        switch (flag)
        {
            case DeluxCodec.FlagCharging:
                _charging = true;
                return _reliable ?? percent;

            case DeluxCodec.FlagFull:
                // 设备自己宣布充满：信任并收尾，清掉过渡状态。
                _charging = false;
                _settleUntil = default;
                _reliable = percent;
                return percent;

            default:
                if (_charging)
                {
                    _charging = false;
                    _settleUntil = now + SettleWindow;
                }
                if (now < _settleUntil) return _reliable ?? percent;
                _reliable = percent;
                return percent;
        }
    }
}
