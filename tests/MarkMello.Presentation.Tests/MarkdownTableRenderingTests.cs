using MarkMello.Domain;
using MarkMello.Infrastructure.Markdown;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Выравнивание колонок из строки-разделителя таблицы (<c>:---</c>, <c>:---:</c>,
/// <c>---:</c>) попадает в модель. Регрессия: конвертер не читал
/// <c>TableColumnDefinition.Alignment</c>, и все колонки рисовались по левому краю.
/// </summary>
public sealed class MarkdownTableRenderingTests
{
    [Fact]
    public void RenderReadsColumnAlignmentFromTheDelimiterRow()
    {
        var table = RenderTable("""
            | Left | Centered | Right | Plain |
            |:---|:---:|---:|---|
            | a | b | c | d |
            """);

        Assert.Equal(
            [
                MarkdownTableColumnAlignment.Left,
                MarkdownTableColumnAlignment.Center,
                MarkdownTableColumnAlignment.Right,
                MarkdownTableColumnAlignment.Left
            ],
            Enumerable.Range(0, 4).Select(table.GetColumnAlignment));
    }

    [Fact]
    public void RenderReadsColumnAlignmentOfATableWithoutOuterPipes()
    {
        var table = RenderTable("""
            Name | Amount
            ---- | -----:
            tea | 1.50
            """);

        Assert.Equal(MarkdownTableColumnAlignment.Left, table.GetColumnAlignment(0));
        Assert.Equal(MarkdownTableColumnAlignment.Right, table.GetColumnAlignment(1));
    }

    [Fact]
    public void ColumnWithoutAnAlignmentEntryIsLeftAligned()
    {
        var table = new MarkdownTableBlock(
            [new MarkdownTableCell([new MarkdownTextInline("A")])],
            [],
            [MarkdownTableColumnAlignment.Right]);

        Assert.Equal(MarkdownTableColumnAlignment.Right, table.GetColumnAlignment(0));
        Assert.Equal(MarkdownTableColumnAlignment.Left, table.GetColumnAlignment(1));
        Assert.Equal(MarkdownTableColumnAlignment.Left, table.GetColumnAlignment(-1));
        Assert.Empty(new MarkdownTableBlock([], []).ColumnAlignments);
    }

    /// <summary>
    /// По GFM <c>\|</c> в ячейке — пайп, который не делит строку на ячейки, в том
    /// числе внутри code span: <c>`a \| b`</c> показывается как <c>a | b</c>.
    /// Регрессия: код в ячейке выводился с обратным слешем.
    /// </summary>
    [Fact]
    public void EscapedPipeInsideCodeSpanInACellIsShownWithoutTheBackslash()
    {
        var table = RenderTable("""
            | Syntax | Notes |
            |---|---|
            | `a \| b` | x \| y |
            """);

        var cells = Assert.Single(table.Rows);
        Assert.Equal(2, cells.Count);
        Assert.Equal("a | b", Assert.IsType<MarkdownCodeInline>(Assert.Single(cells[0].Inlines)).Code);
        Assert.Equal("x | y", string.Concat(cells[1].Inlines.Cast<MarkdownTextInline>().Select(static text => text.Text)));
    }

    [Fact]
    public void BackslashInCodeSpanInACellThatDoesNotEscapeAPipeStays()
    {
        var table = RenderTable("""
            | Path |
            |---|
            | `C:\temp\n` |
            """);

        var cell = Assert.Single(Assert.Single(table.Rows));
        Assert.Equal(@"C:\temp\n", Assert.IsType<MarkdownCodeInline>(Assert.Single(cell.Inlines)).Code);
    }

    [Fact]
    public void EscapedPipeInsideCodeSpanOutsideATableKeepsTheBackslash()
    {
        var document = new MarkdigMarkdownDocumentRenderer().Render(@"Text `a \| b` text");

        var paragraph = Assert.IsType<MarkdownParagraphBlock>(Assert.Single(document.Blocks));
        Assert.Equal(@"a \| b", Assert.IsType<MarkdownCodeInline>(paragraph.Inlines[1]).Code);
    }

    private static MarkdownTableBlock RenderTable(string markdown)
    {
        var document = new MarkdigMarkdownDocumentRenderer().Render(markdown);

        return Assert.IsType<MarkdownTableBlock>(Assert.Single(document.Blocks));
    }
}
