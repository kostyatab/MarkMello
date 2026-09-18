using MarkMello.Domain;
using MarkMello.Infrastructure.Markdown;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Markdig разбирает сноски в inline <c>FootnoteLink</c> и блок <c>FootnoteGroup</c>.
/// Регрессия, от которой защищают тесты: для <c>FootnoteLink</c> не было ветки в
/// конвертере, и метка молча пропадала из текста, а группа уходила в общую ветку
/// контейнера — тексты сносок становились обычными абзацами в конце документа, без
/// номеров и без связи с метками.
/// </summary>
public sealed class MarkdownFootnoteRenderingTests
{
    [Fact]
    public void RenderKeepsTheReferenceAndCollectsTheFootnoteAtTheEnd()
    {
        var document = Render("""
            Markdig[^markdig] renders it.

            [^markdig]: A Markdown processor.
            """);

        Assert.Collection(
            document.Blocks,
            block => Assert.Equal(
                [new MarkdownTextInline("Markdig"), new MarkdownFootnoteReferenceInline(1), new MarkdownTextInline(" renders it.")],
                Assert.IsType<MarkdownParagraphBlock>(block).Inlines),
            block =>
            {
                var footnote = Assert.Single(Assert.IsType<MarkdownFootnotesBlock>(block).Footnotes);
                Assert.Equal(1, footnote.Number);
                Assert.Equal("A Markdown processor.", ParagraphText(Assert.Single(footnote.Blocks)));
            });
    }

    [Fact]
    public void RenderNumbersFootnotesByTheirFirstReference()
    {
        var document = Render("""
            First[^b], second[^a].

            [^a]: Defined first, referenced second.
            [^b]: Defined second, referenced first.
            """);

        Assert.Equal([1, 2], References(document));
        Assert.Collection(
            Footnotes(document).Footnotes,
            footnote =>
            {
                Assert.Equal(1, footnote.Number);
                Assert.Equal("Defined second, referenced first.", ParagraphText(Assert.Single(footnote.Blocks)));
            },
            footnote =>
            {
                Assert.Equal(2, footnote.Number);
                Assert.Equal("Defined first, referenced second.", ParagraphText(Assert.Single(footnote.Blocks)));
            });
    }

    [Fact]
    public void RenderGivesARepeatedReferenceTheSameNumberAndKeepsOneFootnote()
    {
        var document = Render("""
            Once[^note], other[^other], twice[^note].

            [^note]: Referenced twice.
            [^other]: Referenced once.
            """);

        Assert.Equal([1, 2, 1], References(document));
        Assert.Equal([1, 2], Footnotes(document).Footnotes.Select(static footnote => footnote.Number));
    }

    [Fact]
    public void RenderKeepsEveryParagraphOfAMultiParagraphFootnote()
    {
        var document = Render("""
            Text[^long].

            [^long]: First paragraph.

                Second paragraph, indented.
            """);

        var footnote = Assert.Single(Footnotes(document).Footnotes);
        Assert.Collection(
            footnote.Blocks,
            block => Assert.Equal("First paragraph.", ParagraphText(block)),
            block => Assert.Equal("Second paragraph, indented.", ParagraphText(block)));
    }

    [Fact]
    public void RenderDropsTheBackLinksMarkdigAddsToAFootnoteEndingInAList()
    {
        // Markdig дописывает обратные ссылки отдельным абзацем, если сноска
        // кончается не абзацем: пустого абзаца в модели быть не должно.
        var document = Render("""
            Text[^list].

            [^list]: Items:

                - one
                - two
            """);

        var footnote = Assert.Single(Footnotes(document).Footnotes);
        Assert.Collection(
            footnote.Blocks,
            block => Assert.Equal("Items:", ParagraphText(block)),
            block => Assert.Equal(2, Assert.IsType<MarkdownListBlock>(block).Items.Count));
    }

    [Fact]
    public void RenderKeepsAFootnoteWithOnlyAnImageAsAnImage()
    {
        var document = Render("""
            Text[^figure].

            [^figure]: ![Diagram](diagram.png)
            """);

        var footnote = Assert.Single(Footnotes(document).Footnotes);
        Assert.Equal("diagram.png", Assert.IsType<MarkdownImageBlock>(Assert.Single(footnote.Blocks)).Url);
    }

