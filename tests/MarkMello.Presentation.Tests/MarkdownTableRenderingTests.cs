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

    private static MarkdownTableBlock RenderTable(string markdown)
    {
        var document = new MarkdigMarkdownDocumentRenderer().Render(markdown);

        return Assert.IsType<MarkdownTableBlock>(Assert.Single(document.Blocks));
    }
}
