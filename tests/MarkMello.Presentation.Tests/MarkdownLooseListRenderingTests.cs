using MarkMello.Domain;
using MarkMello.Infrastructure.Markdown;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// По CommonMark список loose, если его пункты или блоки внутри пункта разделены
/// пустыми строками, иначе tight. Регрессия: конвертер не передавал это в модель,
/// и каждый список рисовался с абзацными отступами, как loose.
/// </summary>
public sealed class MarkdownLooseListRenderingTests
{
    [Theory]
    [InlineData("- One\n- Two\n- Three", false)]
    [InlineData("1. One\n2. Two", false)]
    [InlineData("- One\n  - Nested\n- Two", false)]
    [InlineData("- One\n\n- Two", true)]
    [InlineData("1. One\n\n   Second paragraph of one.\n2. Two", true)]
    public void RenderReadsWhetherTheListIsLoose(string markdown, bool expectedLoose)
    {
        var document = new MarkdigMarkdownDocumentRenderer().Render(markdown);

        var list = Assert.IsType<MarkdownListBlock>(Assert.Single(document.Blocks));
        Assert.Equal(expectedLoose, list.IsLoose);
    }

    [Fact]
    public void NestedListKeepsItsOwnTightness()
    {
        var document = new MarkdigMarkdownDocumentRenderer().Render("- One\n  - Nested\n  - Nested\n\n- Two");

        var list = Assert.IsType<MarkdownListBlock>(Assert.Single(document.Blocks));
        var nested = Assert.IsType<MarkdownListBlock>(list.Items[0].Blocks[1]);
        Assert.True(list.IsLoose);
        Assert.False(nested.IsLoose);
    }
}
