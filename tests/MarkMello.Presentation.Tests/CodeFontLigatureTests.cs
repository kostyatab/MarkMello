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
/// Код показывается символ в символ: лигатуры JetBrains Mono рисуют <c>-|</c> как
/// <c>⊣</c>, а <c>=&gt;</c> как <c>⇒</c>. Шрифт склеивает их через <c>calt</c>
/// с глифами-распорками, и ширина строки при этом не меняется, поэтому тесты
/// сравнивают сами глифы с глифами символов из <c>cmap</c> шрифта.
/// </summary>
[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class CodeFontLigatureTests
{
    private const string Operators = "-| => != -> <= >= ===";
    private const string MonoFamilyName = "JetBrains Mono";

    private readonly AvaloniaHeadlessFixture _fixture;

    public CodeFontLigatureTests(AvaloniaHeadlessFixture fixture) => _fixture = fixture;

    [Fact]
    public Task BundledMonoFontJoinsOperatorsUnlessToldNotTo()
    {
        return _fixture.Session.Dispatch(() =>
        {
            // Без этой проверки тесты ниже прошли бы и без исправления: например,
            // если бы шрифт или шейпер перестал склеивать операторы сам.
            var typeface = new Typeface(LoadMonoFontFamily());
            using var joined = new TextLayout(Operators, typeface, 16, Brushes.Black);
            using var separate = new TextLayout(
                Operators,
                typeface,
                16,
                Brushes.Black,
                fontFeatures: MarkdownTextRunPropertiesFactory.CodeFontFeatures);

            var joinedRun = Assert.Single(GlyphRuns(joined));
            Assert.True(IsMonoFont(joinedRun.GlyphTypeface), $"Operators are set in {joinedRun.GlyphTypeface.FamilyName}.");
            Assert.NotEqual(NominalGlyphs(joinedRun), ShapedGlyphs(joinedRun));
            AssertNominal(Assert.Single(GlyphRuns(separate)));
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

    private static List<MarkdownSelectionTextFragment> LayOut(MarkdownBlock block)
    {
        // Тестовая сессия идёт без темы, а без Typography.axaml код набирался бы
        // системным моноширинным шрифтом, у которого может не быть лигатур.
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
