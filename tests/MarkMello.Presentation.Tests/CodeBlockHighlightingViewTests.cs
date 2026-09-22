using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using MarkMello.Domain;
using MarkMello.Presentation.Views;
using MarkMello.Presentation.Views.Markdown;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Блок кода с подсветкой во view (ADR-0010 §3, §4, §7): цвета — из темы и
/// меняются вместе с ней, текст блока для выделения, копирования и поиска тот
/// же, докраска не сбрасывает выделение, миникарта остаётся без цветов.
/// </summary>
[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class CodeBlockHighlightingViewTests
{
    private const string Code = "public Task Run() => 42; // done";

    private static readonly MarkdownCodeToken[] Tokens =
    [
        new(0, 6, MarkdownCodeTokenKind.Keyword),
        new(7, 4, MarkdownCodeTokenKind.Type),
        new(12, 3, MarkdownCodeTokenKind.Function),
        new(21, 2, MarkdownCodeTokenKind.Constant),
        new(25, 7, MarkdownCodeTokenKind.Comment),
    ];

    private readonly AvaloniaHeadlessFixture _fixture;

    public CodeBlockHighlightingViewTests(AvaloniaHeadlessFixture fixture) => _fixture = fixture;

    [Fact]
    public Task TokensAreDrawnInThemeColors()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var (window, view) = Show(ThemeVariant.Light, Highlighted());
            var fragment = CodeFragment(view);

            var colors = ColorsByText(fragment);

            Assert.Equal(Brush(window, "MmSyntaxKeywordBrush"), colors["public"]);
            Assert.Equal(Brush(window, "MmSyntaxTypeBrush"), colors["Task"]);
            Assert.Equal(Brush(window, "MmSyntaxFunctionBrush"), colors["Run"]);
            Assert.Equal(Brush(window, "MmSyntaxConstantBrush"), colors["42"]);
            Assert.Equal(Brush(window, "MmSyntaxCommentBrush"), colors["// done"]);
            Assert.Equal(Brush(window, "MmTextBrush"), colors["() => "]);
        }, CancellationToken.None);
    }

    [Fact]
    public Task ThemeChangeRecolorsTheCode()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var (window, view) = Show(ThemeVariant.Light, Highlighted());
            var light = ColorsByText(CodeFragment(view))["public"];

            window.RequestedThemeVariant = ThemeVariant.Dark;
            window.UpdateLayout();

            var dark = ColorsByText(CodeFragment(view))["public"];
            Assert.NotEqual(light, dark);
            Assert.Equal(Brush(window, "MmSyntaxKeywordBrush"), dark);
        }, CancellationToken.None);
    }

    [Fact]
    public Task SelectionAndSearchSeeTheSameText()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var (_, plainView) = Show(ThemeVariant.Light, new RenderedMarkdownDocument([new MarkdownCodeBlock("cs", Code)]));
            plainView.SelectAll();
            var (_, view) = Show(ThemeVariant.Light, Highlighted());
            var fragment = CodeFragment(view);

            view.SelectAll();
            Assert.Contains(Code, view.SelectedText, StringComparison.Ordinal);
            Assert.Equal(plainView.SelectedText, view.SelectedText);

            view.ApplySearchQuery("Task Run");
            Assert.Equal(1, view.MatchCount);
            Assert.NotEmpty(fragment.SearchHighlightRanges);
        }, CancellationToken.None);
    }

    [Fact]
    public Task HighlightingKeepsTheSelectionAndOtherBlocks()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var paragraph = new MarkdownParagraphBlock([new MarkdownTextInline("Intro")]);
            var plain = new MarkdownCodeBlock("cs", Code);
            var (window, view) = Show(ThemeVariant.Light, new RenderedMarkdownDocument([paragraph, plain]));
            var introFragment = view.GetVisualDescendants().OfType<MarkdownSelectionTextFragment>()
                .Single(fragment => fragment.StyledText.Text == "Intro");
            view.SelectAll();
            var selected = view.SelectedText;

            view.Document = new RenderedMarkdownDocument([paragraph, plain with { Tokens = Tokens }]);
            window.UpdateLayout();

            Assert.Equal(selected, view.SelectedText);
            Assert.Contains(view.GetVisualDescendants().OfType<MarkdownSelectionTextFragment>(), fragment => ReferenceEquals(fragment, introFragment));
            Assert.True(CodeFragment(view).StyledText.HasSyntax);
        }, CancellationToken.None);
    }

    [Fact]
    public Task MiniatureDrawsCodeWithoutSyntaxColors()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var (window, view) = Show(ThemeVariant.Light, Highlighted());
            var fragment = CodeFragment(view);

            var miniature = CollectRuns(context => fragment.RenderMiniature(context));

            var text = Brush(window, "MmTextBrush");
            Assert.NotEmpty(miniature);
            Assert.All(miniature, run => Assert.Equal(text, Color(run.Foreground)));
        }, CancellationToken.None);
    }

    [Fact]
    public Task CodeWithoutTokensKeepsTheTextColor()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var (window, view) = Show(ThemeVariant.Light, new RenderedMarkdownDocument([new MarkdownCodeBlock("cs", Code)]));

            var runs = CollectRuns(context => CodeFragment(view).Render(context));

            Assert.All(runs, run => Assert.Equal(Brush(window, "MmTextBrush"), Color(run.Foreground)));
        }, CancellationToken.None);
    }

    [Fact]
    public Task DiffLinesGetAFullWidthBackgroundAndAColoredSign()
    {
        return _fixture.Session.Dispatch(() =>
        {
            const string diff = " keep\n+added\n-removed";
            var block = new MarkdownCodeBlock("diff", diff)
            {
                Tokens =
                [
                    new MarkdownCodeToken(6, 6, MarkdownCodeTokenKind.Inserted),
                    new MarkdownCodeToken(13, 8, MarkdownCodeTokenKind.Deleted),
                ],
            };
            var (window, view) = Show(ThemeVariant.Light, new RenderedMarkdownDocument([block]));
            var fragment = view.GetVisualDescendants().OfType<MarkdownSelectionTextFragment>()
                .Single(candidate => candidate.StyledText.Text == diff);
            var bands = Assert.Single(view.GetVisualDescendants().OfType<MarkdownCodeLineBands>());

            var fills = CollectFills(context => bands.Render(context));

            Assert.Equal(2, fills.Count);
            Assert.Equal(Brush(window, "MmSyntaxInsertedBackgroundBrush"), fills[0].Color);
            Assert.Equal(Brush(window, "MmSyntaxDeletedBackgroundBrush"), fills[1].Color);
            Assert.All(fills, fill => Assert.Equal(bands.Bounds.Width, fill.Rect.Width, 3));
            Assert.True(bands.Bounds.Width > fragment.Bounds.Width);
            Assert.True(fills[1].Rect.Top >= fills[0].Rect.Bottom - 0.5);

            var colors = ColorsByText(fragment);
            Assert.Equal(Brush(window, "MmSyntaxInsertedBrush"), colors["+"]);
            Assert.Equal(Brush(window, "MmSyntaxDeletedBrush"), colors["-"]);
            Assert.Equal(Brush(window, "MmTextBrush"), colors["added"]);
        }, CancellationToken.None);
    }

    private static List<(Rect Rect, Color Color)> CollectFills(Action<DrawingContext> render)
    {
        var drawing = new DrawingGroup();
        using (var context = drawing.Open())
        {
            render(context);
        }

        var fills = new List<(Rect, Color)>();
        void Walk(Drawing node)
        {
            switch (node)
            {
                case GeometryDrawing { Geometry: { } geometry, Brush: ISolidColorBrush brush }:
                    fills.Add((geometry.Bounds, brush.Color));
                    break;
                case DrawingGroup group:
                    foreach (var child in group.Children)
                    {
                        Walk(child);
                    }

                    break;
            }
        }

        Walk(drawing);
        return fills;
    }

    private static RenderedMarkdownDocument Highlighted()
        => new([new MarkdownCodeBlock("cs", Code) { Tokens = Tokens }]);

    private static (Window Window, MarkdownDocumentView View) Show(ThemeVariant theme, RenderedMarkdownDocument document)
    {
        var view = new MarkdownDocumentView();
        var window = ThemedTestWindow.Create(theme, view);
        window.Width = 900;
        window.Show();
        view.Document = document;
        window.UpdateLayout();
        return (window, view);
    }

    private static MarkdownSelectionTextFragment CodeFragment(MarkdownDocumentView view)
        => view.GetVisualDescendants().OfType<MarkdownSelectionTextFragment>()
            .Single(fragment => fragment.StyledText.Text == Code);

    private static Color Brush(Window window, string key)
    {
        Assert.True(window.TryFindResource(key, window.ActualThemeVariant, out var value), key);
        return Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
    }

    private static Color Color(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    /// <summary>Цвет каждого отрезка текста, как его нарисовал фрагмент.</summary>
    private static Dictionary<string, Color> ColorsByText(MarkdownSelectionTextFragment fragment)
        => CollectRuns(context => fragment.Render(context))
            .GroupBy(run => run.GlyphRun?.Characters.ToString() ?? string.Empty)
            .ToDictionary(group => group.Key, group => Color(group.First().Foreground));

    private static List<GlyphRunDrawing> CollectRuns(Action<DrawingContext> render)
    {
        var drawing = new DrawingGroup();
        using (var context = drawing.Open())
        {
            render(context);
        }

        var runs = new List<GlyphRunDrawing>();
        Collect(drawing, runs);
        return runs;
    }

    private static void Collect(Drawing drawing, List<GlyphRunDrawing> runs)
    {
        switch (drawing)
        {
            case GlyphRunDrawing run:
                runs.Add(run);
                break;
            case DrawingGroup group:
                foreach (var child in group.Children)
                {
                    Collect(child, runs);
                }

                break;
        }
    }
}
