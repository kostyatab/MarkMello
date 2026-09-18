using MarkMello.Domain;
using MarkMello.Presentation.Views.Markdown;

namespace MarkMello.Presentation.Tests;

public sealed class MarkdownDisplayLayoutModelTests
{
    [Fact]
    public void CreateWrapsInlineCodeWithDisplayOnlyPaddingMarkers()
    {
        var styled = MarkdownStyledText.FromInlines(
        [
            new MarkdownTextInline("See "),
            new MarkdownCodeInline("docs"),
            new MarkdownTextInline(" now")
        ]);

        var model = MarkdownDisplayLayoutModel.Create(styled);

        Assert.Equal(styled.Text.Length + 2, model.DisplayLength);
        Assert.Single(model.CodeBoxes);

        var codeBox = model.CodeBoxes[0];
        Assert.Equal(new DocumentTextRange(4, 8), codeBox.CanonicalRange);
        Assert.Equal(4, model.GetDisplayStartForCanonicalCaret(4));
        Assert.Equal(10, model.GetDisplayEndForCanonicalCaret(8));

        Assert.Equal(4, model.GetCanonicalCaretForDisplayCaret(4));
        Assert.Equal(4, model.GetCanonicalCaretForDisplayCaret(5));
        Assert.Equal(8, model.GetCanonicalCaretForDisplayCaret(9));
        Assert.Equal(8, model.GetCanonicalCaretForDisplayCaret(10));
    }

    [Fact]
    public void CreateDrawsAFootnoteReferenceAsOneCharacter()
    {
        var styled = MarkdownStyledText.FromInlines(
        [
            new MarkdownTextInline("Word"),
            new MarkdownFootnoteReferenceInline(12),
            new MarkdownTextInline(" next")
        ]);

        var model = MarkdownDisplayLayoutModel.Create(styled);

        // «[12]» в тексте — один символ на экране.
        Assert.Equal("Word[12] next".Length - 3, model.DisplayLength);
        var segment = Assert.Single(model.Segments, static segment => segment.Kind == MarkdownDisplaySegmentKind.FootnoteReference);
        Assert.Equal("12", segment.Text);
        Assert.Equal(4, segment.DisplayStart);
        Assert.Equal(new DocumentTextRange(4, 8), segment.CanonicalRange);
        Assert.Equal(4, model.GetCanonicalCaretForDisplayCaret(4));
        Assert.Equal(8, model.GetCanonicalCaretForDisplayCaret(5));
    }

    [Fact]
    public void CreateSelectsAFootnoteReferenceWholeButNotTheWordNextToIt()
    {
        var styled = MarkdownStyledText.FromInlines(
        [
            new MarkdownTextInline("Word"),
            new MarkdownFootnoteReferenceInline(12),
            new MarkdownTextInline(" next")
        ]);

        var model = MarkdownDisplayLayoutModel.Create(styled);

        // Граница выделения внутри «[12]» захватывает метку целиком.
        Assert.Equal(4, model.GetDisplayStartForCanonicalCaret(6));
        Assert.Equal(5, model.GetDisplayEndForCanonicalCaret(6));

        // Выделение «Word» и « next» вплотную к метке её не захватывает.
        Assert.Equal(4, model.GetDisplayEndForCanonicalCaret(4));
        Assert.Equal(5, model.GetDisplayStartForCanonicalCaret(8));
    }
}
