using MarkMello.Domain;
using MarkMello.Infrastructure.Markdown;
using MarkMello.Presentation.Views.Markdown;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Preview переиспользует контрол блока, только если компаратор видит тот же блок.
/// Новые узлы сравниваются по содержимому: повторный разбор того же текста —
/// равный блок, правка внутри узла — другой.
/// </summary>
public sealed class MarkdownBlockStructuralComparerTests
{
    [Theory]
    [InlineData("A <mark>**b**</mark>, H<sub>2</sub>O, mc<sup>2</sup>")]
    [InlineData("Term **bold**\n:   First.\n:   Second.\n\n    More.")]
    public void ReparsedBlockIsEqualWithTheSameHash(string markdown)
    {
        var first = RenderSingleBlock(markdown);
        var second = RenderSingleBlock(markdown);

        Assert.True(MarkdownBlockStructuralComparer.Instance.Equals(first, second));
        Assert.Equal(
            MarkdownBlockStructuralComparer.Instance.GetHashCode(first),
            MarkdownBlockStructuralComparer.Instance.GetHashCode(second));
    }

    [Theory]
    [InlineData("A <mark>b</mark>", "A <mark>c</mark>")]
    [InlineData("A <mark>b</mark>", "A <sup>b</sup>")]
    [InlineData("H<sub>2</sub>O", "H<sup>2</sup>O")]
    [InlineData("Term\n:   Definition", "Term\n:   Other")]
    [InlineData("Term\n:   Definition", "Other\n:   Definition")]
    [InlineData("Term\n:   Definition", "Term\n:   Definition\n:   Second")]
    [InlineData("Term\n:   Definition", "Term\nMore\n:   Definition")]
    public void ChangeInsideTheNodeMakesADifferentBlock(string before, string after)
    {
        Assert.False(MarkdownBlockStructuralComparer.Instance.Equals(RenderSingleBlock(before), RenderSingleBlock(after)));
    }

    [Fact]
    public void HighlightedCodeBlockDiffersFromThePlainOne()
    {
        var plain = new MarkdownCodeBlock("csharp", "var x = 1;");
        var highlighted = plain with { Tokens = [new MarkdownCodeToken(0, 3, MarkdownCodeTokenKind.Keyword)] };

        Assert.False(MarkdownBlockStructuralComparer.Instance.Equals(plain, highlighted));
        Assert.False(MarkdownBlockStructuralComparer.Instance.Equals(
            highlighted,
            plain with { Tokens = [new MarkdownCodeToken(0, 3, MarkdownCodeTokenKind.Type)] }));
    }

    [Fact]
    public void CodeBlocksWithEqualTokensAreEqual()
    {
        var first = new MarkdownCodeBlock("csharp", "var x = 1;")
        {
            Tokens = [new MarkdownCodeToken(0, 3, MarkdownCodeTokenKind.Keyword)],
        };
        var second = new MarkdownCodeBlock("csharp", "var x = 1;")
        {
            Tokens = [new MarkdownCodeToken(0, 3, MarkdownCodeTokenKind.Keyword)],
        };

        Assert.True(MarkdownBlockStructuralComparer.Instance.Equals(first, second));
        Assert.Equal(
            MarkdownBlockStructuralComparer.Instance.GetHashCode(first),
            MarkdownBlockStructuralComparer.Instance.GetHashCode(second));
    }

    private static MarkdownBlock RenderSingleBlock(string markdown)
        => Assert.Single(new MarkdigMarkdownDocumentRenderer().Render(markdown).Blocks);
}
