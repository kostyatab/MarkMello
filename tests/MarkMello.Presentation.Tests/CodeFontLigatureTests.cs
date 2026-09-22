using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.VisualTree;
using MarkMello.Domain;
using MarkMello.Presentation.Views;
using MarkMello.Presentation.Views.Markdown;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Код показывается символ в символ: лигатуры моноширинных шрифтов рисуют <c>-|</c>
/// как <c>⊣</c>, а <c>=&gt;</c> как <c>⇒</c>. Шрифты склеивают их через <c>calt</c>
/// с глифами-распорками, и ширина строки при этом не меняется, поэтому тесты
/// сравнивают сами глифы с глифами символов из <c>cmap</c> шрифта.
/// Встроенный шрифт собран без лигатур, поэтому код обязан выключать их сам:
/// набрать его может и шрифт из fallback-стека, у которого лигатуры есть.
/// </summary>
[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class CodeFontLigatureTests
{
    private const string Operators = "-| => != -> <= >= ===";
    private const string MonoFamilyName = "Maple Mono NL";

    private readonly AvaloniaHeadlessFixture _fixture;

    public CodeFontLigatureTests(AvaloniaHeadlessFixture fixture) => _fixture = fixture;

    [Fact]
    public Task BundledMonoFontKeepsOperatorsApartEvenWithLigaturesOn()
    {
        return _fixture.Session.Dispatch(() =>
        {
            // Поэтому тесты на блок кода и инлайн-код ниже проходят и без -liga/-calt:
            // выключены ли лигатуры, проверяют тесты на CodeFontFeatures.
            var typeface = new Typeface(LoadMonoFontFamily());
            using var withLigatures = new TextLayout(
                Operators,
                typeface,
                16,
                Brushes.Black,
                fontFeatures: [FontFeature.Parse("+liga"), FontFeature.Parse("+calt")]);

            AssertNominal(Assert.Single(GlyphRuns(withLigatures)));
        }, CancellationToken.None);
    }

    [Fact]
    public void CodeFontFeaturesTurnLigaturesOff()
    {
        var features = MarkdownTextRunPropertiesFactory.CodeFontFeatures;

        Assert.Contains(features, static feature => feature.Tag == "liga" && feature.Value == 0);
        Assert.Contains(features, static feature => feature.Tag == "calt" && feature.Value == 0);
    }

    [Fact]
    public Task CodeBlockIsSetWithCodeFontFeatures()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var fragment = LayOut(new MarkdownCodeBlock("csharp", Operators))
                .Single(candidate => candidate.Classes.Contains("mm-md-codeblock-text"));

            Assert.Same(MarkdownTextRunPropertiesFactory.CodeFontFeatures, fragment.BaseFontFeatures);
        }, CancellationToken.None);
    }

    [Fact]
    public Task InlineCodeIsSetWithCodeFontFeatures()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var factory = new MarkdownTextRunPropertiesFactory(
                new FontFamily("Georgia"),
                LoadMonoFontFamily(),
                fontSize: 18,
                FontWeight.Normal,
                FontStyle.Normal,
                Brushes.Black,
                linkDecorations: null);

            var code = factory.Get(MarkdownInlineStyleState.Default with { IsCode = true });

            Assert.Same(MarkdownTextRunPropertiesFactory.CodeFontFeatures, code.FontFeatures);
        }, CancellationToken.None);
    }

    [Fact]
    public Task CodeBlockShowsOperatorsAsWritten()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var fragment = LayOut(new MarkdownCodeBlock("csharp", Operators))
                .Single(candidate => candidate.Classes.Contains("mm-md-codeblock-text"));

            var run = Assert.Single(Render(fragment));
            Assert.Equal(Operators, run.Characters.ToString());
            AssertNominal(run);
        }, CancellationToken.None);
    }

    [Fact]
    public Task InlineCodeShowsOperatorsAsWritten()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var fragment = Assert.Single(LayOut(new MarkdownParagraphBlock(
            [
                new MarkdownTextInline("Compare "),
                new MarkdownCodeInline(Operators),
                new MarkdownTextInline(" here")
            ])));

            var run = Assert.Single(Render(fragment), candidate => candidate.Characters.ToString() == Operators);
            AssertNominal(run);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ItalicCodeIsSetWithTheItalicFace(bool bold)
    {
        return _fixture.Session.Dispatch(() =>
        {
            // Рукописные a k l x есть только в курсивных файлах шрифта; без них
            // Avalonia наклонила бы прямое начертание сама.
            MarkdownInline code = new MarkdownEmphasisInline([new MarkdownCodeInline("a k l x")]);
            if (bold)
            {
                code = new MarkdownStrongInline([code]);
            }

            var fragment = Assert.Single(LayOut(new MarkdownParagraphBlock([code])));

            var run = Assert.Single(Render(fragment));
            Assert.True(IsMonoFont(run.GlyphTypeface), $"Code is set in {run.GlyphTypeface.FamilyName}.");
            Assert.Equal(FontStyle.Italic, run.GlyphTypeface.Style);
            Assert.Equal(bold ? FontWeight.Bold : FontWeight.Normal, run.GlyphTypeface.Weight);
            Assert.Equal(FontSimulations.None, run.GlyphTypeface.FontSimulations);
        }, CancellationToken.None);
    }

    private static List<MarkdownSelectionTextFragment> LayOut(MarkdownBlock block)
    {
        // Тестовая сессия идёт без темы, а без Typography.axaml код набирался бы
        // системным моноширинным шрифтом, и проверка на встроенный шрифт упала бы.
        // Документ — только в окне: шрифты блоков ищутся по ресурсам, когда
        // блоки строятся.
        var view = new MarkdownDocumentView();
        var window = new Window { Width = 800, Height = 400 };
        window.Resources.MergedDictionaries.Add(LoadTypography());
        window.Content = view;
        window.Show();
        view.Document = new RenderedMarkdownDocument([block]);
        window.UpdateLayout();

        return view.GetVisualDescendants().OfType<MarkdownSelectionTextFragment>().ToList();
    }

    private static List<GlyphRun> Render(MarkdownSelectionTextFragment fragment)
    {
        var drawing = new DrawingGroup();
        using (var context = drawing.Open())
        {
            fragment.Render(context);
        }

        var runs = new List<GlyphRun>();
        Collect(drawing, runs);
        return runs;
    }

    private static void Collect(Drawing drawing, List<GlyphRun> runs)
    {
        switch (drawing)
        {
            case GlyphRunDrawing { GlyphRun: { } glyphRun }:
                runs.Add(glyphRun);
                break;
            case DrawingGroup group:
                foreach (var child in group.Children)
                {
                    Collect(child, runs);
                }

                break;
        }
    }

    private static IEnumerable<GlyphRun> GlyphRuns(TextLayout layout)
        => layout.TextLines
            .SelectMany(line => line.TextRuns)
            .OfType<ShapedTextRun>()
            .Select(run => run.GlyphRun);

    private static void AssertNominal(GlyphRun run)
    {
        // Иначе текст набрал fallback-шрифт, и проверка ничего не доказывает.
        Assert.True(IsMonoFont(run.GlyphTypeface), $"Code is set in {run.GlyphTypeface.FamilyName}.");
        Assert.Equal(NominalGlyphs(run), ShapedGlyphs(run));
    }

    private static ushort[] ShapedGlyphs(GlyphRun run)
        => run.GlyphInfos.Select(glyph => glyph.GlyphIndex).ToArray();

    private static ushort[] NominalGlyphs(GlyphRun run)
    {
        var map = run.GlyphTypeface.CharacterToGlyphMap;
        return run.Characters.ToArray().Select(character => map.GetGlyph(character)).ToArray();
    }

    private static bool IsMonoFont(GlyphTypeface glyphTypeface)
        => string.Equals(glyphTypeface.FamilyName, MonoFamilyName, StringComparison.Ordinal)
            || string.Equals(glyphTypeface.TypographicFamilyName, MonoFamilyName, StringComparison.Ordinal);

    private static FontFamily LoadMonoFontFamily()
        => Assert.IsType<FontFamily>(LoadTypography()["MmDocumentMonoFontFamily"]);

    private static IResourceDictionary LoadTypography()
        => Assert.IsAssignableFrom<IResourceDictionary>(
            AvaloniaXamlLoader.Load(new Uri("avares://MarkMello.Presentation/Themes/Typography.axaml")));
}
