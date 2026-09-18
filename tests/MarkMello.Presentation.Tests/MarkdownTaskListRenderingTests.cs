using MarkMello.Domain;
using MarkMello.Infrastructure.Markdown;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Markdig разбирает <c>- [ ]</c> / <c>- [x]</c> в inline <c>TaskList</c>. Регрессия,
/// от которой защищают тесты: для него не было ветки в конвертере, inline молча
/// пропадал, и пункт выглядел как обычный «• текст» (с лишним пробелом в начале).
/// </summary>
public sealed class MarkdownTaskListRenderingTests
{
    [Fact]
    public void RenderReadsCheckboxStateIntoListItems()
    {
        var list = RenderList("""
            - [ ] open
            - [x] done
            - [X] done in capitals
            - regular
            """);

        Assert.False(list.IsOrdered);
        Assert.Collection(
            list.Items,
            item => AssertItem(item, false, "open"),
            item => AssertItem(item, true, "done"),
            item => AssertItem(item, true, "done in capitals"),
            item => AssertItem(item, null, "regular"));
    }

    [Fact]
    public void RenderReadsCheckboxStateInNestedList()
    {
        var list = RenderList("""
            - [ ] parent
              - [x] child
              - plain child
            """);

        var parent = Assert.Single(list.Items);
        Assert.False(parent.IsChecked);
        Assert.Equal("parent", ParagraphText(parent.Blocks[0]));

        var nested = Assert.IsType<MarkdownListBlock>(parent.Blocks[1]);
        Assert.Collection(
            nested.Items,
            item => AssertItem(item, true, "child"),
            item => AssertItem(item, null, "plain child"));
    }

    [Fact]
    public void RenderReadsCheckboxStateInOrderedList()
    {
        var list = RenderList("""
            1. [x] done
            2. [ ] open
            3. regular
            """);

        Assert.True(list.IsOrdered);
        Assert.Collection(
            list.Items,
            item => AssertItem(item, true, "done"),
            item => AssertItem(item, false, "open"),
            item => AssertItem(item, null, "regular"));
    }

    [Fact]
    public void RenderKeepsFormattingThatStartsRightAfterTheCheckbox()
    {
        var item = Assert.Single(RenderList("- [x] **bold** tail").Items);

        Assert.True(item.IsChecked);
        var paragraph = Assert.IsType<MarkdownParagraphBlock>(Assert.Single(item.Blocks));
        Assert.IsType<MarkdownStrongInline>(paragraph.Inlines[0]);
        Assert.Equal("bold tail", MarkdownDocumentTextMap.ExtractPlainText(paragraph.Inlines));
    }

    [Theory]
    [InlineData("see [x] and [ ] here")]
    [InlineData("see [X] and [ ] here")]
    public void RenderKeepsBracketsInTheMiddleOfAnItemAsWritten(string text)
    {
        var item = Assert.Single(RenderList("- " + text).Items);

        Assert.Null(item.IsChecked);
        Assert.Equal(text, ParagraphText(Assert.Single(item.Blocks)));
    }

    [Fact]
    public void RenderKeepsBracketsAtTheStartOfASecondParagraphAsWritten()
    {
        var item = Assert.Single(RenderList("""
            - [x] first paragraph

              [X] second paragraph and [ ] more
            """).Items);

        Assert.True(item.IsChecked);
        Assert.Collection(
            item.Blocks,
            block => Assert.Equal("first paragraph", ParagraphText(block)),
            block => Assert.Equal("[X] second paragraph and [ ] more", ParagraphText(block)));
    }

    [Fact]
    public void RenderKeepsBracketsInAHeadingInsideAnItemAsWritten()
    {
        var item = Assert.Single(RenderList("- # [X] heading").Items);

        Assert.Null(item.IsChecked);
        var heading = Assert.IsType<MarkdownHeadingBlock>(Assert.Single(item.Blocks));
        Assert.Equal("[X] heading", MarkdownDocumentTextMap.ExtractPlainText(heading.Inlines));
    }

    [Fact]
    public void RenderKeepsBracketsAsWrittenInASourceWithWindowsLineEndings()
    {
        var list = RenderList("- [x] done\r\n- see *[X]* here\r\n");

        Assert.Collection(
            list.Items,
            item => AssertItem(item, true, "done"),
            item => AssertItem(item, null, "see [X] here"));
    }

    [Theory]
    [InlineData("- [ ]", false)]
    [InlineData("- [x]", true)]
    public void RenderTurnsAnItemWithOnlyACheckboxIntoACheckboxWithoutText(string markdown, bool expectedChecked)
    {
        var item = Assert.Single(RenderList(markdown).Items);

        Assert.Equal(expectedChecked, item.IsChecked);
        Assert.Empty(item.Blocks);
    }

    [Fact]
    public void RenderedTaskListCopiesWithCheckboxesInsteadOfBullets()
    {
        var document = new MarkdigMarkdownDocumentRenderer().Render("""
            - [x] done
            - [ ] open
            - regular
            """);

        var textMap = MarkdownDocumentTextMap.Create(document);

        Assert.Equal("☑ done\n☐ open\n• regular", textMap.Text.TrimEnd('\n'));
    }

    private static MarkdownListBlock RenderList(string markdown)
    {
        var document = new MarkdigMarkdownDocumentRenderer().Render(markdown);

        return Assert.IsType<MarkdownListBlock>(Assert.Single(document.Blocks));
    }

    private static void AssertItem(MarkdownListItem item, bool? expectedChecked, string expectedText)
    {
        Assert.Equal(expectedChecked, item.IsChecked);
        Assert.Equal(expectedText, ParagraphText(Assert.Single(item.Blocks)));
    }

    private static string ParagraphText(MarkdownBlock block)
        => MarkdownDocumentTextMap.ExtractPlainText(Assert.IsType<MarkdownParagraphBlock>(block).Inlines);
}
