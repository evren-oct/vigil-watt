using VigilWatt.Core.Localization;
using Xunit;

namespace VigilWatt.Core.Tests;

/// <summary>
/// 功 能：界面语言配置值的归一化：zh-CN 是唯一内置回退语言。
/// 2026/11/28 dogs0
/// 不测 CurrentUICulture 分支：并行执行时改全局 culture 会干扰其它断言。
/// </summary>
public class AppLanguageTests
{
    [Theory]
    [InlineData(null, AppLanguage.Auto)]
    [InlineData("", AppLanguage.Auto)]
    [InlineData("  ", AppLanguage.Auto)]
    [InlineData("auto", AppLanguage.Auto)]
    [InlineData("AUTO", AppLanguage.Auto)]
    [InlineData("ru", AppLanguage.Russian)]
    [InlineData("ru-RU", AppLanguage.Russian)]
    [InlineData(" RU-ru ", AppLanguage.Russian)]
    [InlineData("zh", AppLanguage.Chinese)]
    [InlineData("zh-CN", AppLanguage.Chinese)]
    [InlineData("en-US", AppLanguage.Chinese)]
    [InlineData("garbage", AppLanguage.Chinese)]
    public void Normalize_ResolvesSupportedValues(string? configured, string expected)
        => Assert.Equal(expected, AppLanguage.Normalize(configured));

    [Theory]
    [InlineData("ru-RU", AppLanguage.Russian)]
    [InlineData("zh-CN", AppLanguage.Chinese)]
    [InlineData("en-US", AppLanguage.Chinese)] // 未知值直接回退，不看系统 culture
    public void Resolve_WithConfiguredValue_UsesIt(string configured, string expected)
        => Assert.Equal(expected, AppLanguage.Resolve(configured));
}
