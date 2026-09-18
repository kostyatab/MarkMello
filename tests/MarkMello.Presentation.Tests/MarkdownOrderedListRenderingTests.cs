using MarkMello.Domain;
using MarkMello.Infrastructure.Markdown;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// По CommonMark нумерованный список начинается с номера первого пункта. Регрессия,
/// от которой защищают тесты: конвертер не передавал в модель стартовый номер, и
/// список <c>7. / 8. / 9.</c> показывался и копировался как 1, 2, 3.
/// </summary>
public sealed class MarkdownOrderedListRenderingTests
{
    [Theory]
    [InlineData("7. Seventh\n8. Eighth\n9. Ninth", 7)]
    [InlineData("0. Zero\n1. One", 0)]
    [InlineData("003. Leading zeros\n004. Next", 3)]
    [InlineData("1. First\n2. Second", 1)]
    [InlineData("5. Numbers after the first\n1. do not matter", 5)]
    public void RenderReadsStartNumberOfOrderedList(string markdown, int expectedStart)
    {
        var list = RenderList(markdown);

        Assert.True(list.IsOrdered);
        Assert.Equal(expectedStart, list.StartNumber);
    }

    /// <summary>
    /// Markdig (ListExtras) переводит буквенный и римский маркер в число: «c.» → 3,
    /// «iv.» → 4. Буквенная и римская нумерация не поддерживается, такие списки
    /// нумеруются с 1, как и раньше, — иначе «c. / d.» внезапно стал бы «3. / 4.».
    /// </summary>
    [Theory]
    [InlineData("c. Third\nd. Fourth")]
    [InlineData("C. Third\nD. Fourth")]
    [InlineData("iv. Fourth\nv. Fifth")]
    [InlineData("IV. Fourth\nV. Fifth")]
    public void RenderStartsAlphaAndRomanListsFromOne(string markdown)
    {
        var list = RenderList(markdown);

        Assert.True(list.IsOrdered);
        Assert.Equal(1, list.StartNumber);
    }

    [Fact]
    public void RenderKeepsDefaultStartNumberForBulletList()
    {
        var list = RenderList("""
            - one
            - two
            """);

        Assert.False(list.IsOrdered);
        Assert.Equal(1, list.StartNumber);
    }

    [Fact]
    public void RenderReadsStartNumberOfNestedOrderedList()
    {
        // Список не с «1.» по CommonMark не прерывает абзац — без пустой строки
        // «5. child» стал бы продолжением «parent».
        var list = RenderList("""
            3. parent

               5. child
               6. child
            """);

        var parent = Assert.Single(list.Items);
        var nested = Assert.IsType<MarkdownListBlock>(parent.Blocks[1]);

        Assert.Equal(3, list.StartNumber);
        Assert.True(nested.IsOrdered);
        Assert.Equal(5, nested.StartNumber);
    }

    [Fact]
    public void RenderedListCopiesWithTheNumbersFromItsStart()
    {
        var document = new MarkdigMarkdownDocumentRenderer().Render("""
            7. Seventh
            8. Eighth
            9. Ninth
            """);

        var textMap = MarkdownDocumentTextMap.Create(document);

        Assert.Equal("7. Seventh\n8. Eighth\n9. Ninth", textMap.Text.TrimEnd('\n'));
    }

    private static MarkdownListBlock RenderList(string markdown)
    {
        var document = new MarkdigMarkdownDocumentRenderer().Render(markdown);

        return Assert.IsType<MarkdownListBlock>(Assert.Single(document.Blocks));
    }
}
