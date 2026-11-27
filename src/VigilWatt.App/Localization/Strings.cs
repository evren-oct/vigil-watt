using System.ComponentModel;
using System.Globalization;
using VigilWatt.Core.Localization;

namespace VigilWatt.App.Localization;
/// <summary>
/// 功 能：界面文案表：XAML 用 {Binding Key}（页面 DataContext = Current），C# 用 Get/Format。
/// 2026/11/28 dogs0
/// App 构造时登记单例；Core 的纯逻辑层通过 UiText.Translator 复用同一张表。
/// </summary>
public sealed partial class Strings : INotifyPropertyChanged
{
    /// <summary>App.xaml 资源创建时登记；其它代码只读它。</summary>
    public static Strings Current { get; private set; } = null!;

    private string _language = AppLanguage.Chinese;

    public Strings() => Current = this;

    /// <summary>当前生效语言（zh-CN / ru-RU）。</summary>
    public string Language => _language;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>语言切换后，自绘界面（托盘/贴片/小组件）据此重绘。</summary>
    public static event Action? LanguageChanged;

    /// <summary>启动时按设置或系统 culture 选定语言；未知值回退 zh-CN。</summary>
    public void Initialize(string configured)
    {
        Apply(AppLanguage.Resolve(configured), notify: false);
    }

    /// <summary>运行时切换语言：刷新所有绑定并通知自绘界面重绘。</summary>
    public void SetLanguage(string language)
    {
        var resolved = AppLanguage.Normalize(language);
        if (resolved == AppLanguage.Auto) resolved = AppLanguage.Resolve(null);
        Apply(resolved, notify: true);
    }

    private void Apply(string language, bool notify)
    {
        if (_language == language && notify) return;
        _language = language;
        UiText.Language = language;
        UiText.Translator = Get;
        if (!notify) return;
        // 空属性名 = 全部属性变更，WinUI 绑定会重新取值；逐条发也兼容只认具体属性名的实现。
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        foreach (var key in Zh.Keys)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(key));
        LanguageChanged?.Invoke();
    }

    /// <summary>按 key 取当前语言文案；缺条目时回退中文，绝不把 key 暴露给用户。</summary>
    public string Get(string key)
    {
        var table = _language == AppLanguage.Russian ? Ru : Zh;
        return table.TryGetValue(key, out var text) ? text : Zh.GetValueOrDefault(key, key);
    }

    /// <summary>带占位符的文案（{0}、{1}…），参数按当前区域格式；允许传入尚未查明的空值。</summary>
    public string Format(string key, params object?[]? args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args ?? Array.Empty<object?>());
}
