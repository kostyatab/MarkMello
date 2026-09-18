using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Inter встроен в сборку, и каждый вес, которым пользуется приложение, должен
/// находить в нём собственное начертание. Avalonia не применяет ось <c>wght</c>
/// variable-шрифта: из такого файла любой вес рисуется как Regular, в лучшем
/// случае с синтетическим утолщением, которое не меняет ширину текста.
/// </summary>
[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class SansFontFamilyWeightTests
{
    private const string Sample = "Total bold Inter weight 12345";

    private readonly AvaloniaHeadlessFixture _fixture;

    public SansFontFamilyWeightTests(AvaloniaHeadlessFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData(FontStyle.Normal, FontWeight.Normal)]
    [InlineData(FontStyle.Normal, FontWeight.Medium)]
    [InlineData(FontStyle.Normal, FontWeight.SemiBold)]
    [InlineData(FontStyle.Normal, FontWeight.Bold)]
    [InlineData(FontStyle.Italic, FontWeight.Normal)]
    [InlineData(FontStyle.Italic, FontWeight.SemiBold)]
    [InlineData(FontStyle.Italic, FontWeight.Bold)]
    public Task EveryUsedWeightResolvesToItsOwnInterFace(FontStyle style, FontWeight weight)
    {
        return _fixture.Session.Dispatch(() =>
        {
            var typeface = new Typeface(LoadSansFontFamily(), style, weight);

            Assert.True(FontManager.Current.TryGetGlyphTypeface(typeface, out var glyphTypeface));
            AssertOwnInterFace(glyphTypeface, style, weight);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(FontStyle.Normal, FontWeight.Medium)]
    [InlineData(FontStyle.Normal, FontWeight.SemiBold)]
    [InlineData(FontStyle.Normal, FontWeight.Bold)]
    [InlineData(FontStyle.Italic, FontWeight.SemiBold)]
    [InlineData(FontStyle.Italic, FontWeight.Bold)]
    public Task HeavierWeightSetsWiderText(FontStyle style, FontWeight weight)
    {
        return _fixture.Session.Dispatch(() =>
        {
            var family = LoadSansFontFamily();

            var regularWidth = MeasureWidth(family, style, FontWeight.Normal);
            var heavierWidth = MeasureWidth(family, style, weight);

            Assert.True(
                heavierWidth > regularWidth,
                $"{weight} {style} is {heavierWidth:0.0} px wide, Normal is {regularWidth:0.0} px.");
        }, CancellationToken.None);
    }

    private static double MeasureWidth(FontFamily family, FontStyle style, FontWeight weight)
    {
        using var layout = new TextLayout(Sample, new Typeface(family, style, weight), 17, Brushes.Black);

        // Ширина что-то доказывает, только если строку набрал сам Inter нужного
        // веса, а не fallback-шрифт или синтетическое начертание.
        var runs = layout.TextLines.SelectMany(line => line.TextRuns).OfType<ShapedTextRun>().ToList();
        Assert.NotEmpty(runs);
        foreach (var run in runs)
        {
            AssertOwnInterFace(run.GlyphRun.GlyphTypeface, style, weight);
        }

        return layout.WidthIncludingTrailingWhitespace;
    }

    private static void AssertOwnInterFace(GlyphTypeface glyphTypeface, FontStyle style, FontWeight weight)
    {
        // У Medium и SemiBold семейство — «Inter Medium»/«Inter SemiBold»,
        // «Inter» записан в типографском семействе.
        var familyName = string.IsNullOrEmpty(glyphTypeface.TypographicFamilyName)
            ? glyphTypeface.FamilyName
            : glyphTypeface.TypographicFamilyName;
        Assert.Equal("Inter", familyName);
        Assert.Equal(weight, glyphTypeface.Weight);
        Assert.Equal(style, glyphTypeface.Style);
        Assert.Equal(FontSimulations.None, glyphTypeface.FontSimulations);
    }

    private static FontFamily LoadSansFontFamily()
    {
        var typography = Assert.IsAssignableFrom<IResourceDictionary>(
            AvaloniaXamlLoader.Load(new Uri("avares://MarkMello.Presentation/Themes/Typography.axaml")));

        return Assert.IsType<FontFamily>(typography["MmDocumentSansFontFamily"]);
    }
}
