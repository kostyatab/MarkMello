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
/// Цитата — тёплая плашка с полосой слева и значком кавычек в правом верхнем углу;
/// вложенная — только полоса. Плашка заканчивается чуть ниже последней строки: под
/// текстом остаётся только поле цитаты, симметричное верхнему. Регрессия: нижние
/// отступы последнего абзаца и вложенных цитат складывались, и во вложенных
/// цитатах под последней строкой оставалась пустая полоса на каждый уровень.
/// </summary>
[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class MarkdownQuoteLayoutTests
{
    private const double Tolerance = 0.5;

    private readonly AvaloniaHeadlessFixture _fixture;

    public MarkdownQuoteLayoutTests(AvaloniaHeadlessFixture fixture) => _fixture = fixture;

    [Fact]
    public Task QuoteLeavesOnlyItsPaddingBelowTheLastLine()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var (window, view) = Show(new MarkdownQuoteBlock([Paragraph("First."), Paragraph("Last.")]));
            var quote = TopLevelQuote(view);

            var padding = Content(quote).Margin;
            Assert.True(padding.Bottom > 0, "the quote should have an inner padding");
            Assert.Equal(padding.Top, SpaceAboveFirstText(quote), Tolerance);
            Assert.Equal(padding.Bottom, SpaceBelowLastText(quote), Tolerance);

            // Между абзацами внутри цитаты просвет остаётся.
            Assert.True(Text(view, "Last.").Margin.Top > 0);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task NestedQuotesLeaveOnlyTheirPaddingsBelowTheLastLine()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var (window, view) = Show(new MarkdownQuoteBlock(
            [
                Paragraph("Level one."),
                new MarkdownQuoteBlock(
                [
                    Paragraph("Level two."),
                    new MarkdownQuoteBlock([Paragraph("Level three.")])
                ])
            ]));
            var quote = TopLevelQuote(view);
            var quotes = quote.GetSelfAndVisualDescendants().OfType<Border>()
                .Where(static border => border.Classes.Contains("mm-md-quote"))
                .ToArray();

            Assert.Equal(3, quotes.Length);
            Assert.Equal(Content(quote).Margin.Bottom, SpaceBelowLastText(quote), Tolerance);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task ListAtTheEndOfAQuoteLeavesOnlyTheQuotePaddingBelow()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var (window, view) = Show(new MarkdownQuoteBlock(
            [
                Paragraph("Before a release"),
                new MarkdownListBlock(false, [new MarkdownListItem([Paragraph("Run the tests")]), new MarkdownListItem([Paragraph("Tag the release")])])
            ]));
            var quote = TopLevelQuote(view);

            Assert.Equal(Content(quote).Margin.Bottom, SpaceBelowLastText(quote), Tolerance);

            window.Close();
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public Task QuoteIsAPlaqueWithABarAndAQuoteMark(string themeName)
    {
        return _fixture.Session.Dispatch(() =>
        {
            var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
            var (window, view) = Show(new MarkdownQuoteBlock([Paragraph("A quote.")]), theme);
            var quote = TopLevelQuote(view);
            var em = ReadingPreferences.Default.FontSize;

            Assert.Same(Resource(window, "MmQuoteBackgroundBrush", theme), quote.Background);
            Assert.Same(Resource(window, "MmQuoteMarkBrush", theme), quote.BorderBrush);
            Assert.Equal(new Thickness(3, 0, 0, 0), quote.BorderThickness);
            Assert.Equal(new CornerRadius(em * 6 / 14), quote.CornerRadius);
            Assert.Equal(new Thickness(em * 0.9, em * 0.55, em * 2.4, em * 0.55), Content(quote).Margin);

            // Значок кавычек — в правом верхнем углу плашки, цветом полосы.
            var mark = Assert.Single(quote.GetVisualDescendants().OfType<LucideIcon>());
            Assert.Same(Resource(window, "LucideQuoteGeometry", theme), mark.Data);
            Assert.Same(Resource(window, "MmQuoteMarkBrush", theme), mark.Foreground);
            Assert.Equal(em * 1.1, mark.Width, 3);
            var markBox = new Rect(mark.TranslatePoint(default, quote)!.Value, mark.Bounds.Size);
            Assert.Equal(em * 0.7, markBox.Top, Tolerance);
            Assert.Equal(em * 0.8, quote.Bounds.Width - markBox.Right, Tolerance);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task NestedQuoteIsOnlyABarCloseToTheTextAbove()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var (window, view) = Show(new MarkdownQuoteBlock(
            [
                Paragraph("Level one."),
                new MarkdownQuoteBlock([Paragraph("Level two.")])
            ]));
            var quote = TopLevelQuote(view);
            var nested = Assert.IsType<Border>(Content(quote).Children[1]);
            var em = ReadingPreferences.Default.FontSize;

            Assert.Contains("mm-md-quote-nested", nested.Classes);
            Assert.Same(Resource(window, "MmQuoteBarBrush", ThemeVariant.Light), nested.BorderBrush);
            Assert.Equal(new Thickness(2, 0, 0, 0), nested.BorderThickness);
            Assert.Null(nested.Background is ISolidColorBrush { Color.A: > 0 } ? nested.Background : null);
            Assert.Equal(new Thickness(em * 0.9, 0, 0, 0), nested.Padding);
            Assert.Equal(em * 0.5, nested.Margin.Top, 3);
            Assert.Single(quote.GetVisualDescendants().OfType<LucideIcon>());

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task NestedQuoteAfterATableKeepsTheTableGap()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var (window, view) = Show(new MarkdownQuoteBlock(
            [
                Paragraph("Level one."),
                new MarkdownTableBlock(
                    [new MarkdownTableCell([new MarkdownTextInline("Key")])],
                    [[new MarkdownTableCell([new MarkdownTextInline("Value")])]],
                    [MarkdownTableColumnAlignment.Left]),
                new MarkdownQuoteBlock([Paragraph("Level two.")])
            ]));
            var nested = Assert.IsType<Border>(Content(TopLevelQuote(view)).Children[2]);

            // Соседние просветы не складываются: берётся больший — таблицы, а не .5em.
            Assert.Equal(ReadingPreferences.Default.FontSize * 1.4, nested.Margin.Top, 3);

            window.Close();
        }, CancellationToken.None);
    }

    private static MarkdownParagraphBlock Paragraph(string text) => new([new MarkdownTextInline(text)]);

    private static object Resource(Window window, string key, ThemeVariant theme)
    {
        Assert.True(window.TryFindResource(key, theme, out var value), $"{key} is not defined in the theme");
        return value!;
    }

    /// <summary>Блоки цитаты верхнего уровня: рядом с ними в сетке стоит значок кавычек.</summary>
    private static StackPanel Content(Border quote)
        => Assert.Single(Assert.IsType<Grid>(quote.Child).Children.OfType<StackPanel>());

    private static (Window Window, MarkdownDocumentView View) Show(MarkdownQuoteBlock quote, ThemeVariant? theme = null)
    {
        var view = new MarkdownDocumentView
        {
            ReadingPreferences = ReadingPreferences.Default,
            Document = new RenderedMarkdownDocument([quote, Paragraph("After.")])
        };

        var window = ThemedTestWindow.Create(theme ?? ThemeVariant.Light, view);
        window.Show();
        window.UpdateLayout();
        return (window, view);
    }

    private static Border TopLevelQuote(MarkdownDocumentView view)
    {
        var viewport = Assert.IsType<Border>(view.Content);
        var root = Assert.IsType<StackPanel>(viewport.Child);
        return Assert.IsType<Border>(root.Children[0]);
    }

    private static MarkdownSelectionTextFragment Text(MarkdownDocumentView view, string text)
        => view.GetVisualDescendants().OfType<MarkdownSelectionTextFragment>()
            .Single(fragment => fragment.StyledText.Text == text);

    private static double SpaceAboveFirstText(Border quote)
    {
        var first = quote.GetVisualDescendants().OfType<MarkdownSelectionTextFragment>().First();
        return first.TranslatePoint(default, quote)!.Value.Y;
    }

    private static double SpaceBelowLastText(Border quote)
    {
        var last = quote.GetVisualDescendants().OfType<MarkdownSelectionTextFragment>().Last();
        return quote.Bounds.Height - last.TranslatePoint(new Point(0, last.Bounds.Height), quote)!.Value.Y;
    }
}
