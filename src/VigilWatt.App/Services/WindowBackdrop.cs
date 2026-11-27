using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml.Media;

using VigilWatt.App.Localization;
namespace VigilWatt.App.Services;

/// <summary>
/// 功 能： 窗口背景材质：取值定义 + "按系统能力回退"的纯逻辑。
/// 2026/10/08 17:10:27 xliu
/// 必须回退而不能写死：Mica 是 Win11 专属，Desktop Acrylic 要 Win10 1809+，两者都得在运行时先问控制器。
/// </summary>
public static class WindowBackdrop
{
    /// <summary>跟随系统能力：能用 Mica 就用 Mica，否则 Acrylic，再不行纯色。</summary>
    public const string Auto = "Auto";

    /// <summary>云母：跟随桌面壁纸的半透明材质，仅 Windows 11。</summary>
    public const string Mica = "Mica";

    /// <summary>桌面亚克力：桌面实时模糊，Windows 10 1809+（省电模式下会被系统关掉并自动回退纯色）。</summary>
    public const string Acrylic = "Acrylic";

    public const string Solid = "Solid";

    private static bool? _micaSupported;
    private static bool? _acrylicSupported;

    /// <summary>进程内缓存：探测要构造控制器，而系统能力在一次运行内不会变（设置页每次进入都会问它）。</summary>
    public static bool IsMicaSupported
    {
        get
        {
            _micaSupported ??= MicaController.IsSupported();
            return _micaSupported.Value;
        }
    }

    public static bool IsAcrylicSupported
    {
        get
        {
            _acrylicSupported ??= DesktopAcrylicController.IsSupported();
            return _acrylicSupported.Value;
        }
    }

    /// <summary>把任意输入（含 null / 手改坏了的配置）收敛成四个合法取值之一。</summary>
    public static string Normalize(string? preference) => preference switch
    {
        Mica => Mica,
        Acrylic => Acrylic,
        Solid => Solid,
        _ => Auto,
    };

    /// <summary>
    /// 纯逻辑：用户偏好 + 本机能力 → 实际生效的材质。
    /// 只有显式选 Solid 才是 Solid；显式选 Acrylic 但本机不支持时也只能是纯色（没有再往下的一档）。
    /// </summary>
    public static string Resolve(string? preference, bool micaSupported, bool acrylicSupported)
    {
        return Normalize(preference) switch
        {
            Solid => Solid,
            Acrylic => acrylicSupported ? Acrylic : Solid,
            // Auto 与 Mica 走同一条路：Mica 优先，退 Acrylic，再退纯色。
            // 显式选了 Mica 却跑在 Win10 上时，静默降级比"什么背景都没有"体验好。
            _ => micaSupported ? Mica : acrylicSupported ? Acrylic : Solid,
        };
    }

    /// <summary>按已解析出的取值造 SystemBackdrop；Solid 返回 null（由调用方铺纯色）。</summary>
    public static SystemBackdrop? Create(string resolved) => resolved switch
    {
        // MicaKind.Base = 较不透明的那一档：主窗口长期驻留，文字可读性优先于通透感
        Mica => new MicaBackdrop { Kind = MicaKind.Base },
        // WASDK 1.7 的 DesktopAcrylicBackdrop 没有 Kind 属性（只有 MicaBackdrop 有），
        // 写了会直接 CS0117 —— 它本身就只有默认那一档，不要画蛇添足
        Acrylic => new DesktopAcrylicBackdrop(),
        _ => null,
    };

    /// <summary>设置页副标题用的一句话说明，直接告诉用户这台机器上实际生效的是什么。</summary>
    public static string Describe(string resolved, bool micaSupported) => resolved switch
    {
        Mica => Strings.Current.Get("S_5fec9621c1"),
        Acrylic => micaSupported
            ? Strings.Current.Get("S_926ffa7936")
            : Strings.Current.Get("S_47ff50df51"),
        _ => Strings.Current.Get("S_d92c70637a"),
    };
}
