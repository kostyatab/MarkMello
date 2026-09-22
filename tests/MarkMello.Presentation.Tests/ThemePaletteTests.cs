using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using MarkMello.Domain;
using MarkMello.Presentation.Views.Markdown;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Палитра — пара словарей Light и Dark. Ключа, которого нет в одной из тем,
/// в ней просто не найдётся: другая тема его не подставит, и контрол останется
/// без цвета. Поэтому наборы ключей в обеих темах совпадают.
/// </summary>
[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class ThemePaletteTests
{
    private readonly AvaloniaHeadlessFixture _fixture;

    public ThemePaletteTests(AvaloniaHeadlessFixture fixture) => _fixture = fixture;

    [Fact]
    public Task LightAndDarkDefineTheSameKeys()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var palette = Assert.IsAssignableFrom<ResourceDictionary>(
                AvaloniaXamlLoader.Load(new Uri("avares://MarkMello.Presentation/Themes/Colors.axaml")));

            var light = Keys(palette, ThemeVariant.Light);
            var dark = Keys(palette, ThemeVariant.Dark);

            Assert.NotEmpty(light);
            Assert.Empty(light.Except(dark));
            Assert.Empty(dark.Except(light));
        }, CancellationToken.None);
    }

    /// <summary>
    /// У каждого вида токена подсветки есть кисть, и её контраст с фоном блока
    /// кода не хуже, чем у вторичного текста (ADR-0010 §3).
    /// </summary>
    [Fact]
    public Task SyntaxColorsAreAtLeastAsReadableAsSoftText()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var palette = Assert.IsAssignableFrom<ResourceDictionary>(
                AvaloniaXamlLoader.Load(new Uri("avares://MarkMello.Presentation/Themes/Colors.axaml")));

            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                Assert.True(palette.ThemeDictionaries.TryGetValue(theme, out var provider));
                var dictionary = Assert.IsAssignableFrom<ResourceDictionary>(provider);
                var background = Color(dictionary, "MmCodeBlockBackgroundBrush");
                var soft = Contrast(Color(dictionary, "MmTextSoftBrush"), background);

                foreach (var kind in Enum.GetValues<MarkdownCodeTokenKind>())
                {
                    var key = MarkdownSyntaxBrushes.GetResourceKey(kind);
                    var contrast = Contrast(Color(dictionary, key), background);
                    Assert.True(contrast >= soft - 0.01, $"{theme} {key}: {contrast:F2} < {soft:F2}");
                }
            }
        }, CancellationToken.None);
    }

    private static Color Color(ResourceDictionary dictionary, string key)
    {
        Assert.True(dictionary.TryGetValue(key, out var value), $"{key} is missing");
        return Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
    }

    private static double Contrast(Color foreground, Color background)
    {
        var (lighter, darker) = (Luminance(foreground), Luminance(background)) is var (a, b) && a > b ? (a, b) : (b, a);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Luminance(Color color)
    {
        static double Channel(byte value)
        {
            var channel = value / 255.0;
            return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));
    }

    private static HashSet<object> Keys(ResourceDictionary palette, ThemeVariant theme)
    {
        Assert.True(palette.ThemeDictionaries.TryGetValue(theme, out var dictionary), $"{theme} is not defined in the palette");
        return Assert.IsAssignableFrom<ResourceDictionary>(dictionary).Keys.ToHashSet();
    }
}
