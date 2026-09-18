using MarkMello.Domain;
using MarkMello.Presentation.Clipboard;

namespace MarkMello.Presentation.Tests.Clipboard;

public sealed class TelegramMarkdownV2WriterTests
{
    [Fact]
    public void FormatPreservesInlineLinks()
    {
        var document = new RenderedMarkdownDocument(
        [
            new MarkdownParagraphBlock(
            [
                new MarkdownTextInline("See "),
                new MarkdownLinkInline([new MarkdownTextInline("docs")], "https://example.com/docs", null)
            ])
        ]);

        var result = TelegramMarkdownFormatter.Format(document);

        Assert.Equal("See [docs](https://example.com/docs)", result);
    }

    [Fact]
    public void FormatDataUriImageUsesTextPlaceholderInsteadOfEmbeddingBase64Url()
    {
        var document = new RenderedMarkdownDocument(
        [
            new MarkdownParagraphBlock(
            [
                new MarkdownTextInline("Cost "),
                new MarkdownImageInline("data:image/png;base64,AQIDBA==", null, null)
            ])
        ]);

        var result = TelegramMarkdownFormatter.Format(document);

        Assert.Equal("Cost image", result);
        Assert.DoesNotContain("AQID", result, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatEscapesMarkdownV2TextCharacters()
    {
        var document = new RenderedMarkdownDocument(
        [
            new MarkdownParagraphBlock([new MarkdownTextInline("a_b *c* [x]!")])
        ]);

        var result = TelegramMarkdownFormatter.Format(document);

        Assert.Equal("a\\_b \\*c\\* \\[x\\]\\!", result);
    }

    [Fact]
    public void FormatCodeBlockUsesFencedBlockWithLanguage()
    {
        var document = new RenderedMarkdownDocument(
        [
            new MarkdownCodeBlock("csharp", "var x = 1;\nConsole.WriteLine(x);")
        ]);

        var result = TelegramMarkdownFormatter.Format(document);

        Assert.Equal("```csharp\nvar x = 1;\nConsole.WriteLine(x);\n```", result);
    }

    [Fact]
    public void FormatInlineCodeEscapesBackticks()
    {
        var document = new RenderedMarkdownDocument(
        [
            new MarkdownParagraphBlock([new MarkdownCodeInline("a ` b")])
        ]);

        var result = TelegramMarkdownFormatter.Format(document);

        Assert.Equal("`a \\` b`", result);
    }

    [Fact]
    public void FormatUnorderedListUsesBulletMarkers()
    {
        var document = new RenderedMarkdownDocument(
        [
            new MarkdownListBlock(false,
            [
                new MarkdownListItem([new MarkdownParagraphBlock([new MarkdownTextInline("one")])]),
                new MarkdownListItem([new MarkdownParagraphBlock([new MarkdownTextInline("two")])])
            ])
        ]);

        var result = TelegramMarkdownFormatter.Format(document);

        Assert.Equal("• one\n• two", result);
    }

    [Fact]
    public void FormatOrderedListEscapesMarkerDot()
    {
        var document = new RenderedMarkdownDocument(
        [
            new MarkdownListBlock(true,
            [
                new MarkdownListItem([new MarkdownParagraphBlock([new MarkdownTextInline("one")])]),
                new MarkdownListItem([new MarkdownParagraphBlock([new MarkdownTextInline("two")])])
            ])
        ]);

        var result = TelegramMarkdownFormatter.Format(document);

        Assert.Equal("1\\. one\n2\\. two", result);
    }

    [Fact]
    public void FormatOrderedListKeepsItsStartNumber()
    {
        var document = new RenderedMarkdownDocument(
        [
            new MarkdownListBlock(true,
            [
                new MarkdownListItem([new MarkdownParagraphBlock([new MarkdownTextInline("seven")])]),
                new MarkdownListItem([new MarkdownParagraphBlock([new MarkdownTextInline("eight")])])
            ],
            StartNumber: 7)
        ]);

        var result = TelegramMarkdownFormatter.Format(document);

        Assert.Equal("7\\. seven\n8\\. eight", result);
    }

    [Fact]
    public void FormatTaskListPutsCheckboxesInPlaceOfBullets()
    {
        var result = TelegramMarkdownFormatter.Format(TaskListDocument(isOrdered: false));

        Assert.Equal("☑ done\n☐ open\n• plain", result);
    }

    [Fact]
    public void FormatOrderedTaskListKeepsNumbersBeforeCheckboxes()
    {
        var result = TelegramMarkdownFormatter.Format(TaskListDocument(isOrdered: true));

        Assert.Equal("1\\. ☑ done\n2\\. ☐ open\n3\\. plain", result);
    }

    [Fact]
    public void FormatSelectionKeepsCheckboxOfPartlySelectedTaskList()
    {
        var document = TaskListDocument(isOrdered: false);
        var textMap = MarkdownDocumentTextMap.Create(document);
        var start = textMap.Text.IndexOf('☐', StringComparison.Ordinal);

        var result = TelegramMarkdownFormatter.FormatSelection(document, new DocumentTextRange(start, start + "☐ open".Length));

        Assert.Equal("☐ open", result);
    }

    [Fact]
    public void FormatDiagramUsesMermaidFencedBlock()
    {
        var document = new RenderedMarkdownDocument(
        [
            new MarkdownDiagramBlock(MarkdownDiagramKind.Mermaid, "graph TD\nA-->B")
        ]);

        var result = TelegramMarkdownFormatter.Format(document);

        Assert.Equal("```mermaid\ngraph TD\nA-->B\n```", result);
    }

    [Fact]
    public void FormatWrapsStrikethroughInSingleTildes()
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

        var result = TelegramMarkdownFormatter.Format(document);

        Assert.Equal("was ~*10*~ now 8", result);
    }

    [Fact]
    public void FormatSelectionSlicesInsideStrikethrough()
    {
        var document = new RenderedMarkdownDocument(
        [
            new MarkdownParagraphBlock(
            [
                new MarkdownTextInline("a "),
                new MarkdownStrikethroughInline([new MarkdownTextInline("bcd")]),
                new MarkdownTextInline(" e")
            ])
        ]);
        var textMap = MarkdownDocumentTextMap.Create(document);
        var start = textMap.Text.IndexOf("cd", StringComparison.Ordinal);

        var result = TelegramMarkdownFormatter.FormatSelection(document, new DocumentTextRange(start, start + "cd e".Length));

        Assert.Equal("~cd~ e", result);
    }

    [Fact]
    public void FormatSelectionPreservesSelectedLink()
    {
        var document = new RenderedMarkdownDocument(
        [
            new MarkdownParagraphBlock(
            [
                new MarkdownTextInline("See "),
                new MarkdownLinkInline([new MarkdownTextInline("docs")], "https://example.com/docs", null),
                new MarkdownTextInline(" now")
            ])
        ]);
        var textMap = MarkdownDocumentTextMap.Create(document);
        var start = textMap.Text.IndexOf("docs", StringComparison.Ordinal);

        var result = TelegramMarkdownFormatter.FormatSelection(document, new DocumentTextRange(start, start + "docs".Length));

        Assert.Equal("[docs](https://example.com/docs)", result);
    }

    [Fact]
    public void FormatSelectionSlicesAcrossTextAndLink()
    {
        var document = new RenderedMarkdownDocument(
        [
            new MarkdownParagraphBlock(
            [
                new MarkdownTextInline("See "),
                new MarkdownLinkInline([new MarkdownTextInline("docs")], "https://example.com/docs", null),
                new MarkdownTextInline(" now")
            ])
        ]);
        var textMap = MarkdownDocumentTextMap.Create(document);
        var start = textMap.Text.IndexOf("See", StringComparison.Ordinal);
        var end = textMap.Text.IndexOf(" now", StringComparison.Ordinal);

        var result = TelegramMarkdownFormatter.FormatSelection(document, new DocumentTextRange(start, end));

        Assert.Equal("See [docs](https://example.com/docs)", result);
    }

    [Fact]
    public void FormatSelectionSlicesCodeBlockInsideFence()
    {
        var document = new RenderedMarkdownDocument(
        [
            new MarkdownCodeBlock("csharp", "var x = 1;\nConsole.WriteLine(x);")
        ]);
        var textMap = MarkdownDocumentTextMap.Create(document);
        var start = textMap.Text.IndexOf("Console", StringComparison.Ordinal);
        var end = textMap.Text.IndexOf("(x)", StringComparison.Ordinal) + "(x)".Length;

        var result = TelegramMarkdownFormatter.FormatSelection(document, new DocumentTextRange(start, end));

        Assert.Equal("```csharp\nConsole.WriteLine(x)\n```", result);
    }

    [Fact]
    public void FormatPutsTheAlertTitleInBoldOnTheFirstLineOfTheQuote()
    {
        var result = TelegramMarkdownFormatter.Format(AlertDocument());

        Assert.Equal("> *Warning*\n> Mind the gap\\.", result);
    }

    [Fact]
    public void FormatUsesTheAlertTitlesItIsGiven()
    {
        var result = TelegramMarkdownFormatter.Format(AlertDocument(), static _ => "Осторожно!");

        Assert.Equal("> *Осторожно\\!*\n> Mind the gap\\.", result);
    }

    [Fact]
    public void FormatSelectionKeepsTheAlertTitleWhenItIsSelected()
    {
        var document = AlertDocument();
        var textMap = MarkdownDocumentTextMap.Create(document);

        var result = TelegramMarkdownFormatter.FormatSelection(document, new DocumentTextRange(0, textMap.Text.Length));

        Assert.Equal("> *Warning*\n> Mind the gap\\.", result);
    }

    [Fact]
    public void FormatSelectionOfTheAlertBodyOnlyLeavesTheTitleOut()
    {
        var document = AlertDocument();
        var textMap = MarkdownDocumentTextMap.Create(document);
        var start = textMap.Text.IndexOf("Mind", StringComparison.Ordinal);

        var result = TelegramMarkdownFormatter.FormatSelection(document, new DocumentTextRange(start, start + "Mind the gap".Length));

        Assert.Equal("> Mind the gap", result);
    }

    [Fact]
    public void FormatSelectionMeasuresOffsetsWithTheViewersAlertTitles()
    {
        var document = new RenderedMarkdownDocument(
        [
            new MarkdownQuoteBlock([new MarkdownParagraphBlock([new MarkdownTextInline("Body")])], MarkdownAlertKind.Note),
            new MarkdownParagraphBlock([new MarkdownTextInline("Tail")])
        ]);
        static string Titles(MarkdownAlertKind kind) => "Примечание";
        var textMap = MarkdownDocumentTextMap.Create(document, Titles);
        var start = textMap.Text.IndexOf("Tail", StringComparison.Ordinal);

        var result = TelegramMarkdownFormatter.FormatSelection(document, new DocumentTextRange(start, start + "Tail".Length), Titles);

        Assert.Equal("Tail", result);
    }

    [Fact]
    public void FormatWritesFootnoteLabelsInBracketsAndFootnotesAsANumberedList()
    {
        var result = TelegramMarkdownFormatter.Format(FootnoteDocument());

        Assert.Equal("Markdig\\[1\\] and Naiad\\[2\\]\\.\n\n1\\. Fast\\.\n2\\. In process\\.\n\nNo browser\\.", result);
    }

    [Fact]
    public void FormatSelectionKeepsTheFootnoteLabelOfTheSelectedWord()
    {
        var document = FootnoteDocument();

        var result = TelegramMarkdownFormatter.FormatSelection(document, new DocumentTextRange(0, "Markdig[1]".Length));

        Assert.Equal("Markdig\\[1\\]", result);
    }

    [Fact]
    public void FormatSelectionOfOneFootnoteKeepsItsNumber()
    {
        var document = FootnoteDocument();
        var textMap = MarkdownDocumentTextMap.Create(document);
        var start = textMap.Text.IndexOf("1. Fast.", StringComparison.Ordinal);

        var result = TelegramMarkdownFormatter.FormatSelection(document, new DocumentTextRange(start, start + "1. Fast.".Length));

        Assert.Equal("1\\. Fast\\.", result);
    }

    private static RenderedMarkdownDocument FootnoteDocument()
        => new(
        [
            new MarkdownParagraphBlock(
            [
                new MarkdownTextInline("Markdig"),
                new MarkdownFootnoteReferenceInline(1),
                new MarkdownTextInline(" and Naiad"),
                new MarkdownFootnoteReferenceInline(2),
                new MarkdownTextInline(".")
            ]),
            new MarkdownFootnotesBlock(
            [
                new MarkdownFootnote(1, [new MarkdownParagraphBlock([new MarkdownTextInline("Fast.")])]),
                new MarkdownFootnote(2,
                [
                    new MarkdownParagraphBlock([new MarkdownTextInline("In process.")]),
                    new MarkdownParagraphBlock([new MarkdownTextInline("No browser.")])
                ])
            ])
        ]);

    private static RenderedMarkdownDocument AlertDocument()
        => new(
        [
            new MarkdownQuoteBlock(
                [new MarkdownParagraphBlock([new MarkdownTextInline("Mind the gap.")])],
                MarkdownAlertKind.Warning)
        ]);

    private static RenderedMarkdownDocument TaskListDocument(bool isOrdered)
        => new(
        [
            new MarkdownListBlock(isOrdered,
            [
                new MarkdownListItem([new MarkdownParagraphBlock([new MarkdownTextInline("done")])], IsChecked: true),
                new MarkdownListItem([new MarkdownParagraphBlock([new MarkdownTextInline("open")])], IsChecked: false),
                new MarkdownListItem([new MarkdownParagraphBlock([new MarkdownTextInline("plain")])])
            ])
        ]);
}
