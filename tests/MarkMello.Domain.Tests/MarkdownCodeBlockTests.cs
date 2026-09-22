using MarkMello.Domain;

namespace MarkMello.Domain.Tests;

public sealed class MarkdownCodeBlockTests
{
    [Fact]
    public void NewBlockIsNotHighlighted()
    {
        var block = new MarkdownCodeBlock("csharp", "var x = 1;");

        Assert.Null(block.Tokens);
    }

    [Fact]
    public void TokensDoNotChangeTheText()
    {
        var block = new MarkdownCodeBlock("csharp", "var x = 1;");

        var highlighted = block with
        {
            Tokens = [new MarkdownCodeToken(0, 3, MarkdownCodeTokenKind.Keyword)],
        };

        Assert.Equal(block.Code, highlighted.Code);
        Assert.Equal(block.Info, highlighted.Info);
        Assert.Equal(MarkdownCodeTokenKind.Keyword, Assert.Single(highlighted.Tokens!).Kind);
    }

    [Fact]
    public void TokenEndIsStartPlusLength()
    {
        var token = new MarkdownCodeToken(4, 3, MarkdownCodeTokenKind.Constant);

        Assert.Equal(7, token.End);
    }
}
