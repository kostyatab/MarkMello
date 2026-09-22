using MarkMello.Domain;
using MarkMello.Infrastructure.Markdown;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Список определений (<c>Термин</c> и <c>:   Определение</c>) — свой блок модели.
/// Регрессия: термин становился абзацем простым текстом и терял оформление, а
/// определения — обычными абзацами.
/// </summary>
public sealed class MarkdownDefinitionListRenderingTests
{
    [Fact]
    public void TermAndDefinitionBecomeADefinitionList()
    {
        var list = RenderDefinitionList("Term\n\n:   Definition");

        var item = Assert.Single(list.Items);
        Assert.Equal("Term", PlainText(Assert.Single(item.Terms).Inlines));
        var definition = Assert.Single(item.Definitions);
        Assert.Equal("Definition", PlainText(Assert.IsType<MarkdownParagraphBlock>(Assert.Single(definition.Blocks)).Inlines));
    }

    [Fact]
    public void TermKeepsItsInlineFormatting()
    {
        var list = RenderDefinitionList("**Bold** `code` <mark>term</mark>\n:   Definition");

        var term = Assert.Single(Assert.Single(list.Items).Terms);
        Assert.IsType<MarkdownStrongInline>(term.Inlines[0]);
        Assert.Contains(term.Inlines, static inline => inline is MarkdownCodeInline);
        Assert.Contains(term.Inlines, static inline => inline is MarkdownHighlightInline);
    }

    [Fact]
    public void EveryColonStartsAnotherDefinitionOfTheSameTerm()
    {
        var list = RenderDefinitionList("Apple\n:   A fruit.\n:   A company.\n\nPear\n:   Also a fruit.");

        Assert.Collection(
            list.Items,
            item =>
            {
                Assert.Equal(["Apple"], item.Terms.Select(static term => PlainText(term.Inlines)));
                Assert.Equal(["A fruit.", "A company."], item.Definitions.Select(DefinitionText));
            },
            item =>
            {
                Assert.Equal(["Pear"], item.Terms.Select(static term => PlainText(term.Inlines)));
                Assert.Equal(["Also a fruit."], item.Definitions.Select(DefinitionText));
            });
    }

    [Fact]
    public void SeveralLinesBeforeTheColonAreSeveralTerms()
    {
        var list = RenderDefinitionList("Pear\nQuince\n:   Fruits.");

        var item = Assert.Single(list.Items);
        Assert.Equal(["Pear", "Quince"], item.Terms.Select(static term => PlainText(term.Inlines)));
        Assert.Equal(["Fruits."], item.Definitions.Select(DefinitionText));
    }

    [Fact]
    public void DefinitionKeepsSeveralParagraphs()
    {
        var list = RenderDefinitionList("Term\n\n:   First.\n\n    Second.");

        var definition = Assert.Single(Assert.Single(list.Items).Definitions);
        Assert.Equal(
            ["First.", "Second."],
            definition.Blocks.Select(static block => PlainText(Assert.IsType<MarkdownParagraphBlock>(block).Inlines)));
    }

    [Fact]
    public void DefinitionListIsCopiedAsTermLineAndDefinitionLinesBelow()
    {
        var document = new MarkdigMarkdownDocumentRenderer().Render("Apple\n:   A fruit.\n:   A company.");

        Assert.Equal("Apple\nA fruit.\nA company.", MarkdownDocumentTextMap.Create(document).Text.TrimEnd('\n'));
    }

    private static MarkdownDefinitionListBlock RenderDefinitionList(string markdown)
    {
        var document = new MarkdigMarkdownDocumentRenderer().Render(markdown);

        return Assert.IsType<MarkdownDefinitionListBlock>(Assert.Single(document.Blocks));
    }

    private static string DefinitionText(MarkdownDefinition definition)
        => string.Join("\n", definition.Blocks.Select(MarkdownDocumentTextMap.ExtractPlainText));

    private static string PlainText(IReadOnlyList<MarkdownInline> inlines)
        => MarkdownDocumentTextMap.ExtractPlainText(inlines);
}
