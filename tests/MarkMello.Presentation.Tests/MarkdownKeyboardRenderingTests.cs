using MarkMello.Domain;
using MarkMello.Infrastructure.Markdown;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// <c>&lt;kbd&gt;Ctrl&lt;/kbd&gt;</c> становится клавишей, а в тексте документа остаётся
/// обычным текстом. Регрессия: тег снимался, и клавиша выводилась как текст абзаца.
/// </summary>
public sealed class MarkdownKeyboardRenderingTests
{
    [Fact]
    public void KbdTagPairsBecomeKeys()
    {
        var inlines = RenderParagraph("Press <kbd>Ctrl</kbd> + <kbd>O</kbd> to open.");

        Assert.Collection(
            inlines,
            inline => Assert.Equal("Press ", Assert.IsType<MarkdownTextInline>(inline).Text),
            inline => Assert.Equal("Ctrl", Assert.IsType<MarkdownKeyboardInline>(inline).Text),
            inline => Assert.Equal(" + ", Assert.IsType<MarkdownTextInline>(inline).Text),
            inline => Assert.Equal("O", Assert.IsType<MarkdownKeyboardInline>(inline).Text),
            inline => Assert.Equal(" to open.", Assert.IsType<MarkdownTextInline>(inline).Text));
    }

    [Theory]
    [InlineData("<KBD>Esc</KBD>")]
    [InlineData("<kbd class=\"key\">Esc</kbd >")]
    [InlineData("<kbd>*Esc*</kbd>")]
    public void KeyIsThePlainTextBetweenTheTags(string markdown)
    {
        var key = Assert.IsType<MarkdownKeyboardInline>(Assert.Single(RenderParagraph("Press " + markdown).Skip(1)));

        Assert.Equal("Esc", key.Text);
    }

    /// <summary>
    /// Сочетание в HTML пишут и вложенными <c>kbd</c>: каждая внутренняя пара —
    /// отдельная клавиша, а внешние теги снимаются.
    /// </summary>
    [Fact]
    public void NestedKbdCombinationBecomesSeparateKeys()
    {
        var inlines = RenderParagraph("Copy with <kbd><kbd>Ctrl</kbd>+<kbd>C</kbd></kbd>.");

        Assert.Equal(
            ["Ctrl", "C"],
            inlines.OfType<MarkdownKeyboardInline>().Select(static key => key.Text));
        Assert.Equal("Copy with Ctrl+C.", MarkdownDocumentTextMap.ExtractPlainText(inlines));
    }

    [Theory]
    [InlineData("Press <kbd>Ctrl to open.", "Press Ctrl to open.")]
    [InlineData("Press Ctrl</kbd> to open.", "Press Ctrl to open.")]
    [InlineData("Empty <kbd></kbd>key.", "Empty key.")]
    public void KbdTagWithoutAPairIsDroppedAndItsTextStays(string markdown, string expectedText)
    {
        var inlines = RenderParagraph(markdown);

        Assert.DoesNotContain(inlines, static inline => inline is MarkdownKeyboardInline);
        Assert.Equal(expectedText, MarkdownDocumentTextMap.ExtractPlainText(inlines));
    }

    [Fact]
    public void KeyIsCopiedAsPlainText()
    {
        var document = new MarkdigMarkdownDocumentRenderer().Render("Press <kbd>Ctrl</kbd> + <kbd>O</kbd> to open.");

        var textMap = MarkdownDocumentTextMap.Create(document);

        // Абзац в тексте документа кончается разделителем блоков.
        Assert.Equal("Press Ctrl + O to open.", textMap.Text.TrimEnd('\n'));
    }

    private static IReadOnlyList<MarkdownInline> RenderParagraph(string markdown)
    {
        var document = new MarkdigMarkdownDocumentRenderer().Render(markdown);

        return Assert.IsType<MarkdownParagraphBlock>(Assert.Single(document.Blocks)).Inlines;
    }
}
