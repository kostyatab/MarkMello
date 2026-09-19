using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

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

    private static HashSet<object> Keys(ResourceDictionary palette, ThemeVariant theme)
    {
        Assert.True(palette.ThemeDictionaries.TryGetValue(theme, out var dictionary), $"{theme} is not defined in the palette");
        return Assert.IsAssignableFrom<ResourceDictionary>(dictionary).Keys.ToHashSet();
    }
}
