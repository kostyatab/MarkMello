using MarkMello.Domain;
using MarkMello.Presentation.Clipboard;

namespace MarkMello.Presentation.Tests.Clipboard;

public sealed class TelegramHtmlClipboardWriterTests
{
    [Fact]
    public void FormatSelectionHtmlPreservesSelectedLinkAsAnchor()
    {
        var document = new RenderedMarkdownDocument(
        [
            new MarkdownParagraphBlock(
            [
                new MarkdownTextInline("See "),
                new MarkdownLinkInline([new MarkdownTextInline("docs")], "https://example.com/docs?x=1&y=2", null),
                new MarkdownTextInline(" now")
            ])
        ]);
        var textMap = MarkdownDocumentTextMap.Create(document);
        var start = textMap.Text.IndexOf("docs", StringComparison.Ordinal);

        var result = TelegramMarkdownFormatter.FormatSelectionHtml(document, new DocumentTextRange(start, start + "docs".Length));

        Assert.Equal("<a href=\"https://example.com/docs?x=1&amp;y=2\">docs</a>", result);
    }

    [Fact]
    public void FormatSelectionHtmlPreservesListLinksForRichPaste()
    {
        var document = new RenderedMarkdownDocument(
        [
            new MarkdownListBlock(false,
            [
                new MarkdownListItem(
                [
                    new MarkdownParagraphBlock(
                    [
                        new MarkdownLinkInline(
                            [new MarkdownTextInline("Работа с геймпадами")],
                            "https://t.me/gamedev_stinger/286",
                            null)
                    ])
                ]),
                new MarkdownListItem(
                [
                    new MarkdownParagraphBlock(
                    [
                        new MarkdownLinkInline(
                            [new MarkdownTextInline("UX дизайн управления")],
                            "https://t.me/gamedev_stinger/285",
                            null)
                    ])
                ])
            ])
        ]);

        var textMap = MarkdownDocumentTextMap.Create(document);
        var result = TelegramMarkdownFormatter.FormatSelectionHtml(document, new DocumentTextRange(0, textMap.Text.Length));

        Assert.Equal(
            "• <a href=\"https://t.me/gamedev_stinger/286\">Работа с геймпадами</a><br>• <a href=\"https://t.me/gamedev_stinger/285\">UX дизайн управления</a>",
            result);
    }

    [Fact]
    public void FormatSelectionHtmlPutsCheckboxesInPlaceOfBullets()
    {
        var document = new RenderedMarkdownDocument(
        [
            new MarkdownListBlock(false,
            [
                new MarkdownListItem([new MarkdownParagraphBlock([new MarkdownTextInline("done")])], IsChecked: true),
                new MarkdownListItem([new MarkdownParagraphBlock([new MarkdownTextInline("open")])], IsChecked: false),
                new MarkdownListItem([new MarkdownParagraphBlock([new MarkdownTextInline("plain")])])
            ])
        ]);

        var textMap = MarkdownDocumentTextMap.Create(document);
        var result = TelegramMarkdownFormatter.FormatSelectionHtml(document, new DocumentTextRange(0, textMap.Text.Length));

        Assert.Equal("☑ done<br>☐ open<br>• plain", result);
    }

    [Fact]
    public void FormatSelectionHtmlPutsTheAlertTitleInBoldOnTheFirstLineOfTheQuote()
    {
        var document = new RenderedMarkdownDocument(
        [
            new MarkdownQuoteBlock(
                [new MarkdownParagraphBlock([new MarkdownTextInline("Mind <the> gap")])],
                MarkdownAlertKind.Caution)
        ]);
        static string Titles(MarkdownAlertKind kind) => "Внимание";

        var textMap = MarkdownDocumentTextMap.Create(document, Titles);
        var result = TelegramMarkdownFormatter.FormatSelectionHtml(document, new DocumentTextRange(0, textMap.Text.Length), Titles);

        Assert.Equal("&gt; <strong>Внимание</strong><br>&gt; Mind &lt;the&gt; gap", result);
    }

    [Fact]
    public void FormatSelectionHtmlOfTheAlertTitleOnlyLeavesTheBodyOut()
    {
        var document = AlertDocument();

        var result = TelegramMarkdownFormatter.FormatSelectionHtml(document, new DocumentTextRange(0, "Note".Length));

        Assert.Equal("&gt; <strong>Note</strong>", result);
    }

    [Fact]
    public void FormatSelectionHtmlOfTheAlertBodyOnlyLeavesTheTitleOut()
    {
        var document = AlertDocument();
        var textMap = MarkdownDocumentTextMap.Create(document);
        var start = textMap.Text.IndexOf("Body", StringComparison.Ordinal);

        var result = TelegramMarkdownFormatter.FormatSelectionHtml(document, new DocumentTextRange(start, start + "Body text".Length));

        Assert.Equal("&gt; Body text", result);
    }

    [Fact]
    public void FormatSelectionHtmlWrapsStrikethroughInSTag()
    {
        var document = new RenderedMarkdownDocument(
        [
            new MarkdownParagraphBlock(
            [
                new MarkdownTextInline("was "),
                new MarkdownStrikethroughInline([new MarkdownStrongInline([new MarkdownTextInline("10")])]),
                new MarkdownTextInline(" now 8")
            ])
        ]);

        var result = TelegramMarkdownFormatter.FormatSelectionHtml(document, new DocumentTextRange(0, "was 10 now 8".Length));

        Assert.Equal("was <s><strong>10</strong></s> now 8", result);
    }

    private static RenderedMarkdownDocument AlertDocument()
        => new(
        [
            new MarkdownQuoteBlock(
                [new MarkdownParagraphBlock([new MarkdownTextInline("Body text")])],
                MarkdownAlertKind.Note)
        ]);
}
