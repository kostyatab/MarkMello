using MarkMello.Domain;
using MarkMello.Presentation.Clipboard;

namespace MarkMello.Presentation.Tests.Clipboard;

/// <summary>
/// MM-48: front matter стал отдельным типом блока, но копируется он ровно так же,
/// как таблица без строки заголовка, — и целиком, и выделением.
/// </summary>
public sealed class TelegramFrontMatterCopyTests
{
    [Fact]
    public void MarkdownV2CopyOfFrontMatterMatchesTheTableOutput()
    {
        Assert.Equal(
            "*id* · MM\\-48\n*empty*",
            TelegramMarkdownFormatter.Format(FrontMatter()));

        Assert.Equal(
            TelegramMarkdownFormatter.Format(AsTable()),
            TelegramMarkdownFormatter.Format(FrontMatter()));
    }

    [Fact]
    public void HtmlCopyOfFrontMatterMatchesTheTableOutput()
    {
        Assert.Equal(
            "<strong>id</strong> &middot; MM-48<br><strong>empty</strong>",
            TelegramMarkdownFormatter.FormatSelectionHtml(FrontMatter(), WholeDocument(FrontMatter())));

        Assert.Equal(
            TelegramMarkdownFormatter.FormatSelectionHtml(AsTable(), WholeDocument(AsTable())),
            TelegramMarkdownFormatter.FormatSelectionHtml(FrontMatter(), WholeDocument(FrontMatter())));
    }

    [Fact]
    public void CopyingASingleValueTakesOnlyTheSelectedCell()
    {
        var document = FrontMatter();
        var text = MarkdownDocumentTextMap.Create(document).Text;
        var start = text.IndexOf("MM-48", StringComparison.Ordinal);
        var selection = new DocumentTextRange(start, start + "MM-48".Length);

        Assert.Equal(
            TelegramMarkdownFormatter.FormatSelection(AsTable(), selection),
            TelegramMarkdownFormatter.FormatSelection(document, selection));
    }

    /// <summary>Значение копируется дословно: в MarkdownV2 его спецсимволы экранируются, в HTML — нет.</summary>
    [Fact]
    public void AValueWithMarkdownCharactersIsCopiedVerbatim()
    {
        var document = new RenderedMarkdownDocument(
        [
            new MarkdownFrontMatterBlock([new MarkdownFrontMatterEntry("title", "A_b *c*")])
        ]);

        Assert.Equal("*title* · A\\_b \\*c\\*", TelegramMarkdownFormatter.Format(document));
        Assert.Equal(
            "<strong>title</strong> &middot; A_b *c*",
            TelegramMarkdownFormatter.FormatSelectionHtml(document, WholeDocument(document)));
    }

    private static DocumentTextRange WholeDocument(RenderedMarkdownDocument document)
        => new(0, MarkdownDocumentTextMap.Create(document).Text.Length);

    private static RenderedMarkdownDocument FrontMatter()
        => new(
        [
            new MarkdownFrontMatterBlock(
            [
                new MarkdownFrontMatterEntry("id", "MM-48"),
                new MarkdownFrontMatterEntry("empty", string.Empty)
            ])
        ]);

    /// <summary>Тот же блок в виде MM-46: таблица без строки заголовка.</summary>
    private static RenderedMarkdownDocument AsTable()
        => new(
        [
            new MarkdownTableBlock(
                [],
                [
                    [
                        new MarkdownTableCell([new MarkdownStrongInline([new MarkdownTextInline("id")])]),
                        new MarkdownTableCell([new MarkdownTextInline("MM-48")])
                    ],
                    [
                        new MarkdownTableCell([new MarkdownStrongInline([new MarkdownTextInline("empty")])]),
                        new MarkdownTableCell([])
                    ]
                ])
        ]);
}
