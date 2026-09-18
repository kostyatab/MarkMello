using MarkMello.Domain;
using MarkMello.Infrastructure.Markdown;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Markdig разбирает GitHub alerts (<c>&gt; [!NOTE]</c> и др.) в <c>AlertBlock</c> —
/// наследника <c>QuoteBlock</c>. Регрессия, от которой защищают тесты: конвертер
/// попадал в ветку обычной цитаты, вид терялся, а маркер пропадал из текста, так
/// что все пять видов выглядели одинаковой курсивной цитатой.
/// </summary>
public sealed class MarkdownAlertRenderingTests
{
    [Theory]
    [InlineData("NOTE", MarkdownAlertKind.Note)]
    [InlineData("TIP", MarkdownAlertKind.Tip)]
    [InlineData("IMPORTANT", MarkdownAlertKind.Important)]
    [InlineData("WARNING", MarkdownAlertKind.Warning)]
    [InlineData("CAUTION", MarkdownAlertKind.Caution)]
    public void RenderReadsTheAlertKindAndDropsTheMarker(string marker, MarkdownAlertKind expectedKind)
    {
        var quote = RenderQuote($"> [!{marker}]\n> Useful information.");

        Assert.Equal(expectedKind, quote.AlertKind);
        Assert.Equal("Useful information.", ParagraphText(Assert.Single(quote.Blocks)));
    }

    [Theory]
    [InlineData("note", MarkdownAlertKind.Note)]
    [InlineData("Tip", MarkdownAlertKind.Tip)]
    [InlineData("wArNiNg", MarkdownAlertKind.Warning)]
    public void RenderReadsTheAlertKindInAnyCase(string marker, MarkdownAlertKind expectedKind)
    {
        var quote = RenderQuote($"> [!{marker}]\n> text");

        Assert.Equal(expectedKind, quote.AlertKind);
    }

    [Fact]
    public void RenderKeepsEveryBlockOfTheAlertBody()
    {
        var quote = RenderQuote("""
            > [!WARNING]
            > first line
            > second line
            >
            > - item
            """);

        Assert.Equal(MarkdownAlertKind.Warning, quote.AlertKind);
        Assert.Collection(
            quote.Blocks,
            block => Assert.Equal("first line second line", ParagraphText(block)),
            block => Assert.IsType<MarkdownListBlock>(block));
    }

    [Fact]
    public void RenderDropsTheEmptyParagraphLeftByAMarkerOnItsOwn()
    {
        var quote = RenderQuote("""
            > [!TIP]
            >
            > Text after a blank line.
            """);

        Assert.Equal(MarkdownAlertKind.Tip, quote.AlertKind);
        Assert.Equal("Text after a blank line.", ParagraphText(Assert.Single(quote.Blocks)));
    }

    [Fact]
    public void RenderTurnsAMarkerWithoutTextIntoAnEmptyAlert()
    {
        var quote = RenderQuote("> [!NOTE]");

        Assert.Equal(MarkdownAlertKind.Note, quote.AlertKind);
        Assert.Empty(quote.Blocks);
    }

    [Fact]
    public void RenderKeepsAnUnknownKindAsAPlainQuoteWithItsMarker()
    {
        var quote = RenderQuote("> [!FOO]\n> unknown kind");

        Assert.Null(quote.AlertKind);
        Assert.Equal("[!FOO] unknown kind", ParagraphText(Assert.Single(quote.Blocks)));
    }

    [Fact]
    public void RenderKeepsAnUnknownMarkerOnItsOwnAsAParagraph()
    {
        var quote = RenderQuote("""
            > [!Foo]
            >
            > text
            """);

        Assert.Null(quote.AlertKind);
        Assert.Collection(
            quote.Blocks,
            block => Assert.Equal("[!Foo]", ParagraphText(block)),
            block => Assert.Equal("text", ParagraphText(block)));
    }

    [Fact]
    public void RenderKeepsAPlainQuotePlain()
    {
        var quote = RenderQuote("> just a quote");

        Assert.Null(quote.AlertKind);
        Assert.Equal("just a quote", ParagraphText(Assert.Single(quote.Blocks)));
    }

    [Theory]
    [InlineData("> [!NOTE] text on the marker line", "[!NOTE] text on the marker line")]
    [InlineData("> outer\n>\n> > [!NOTE]\n> > inner", "[!NOTE] inner")]
    public void RenderKeepsAMarkerThatIsNotAnAlertAsWritten(string markdown, string expectedText)
    {
        // Как и на GitHub, alert — только цитата верхнего уровня, у которой маркер
        // стоит на строке один.
        var quote = RenderQuote(markdown);

        Assert.Null(quote.AlertKind);
        var text = quote.Blocks
            .Select(block => block is MarkdownQuoteBlock nested ? nested.Blocks[0] : block)
            .Select(ParagraphText)
            .Last();
        Assert.Equal(expectedText, text);
    }

    [Fact]
    public void RenderKeepsTheSourceLinesOfTheAlert()
    {
        var quote = RenderQuote("""
            > [!CAUTION]
            > line one
            > line two
            """);

        Assert.Equal(new MarkdownSourceSpan(0, 2), quote.SourceSpan);
    }

    [Fact]
    public void RenderedAlertCopiesWithItsTitle()
    {
        var document = new MarkdigMarkdownDocumentRenderer().Render("""
            Before.

            > [!IMPORTANT]
            > Key information.

            After.
            """);

        var textMap = MarkdownDocumentTextMap.Create(document);

        Assert.Equal("Before.\n\nImportant\nKey information.\n\nAfter.", textMap.Text.TrimEnd('\n'));
    }

    private static MarkdownQuoteBlock RenderQuote(string markdown)
    {
        var document = new MarkdigMarkdownDocumentRenderer().Render(markdown);

        return Assert.IsType<MarkdownQuoteBlock>(Assert.Single(document.Blocks));
    }

    private static string ParagraphText(MarkdownBlock block)
        => MarkdownDocumentTextMap.ExtractPlainText(Assert.IsType<MarkdownParagraphBlock>(block).Inlines);
}
