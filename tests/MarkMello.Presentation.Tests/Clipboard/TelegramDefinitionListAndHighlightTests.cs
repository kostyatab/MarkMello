using MarkMello.Domain;
using MarkMello.Infrastructure.Markdown;
using MarkMello.Presentation.Clipboard;

namespace MarkMello.Presentation.Tests.Clipboard;

/// <summary>
/// Копирование в Telegram: у него нет выделения маркером и индексов — остаётся
/// содержимое со своим оформлением. Список определений — термин жирной строкой,
/// определения строками под ним.
/// </summary>
public sealed class TelegramDefinitionListAndHighlightTests
{
    private const string Inlines = "A <mark>**key** part</mark>, H<sub>2</sub>O, mc<sup>2</sup>";

    private const string DefinitionList = "Apple *red*\n:   A **fruit**.\n:   A company.\n\nPear\n:   Also a fruit.";

    [Fact]
    public void MarkdownV2KeepsTheContentOfHighlightAndIndexesWithItsFormatting()
    {
        Assert.Equal("A *key* part, H2O, mc2", TelegramMarkdownFormatter.Format(Render(Inlines)));
    }

    [Fact]
    public void HtmlKeepsTheContentOfHighlightAndIndexesWithItsFormatting()
    {
        var document = Render(Inlines);

        Assert.Equal("A <strong>key</strong> part, H2O, mc2", TelegramMarkdownFormatter.FormatSelectionHtml(document, WholeDocument(document)));
    }

    [Fact]
    public void MarkdownV2WritesTheTermInBoldAndDefinitionsOnLinesBelow()
    {
        Assert.Equal(
            "*Apple _red_*\nA *fruit*\\.\nA company\\.\n*Pear*\nAlso a fruit\\.",
            TelegramMarkdownFormatter.Format(Render(DefinitionList)));
    }

    [Fact]
    public void HtmlWritesTheTermInBoldAndDefinitionsOnLinesBelow()
    {
        var document = Render(DefinitionList);

        Assert.Equal(
            "<strong>Apple <em>red</em></strong><br>A <strong>fruit</strong>.<br>A company.<br><strong>Pear</strong><br>Also a fruit.",
            TelegramMarkdownFormatter.FormatSelectionHtml(document, WholeDocument(document)));
    }

    [Fact]
    public void SelectionInsideADefinitionCopiesOnlyItsPart()
    {
        var document = Render(DefinitionList);
        var text = MarkdownDocumentTextMap.Create(document).Text;
        var start = text.IndexOf("company", StringComparison.Ordinal);

        Assert.Equal("company", TelegramMarkdownFormatter.FormatSelection(document, new DocumentTextRange(start, start + "company".Length)));
    }

    private static RenderedMarkdownDocument Render(string markdown)
        => new MarkdigMarkdownDocumentRenderer().Render(markdown);

    private static DocumentTextRange WholeDocument(RenderedMarkdownDocument document)
        => new(0, MarkdownDocumentTextMap.Create(document).Text.Length);
}
