using MarkMello.Domain;
using MarkMello.Infrastructure.Markdown;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// <c>&lt;mark&gt;</c>, <c>&lt;sub&gt;</c> и <c>&lt;sup&gt;</c> становятся узлами модели с
/// вложенными инлайнами, как на GitHub. Регрессия: теги снимались, и оформление
/// терялось.
/// </summary>
public sealed class MarkdownHighlightAndIndexRenderingTests
{
    [Fact]
    public void TagPairsBecomeHighlightSubscriptAndSuperscript()
    {
        var inlines = RenderParagraph("A <mark>note</mark>, H<sub>2</sub>O and mc<sup>2</sup>.");

        Assert.Collection(
            inlines,
            inline => Assert.Equal("A ", Assert.IsType<MarkdownTextInline>(inline).Text),
            inline => Assert.Equal("note", PlainText(Assert.IsType<MarkdownHighlightInline>(inline).Inlines)),
            inline => Assert.Equal(", H", Assert.IsType<MarkdownTextInline>(inline).Text),
            inline => Assert.Equal("2", PlainText(Assert.IsType<MarkdownSubscriptInline>(inline).Inlines)),
            inline => Assert.Equal("O and mc", Assert.IsType<MarkdownTextInline>(inline).Text),
            inline => Assert.Equal("2", PlainText(Assert.IsType<MarkdownSuperscriptInline>(inline).Inlines)),
            inline => Assert.Equal(".", Assert.IsType<MarkdownTextInline>(inline).Text));
    }

    [Theory]
    [InlineData("<MARK>x</MARK>")]
    [InlineData("<mark class=\"hl\">x</mark >")]
    public void TagCaseAndAttributesDoNotMatter(string markdown)
    {
        var highlight = Assert.IsType<MarkdownHighlightInline>(Assert.Single(RenderParagraph("A " + markdown).Skip(1)));

        Assert.Equal("x", PlainText(highlight.Inlines));
    }

    [Fact]
    public void FormattingInsideKeepsItsNodes()
    {
        var highlight = Assert.IsType<MarkdownHighlightInline>(
            Assert.Single(RenderParagraph("A <mark>**bold** and `code` and <sup>1</sup></mark>").Skip(1)));

        Assert.Collection(
            highlight.Inlines,
            inline => Assert.IsType<MarkdownStrongInline>(inline),
            inline => Assert.IsType<MarkdownTextInline>(inline),
            inline => Assert.Equal("code", Assert.IsType<MarkdownCodeInline>(inline).Code),
            inline => Assert.IsType<MarkdownTextInline>(inline),
            inline => Assert.Equal("1", PlainText(Assert.IsType<MarkdownSuperscriptInline>(inline).Inlines)));
    }

    [Fact]
    public void NestedPairsOfTheSameTagCloseInOrder()
    {
        var outer = Assert.IsType<MarkdownSubscriptInline>(
            Assert.Single(RenderParagraph("x<sub>a<sub>b</sub>c</sub>").Skip(1)));

        Assert.Collection(
            outer.Inlines,
            inline => Assert.Equal("a", Assert.IsType<MarkdownTextInline>(inline).Text),
            inline => Assert.Equal("b", PlainText(Assert.IsType<MarkdownSubscriptInline>(inline).Inlines)),
            inline => Assert.Equal("c", Assert.IsType<MarkdownTextInline>(inline).Text));
    }

    [Theory]
    [InlineData("A <mark>note here.", "A note here.")]
    [InlineData("A note</mark> here.", "A note here.")]
    [InlineData("H<sub>2O", "H2O")]
    [InlineData("Empty <sup></sup>index.", "Empty index.")]
    public void TagWithoutAPairIsDroppedAndItsTextStays(string markdown, string expectedText)
    {
        var inlines = RenderParagraph(markdown);

        Assert.DoesNotContain(inlines, static inline => inline is MarkdownHighlightInline or MarkdownSubscriptInline or MarkdownSuperscriptInline);
        Assert.Equal(expectedText, MarkdownDocumentTextMap.ExtractPlainText(inlines));
    }

