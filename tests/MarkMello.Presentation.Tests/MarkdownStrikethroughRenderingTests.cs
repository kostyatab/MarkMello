using MarkMello.Domain;
using MarkMello.Infrastructure.Markdown;

namespace MarkMello.Presentation.Tests;

public sealed class MarkdownStrikethroughRenderingTests
{
    [Fact]
    public void RenderMapsDoubleTildeToStrikethroughInsteadOfStrong()
    {
        var inline = Assert.Single(RenderParagraphInlines("~~gone~~"));

        var strikethrough = Assert.IsType<MarkdownStrikethroughInline>(inline);
        Assert.Equal("gone", Assert.IsType<MarkdownTextInline>(Assert.Single(strikethrough.Inlines)).Text);
    }

    [Fact]
    public void RenderKeepsStrongInsideStrikethrough()
    {
        var strikethrough = Assert.IsType<MarkdownStrikethroughInline>(Assert.Single(RenderParagraphInlines("~~**x**~~")));

        var strong = Assert.IsType<MarkdownStrongInline>(Assert.Single(strikethrough.Inlines));
        Assert.Equal("x", Assert.IsType<MarkdownTextInline>(Assert.Single(strong.Inlines)).Text);
    }

    [Fact]
    public void RenderKeepsStrikethroughInsideStrong()
    {
        var strong = Assert.IsType<MarkdownStrongInline>(Assert.Single(RenderParagraphInlines("**~~x~~**")));

        var strikethrough = Assert.IsType<MarkdownStrikethroughInline>(Assert.Single(strong.Inlines));
        Assert.Equal("x", Assert.IsType<MarkdownTextInline>(Assert.Single(strikethrough.Inlines)).Text);
    }

    [Theory]
    [InlineData("**bold**")]
    [InlineData("__bold__")]
    public void RenderKeepsDoubleAsteriskAndUnderscoreAsStrong(string markdown)
    {
        var strong = Assert.IsType<MarkdownStrongInline>(Assert.Single(RenderParagraphInlines(markdown)));

        Assert.Equal("bold", Assert.IsType<MarkdownTextInline>(Assert.Single(strong.Inlines)).Text);
    }

    [Fact]
    public void RenderedStrikethroughIsPlainTextWithoutTildesInTextMap()
    {
        var document = new MarkdigMarkdownDocumentRenderer().Render("a ~~b **c**~~ d");

        var textMap = MarkdownDocumentTextMap.Create(document);

        Assert.Equal("a b c d", textMap.Text.TrimEnd('\n'));
    }

    private static IReadOnlyList<MarkdownInline> RenderParagraphInlines(string markdown)
    {
        var document = new MarkdigMarkdownDocumentRenderer().Render(markdown);

        return Assert.IsType<MarkdownParagraphBlock>(Assert.Single(document.Blocks)).Inlines;
    }
}
