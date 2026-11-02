namespace VigilWatt.Core.Plugins.Vgn;

/// <summary>
/// 功 能： VGN 蜻蜓 F2 的"显示电量"模型——把电压换算成百分比，并做缓慢追随。
/// 2026/11/02
///
/// 为什么需要（现场实测结论）
/// ------------------------
/// F2 的固件在 <c>0x04</c> 电量应答里只给 **5% 一档**的值（实测历史只有
/// 75/80/85/90/95/100），但官方驱动（VGN VHUB）显示的是 93、94 这种非整档数。
///
/// 抓包与逆向确认（<c>probe_f2_sources.py</c> / CDP 注入 <c>vhub_webhidcap.js</c>）：
/// 1. 驱动发的查询帧与我们**逐字节相同**（<c>04 00 … 49</c>）；
/// 2. 驱动收到的应答与我们**逐字节相同**（<c>04 … 02 5F 00 0F EC …</c>，电量位 0x5F=95、电压 4076mV）；
/// 3. 驱动显示的却是 94 —— 因为它在收到电压后**不采信百分比字段**，
///    改用电压查表插值（驱动内部 <c>voltageToLevel</c>），再做时间平滑。
///
/// 所以"设备报 95、驱动显示 94"不是矛盾，而是两种口径：
/// 设备给的是**整数档**，驱动给的是**电压换算的连续值**。
///
/// 换算规则（据驱动行为等价实现，非移植其源码）
/// ------------------------------------------
/// 电压表以 5% 为一档：<c>[3050, 3420, 3480, …, 4060, 4080, 4110]</c>，
/// 落在哪两个点之间就线性插值。实测校验：
/// <list type="bullet">
/// <item>4072mV → (4072-4060)/4 + 90 = 93</item>
/// <item>4076mV → (4076-4060)/4 + 90 = 94</item>
/// <item>4081mV → (4081-4080)/6 + 95 = 95（→ 95.17 四舍五入 95）</item>
/// </list>
///
/// 已知边界（照驱动行为保留，不"修正"）
/// --------------------------------
/// - 电压高于表尾：充电中给 99、放电给 100（驱动就是这么分的）。
/// - 算出来恰好 0 或 15 时 +1（避免显示"0%"和"15%"这两个驱动认为会被误读的值）。
/// - 低电量钳制与满电计数（要连续 8 拍 100% 才认满）属于驱动的 UI 策略，
///   本模型**不实现**——我们直接显示电池状态，不猜用户的充电习惯。
/// </summary>
public static class VgnBatteryDisplayLevel
{
    /// <summary>
    /// 显示值朝目标移动的节拍（秒）。驱动用 10 秒一个 tick，这里保持一致。
    /// </summary>
    public const double TickSeconds = 10.0;

    /// <summary>
    /// 电压 → 百分比 的换算表，每档 5%，索引 i 对应 i*5%。
    /// 首项 3050 是"低于此视为没电"的哨兵，不参与插值基准。
    /// 数值取自官方驱动实测行为（4072→93、4076→94 两点反推 + 全表行为一致）。
    /// </summary>
    private static readonly int[] Voltages =
    {
        3050, 3420, 3480, 3540, 3600, 3660, 3720, 3760, 3800, 3840,
        3880, 3920, 3940, 3960, 3980, 4000, 4020, 4040, 4060, 4080, 4110,
    };

    /// <summary>
    /// 把电压换算成电量百分比。
    /// </summary>
    /// <param name="millivolts">设备回报的电压（mV）。0 或负数 = 设备不提供，调用方应改用设备百分比。</param>
    /// <param name="charging">是否在充电（影响超出表尾时的取值：充电给 99、放电给 100）。</param>
    /// <returns>0~100 的百分比；<paramref name="millivolts"/> 不合法时返回 null。</returns>
    public static int? FromVoltage(int millivolts, bool charging)
    {
        if (millivolts <= 0) return null;

        int last = Voltages[^1];
        // ⚠ 这里用 >= 而不是 >：驱动原本只判 "> 表尾"，于是电压**恰好**等于 4110mV 时
        // 找不到更大的档位、index 保持 -1，最后返回 0%——满电显示成 0% 是明显的边界缺陷，
        // 不复现它。改成 >= 后 4110 归入"表尾及以上"，返回 100（充电 99），单调性也不会断。
        if (millivolts >= last) return charging ? 99 : 100;

        // 找第一个严格大于电压的档位：电压落在这档与前档之间
        int index = -1;
        for (int i = 0; i < Voltages.Length; i++)
        {
            if (millivolts < Voltages[i]) { index = i; break; }
        }

        // 低于表首（3050 以下）或恰好等于表尾：按最低档处理
        if (index <= 0) return 0;

        double interval = (Voltages[index] - Voltages[index - 1]) / 5.0;
        double level = (millivolts - Voltages[index - 1]) / interval + (index - 1) * 5.0;

        // 驱动对"算出来恰好 15"要 +1（15 是它的低电量提示档，显示 15 会和提示语撞车）。
        // 驱动对"恰好 0"也 +1，但那个 +1 与下面的 index<=0 分支重复，而且会让
        // 3050mV→1、3051mV→0 出现一个逆向台阶（破坏单调）。0% 在这里是合法电量值
        // （"读不到"由 index<=0 返回 0 覆盖），所以只保留 15 这条规则。
        if (level == 15) level++;

        return (int)Math.Round(level, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// 朝实测值靠拢，但**按驱动的节拍走**：每 <see cref="TickSeconds"/> 秒最多移动 1 个百分点。
    ///
    /// ⚠ 这里曾经写错过一次，值得记下来：
    /// 驱动里有**两套**时间模型，一个只在首次初始化（<c>batteryHandleInit</c>）时跑一次，
    /// 按 ±0.028/±0.014 %/s 夹住；另一个是常驻定时器（<c>level1TimerTick</c>），
    /// **每 10 秒**让显示值朝目标走一步（1%）。
    /// 第一版实现误把前者当成持续平滑速率，结果下降只有 0.014%/s ——
    /// 60 秒才掉 0.84%，而驱动 60 秒能掉 6%，于是放电时我们的读数一直偏高 1~2 个点
    /// （现场观测：驱动 92 时程序显示 93）。
    ///
    /// 现在的做法：按采样间隔折算成"过了几个 tick"，最多移动同样多的步数。
    /// </summary>
    /// <param name="lastLevel">上次显示值。</param>
    /// <param name="factLevel">本次实测值（已由电压换算）。</param>
    /// <param name="seconds">距上次采样的秒数。</param>
    /// <param name="tickSeconds">一步的间隔；默认 10 秒（与驱动一致），测试可显式传入。</param>
    public static int Smooth(int lastLevel, int factLevel, double seconds, double tickSeconds = 10.0)
    {
        if (factLevel == lastLevel) return lastLevel;

        // 隔太久（超过半小时）说明中间没有观测，旧值已无参考意义，直接采信新值
        if (seconds > 1800) return factLevel;

        // 采样间隔短于一个 tick：还没到该动的时刻，保持不动
        if (seconds < tickSeconds || tickSeconds <= 0) return lastLevel;

        int steps = (int)(seconds / tickSeconds);
        if (factLevel > lastLevel)
        {
            int next = lastLevel + steps;
            return next >= factLevel ? factLevel : next;
        }
        else
        {
            int next = lastLevel - steps;
            return next <= factLevel ? factLevel : next;
        }
    }
}