    /// <summary>
    /// Регрессия: пара искалась заново от каждого непарного тега, и абзац с
    /// несколькими десятками голых <c>&lt;sub&gt;</c> разбирался экспоненциально
    /// долго — окно зависало при открытии файла.
    /// </summary>
    [Fact]
    public void ManyUnpairedTagsInOneParagraphAreParsedAtOnce()
    {
        var markdown = "Tags " + string.Concat(Enumerable.Repeat("<sub> and <sup> and <mark> and <kbd> ", 200));

        var inlines = RenderParagraph(markdown);

        Assert.DoesNotContain(inlines, static inline => inline is MarkdownHighlightInline or MarkdownSubscriptInline or MarkdownSuperscriptInline or MarkdownKeyboardInline);
        Assert.StartsWith("Tags  and  and  and ", MarkdownDocumentTextMap.ExtractPlainText(inlines), StringComparison.Ordinal);
    }

    /// <summary>
    /// Глубокая вложенность не роняет разбор: пары глубже предела снимаются, текст
    /// остаётся.
    /// </summary>
    [Fact]
    public void DeeplyNestedTagsKeepTheirText()
    {
        const int depth = 20_000;
        var markdown = "x" + string.Concat(Enumerable.Repeat("<sub>", depth)) + "2" + string.Concat(Enumerable.Repeat("</sub>", depth));

        var inlines = RenderParagraph(markdown);

        Assert.Equal("x2", MarkdownDocumentTextMap.ExtractPlainText(inlines));
        Assert.IsType<MarkdownSubscriptInline>(inlines[1]);
    }

    [Fact]
    public void CrossingPairsKeepTheOuterOneAndDropTheOther()
    {
        var inlines = RenderParagraph("a <mark>b<sub>c</mark>d</sub> e");

        var highlight = Assert.Single(inlines.OfType<MarkdownHighlightInline>());
        Assert.Equal("bc", PlainText(highlight.Inlines));
        Assert.DoesNotContain(inlines, static inline => inline is MarkdownSubscriptInline);
        Assert.Equal("a bcd e", MarkdownDocumentTextMap.ExtractPlainText(inlines));
    }

    [Theory]
    [InlineData("Write ==m== here.")]
    [InlineData("Use ^u^ here.")]
    public void MarkdownSyntaxForHighlightAndSuperscriptStaysText(string markdown)
    {
        var inlines = RenderParagraph(markdown);

        Assert.Equal(markdown, Assert.IsType<MarkdownTextInline>(Assert.Single(Merge(inlines))).Text);
    }

    [Fact]
    public void SingleTildeStaysStrikethrough()
    {
        var inlines = RenderParagraph("a ~x~ b");

        Assert.Contains(inlines, static inline => inline is MarkdownStrikethroughInline);
    }

    [Fact]
    public void TagsAreCopiedAsPlainText()
    {
        var document = new MarkdigMarkdownDocumentRenderer().Render("<mark>H<sub>2</sub>O</mark> and mc<sup>2</sup>");

        Assert.Equal("H2O and mc2", MarkdownDocumentTextMap.Create(document).Text.TrimEnd('\n'));
    }

    private static IReadOnlyList<MarkdownInline> RenderParagraph(string markdown)
    {
        var document = new MarkdigMarkdownDocumentRenderer().Render(markdown);

        return Assert.IsType<MarkdownParagraphBlock>(Assert.Single(document.Blocks)).Inlines;
    }

    /// <summary>Markdig дробит текст на куски; склеиваем соседние текстовые узлы.</summary>
    private static IReadOnlyList<MarkdownInline> Merge(IReadOnlyList<MarkdownInline> inlines)
        => inlines.All(static inline => inline is MarkdownTextInline)
            ? [new MarkdownTextInline(string.Concat(inlines.Cast<MarkdownTextInline>().Select(static text => text.Text)))]
            : inlines;

    private static string PlainText(IReadOnlyList<MarkdownInline> inlines)
        => MarkdownDocumentTextMap.ExtractPlainText(inlines);
}
