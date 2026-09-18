using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using MarkMello.Domain;
using MarkMello.Presentation.Views;
using MarkMello.Presentation.Views.Markdown;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Полоса цитаты заканчивается чуть ниже последней строки: под текстом остаются
/// только внутренние отступы цитат, симметричные верхним. Регрессия: нижние отступы
/// последнего абзаца и вложенных цитат складывались, и во вложенных цитатах под
/// последней строкой оставалась пустая полоса на каждый уровень.
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

            Assert.True(quote.Padding.Bottom > 0, "the theme should give the quote an inner padding");
            Assert.Equal(quote.Padding.Top, SpaceAboveFirstText(quote), Tolerance);
            Assert.Equal(quote.Padding.Bottom, SpaceBelowLastText(quote), Tolerance);

            // Между абзацами внутри цитаты отступ остаётся.
            Assert.True(Text(view, "First.").Margin.Bottom > 0);

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
            Assert.Equal(quotes.Sum(static level => level.Padding.Bottom), SpaceBelowLastText(quote), Tolerance);

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

            Assert.Equal(quote.Padding.Bottom, SpaceBelowLastText(quote), Tolerance);

            window.Close();
        }, CancellationToken.None);
    }

    private static MarkdownParagraphBlock Paragraph(string text) => new([new MarkdownTextInline(text)]);

    private static (Window Window, MarkdownDocumentView View) Show(MarkdownQuoteBlock quote)
    {
        var view = new MarkdownDocumentView
        {
            ReadingPreferences = ReadingPreferences.Default,
            Document = new RenderedMarkdownDocument([quote, Paragraph("After.")])
        };

        // Отступы цитаты задаёт тема.
        var window = ThemedTestWindow.Create(ThemeVariant.Light, view);
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