    [Fact]
    public void RenderMovesFootnotesToTheEndAndKeepsTheTextAfterTheirDefinitions()
    {
        var document = Render("""
            Before[^a].

            [^a]: The footnote.

            After the definition.
            """);

        Assert.Collection(
            document.Blocks,
            block => Assert.Equal("Before[1].", ParagraphText(block)),
            block => Assert.Equal("After the definition.", ParagraphText(block)),
            block => Assert.IsType<MarkdownFootnotesBlock>(block));
    }

    [Fact]
    public void RenderTakesTheSourceLinesOfTheFootnotesFromTheirDefinitions()
    {
        var document = Render("""
            Text[^a] and[^b].

            [^a]: First.

            [^b]: Second.
            """);

        var span = Footnotes(document).SourceSpan;

        Assert.NotNull(span);
        Assert.Equal(2, span.Value.StartLine);
        Assert.Equal(4, span.Value.EndLine);
    }

    [Fact]
    public void RenderDropsAFootnoteNobodyRefersTo()
    {
        var document = Render("""
            Text[^used].

            [^used]: Used.
            [^unused]: Not used.
            """);

        Assert.Equal([1], Footnotes(document).Footnotes.Select(static footnote => footnote.Number));
    }

    [Fact]
    public void RenderAddsNoFootnotesBlockWhenNoFootnoteIsReferenced()
    {
        var document = Render("""
            Just text.

            [^unused]: Not used.
            """);

        Assert.Equal("Just text.", ParagraphText(Assert.Single(document.Blocks)));
    }

    [Fact]
    public void RenderKeepsAReferenceWithoutADefinitionAsText()
    {
        var document = Render("Missing[^nope].");

        Assert.Equal("Missing[^nope].", ParagraphText(Assert.Single(document.Blocks)));
    }

    [Fact]
    public void RenderKeepsReferencesInsideEmphasisAndTableCells()
    {
        var document = Render("""
            **Bold[^a]**

            | Column |
            |--------|
            | Cell[^a] |

            [^a]: Footnote.
            """);

        var strong = Assert.IsType<MarkdownStrongInline>(Assert.Single(Assert.IsType<MarkdownParagraphBlock>(document.Blocks[0]).Inlines));
        Assert.Contains(new MarkdownFootnoteReferenceInline(1), strong.Inlines);

        var table = Assert.IsType<MarkdownTableBlock>(document.Blocks[1]);
        Assert.Contains(new MarkdownFootnoteReferenceInline(1), Assert.Single(Assert.Single(table.Rows)).Inlines);
    }

    [Fact]
    public void RenderedFootnotesCopyWithBracketedLabelsAndNumberedNotes()
    {
        var document = Render("""
            Markdig[^markdig] and diagrams[^diagrams], again Markdig[^markdig].

            [^markdig]: Markdig note.

            [^diagrams]: Diagrams note.

                Second paragraph.
            """);

        var textMap = MarkdownDocumentTextMap.Create(document);

        Assert.Equal(
            "Markdig[1] and diagrams[2], again Markdig[1].\n\n1. Markdig note.\n2. Diagrams note.\nSecond paragraph.",
            textMap.Text.TrimEnd('\n'));
    }

    private static RenderedMarkdownDocument Render(string markdown)
        => new MarkdigMarkdownDocumentRenderer().Render(markdown);

    private static MarkdownFootnotesBlock Footnotes(RenderedMarkdownDocument document)
        => Assert.IsType<MarkdownFootnotesBlock>(document.Blocks[^1]);

    private static int[] References(RenderedMarkdownDocument document)
        => Assert.IsType<MarkdownParagraphBlock>(document.Blocks[0]).Inlines
            .OfType<MarkdownFootnoteReferenceInline>()
            .Select(static reference => reference.Number)
            .ToArray();

    private static string ParagraphText(MarkdownBlock block)
        => MarkdownDocumentTextMap.ExtractPlainText(Assert.IsType<MarkdownParagraphBlock>(block).Inlines);
}
