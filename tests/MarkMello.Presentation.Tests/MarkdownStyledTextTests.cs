using MarkMello.Domain;
using MarkMello.Presentation.Views.Markdown;

namespace MarkMello.Presentation.Tests;

public sealed class MarkdownStyledTextTests
{
    [Fact]
    public void FromInlinesCreatesSingleMergedLinkRangeForNestedLinkContent()
    {
        var styled = MarkdownStyledText.FromInlines(
        [
            new MarkdownTextInline("See "),
            new MarkdownLinkInline(
            [
                new MarkdownTextInline("docs"),
                new MarkdownStrongInline([new MarkdownTextInline("-api")])
            ],
            "https://example.com/docs",
            null),
            new MarkdownTextInline(" now")
        ]);

        Assert.Equal("See docs-api now", styled.Text);
        Assert.Single(styled.Links);
        Assert.Equal(new DocumentTextRange(4, 12), styled.Links[0].Range);
        Assert.Equal("https://example.com/docs", styled.Links[0].Url);
    }

    [Fact]
    public void FromInlinesMakesAFootnoteReferenceALinkToTheFootnote()
    {
        var styled = MarkdownStyledText.FromInlines(
        [
            new MarkdownStrongInline([new MarkdownTextInline("Word"), new MarkdownFootnoteReferenceInline(12)]),
            new MarkdownTextInline(" next")
        ]);

        Assert.Equal("Word[12] next", styled.Text);
        var reference = Assert.Single(styled.FootnoteReferences);
        Assert.Equal(new DocumentTextRange(4, 8), reference.Range);
        Assert.Equal(12, reference.Number);

        var link = Assert.Single(styled.Links);
        Assert.Equal(reference.Range, link.Range);
        Assert.Equal(new MarkdownFootnoteLinkTarget(12, IsBackReference: false), link.Footnote);

        // Верхний индекс рисуется своим стилем: жирный слова на метку не переходит.
        Assert.Equal(new DocumentTextRange(0, 4), Assert.Single(styled.Spans).Range);
    }

    [Fact]
    public void ForFootnoteMarkerMakesTheNumberALinkBackToTheReference()
    {
        var styled = MarkdownStyledText.ForFootnoteMarker(3);

        Assert.Equal("3. ", styled.Text);
        var link = Assert.Single(styled.Links);
        Assert.Equal(new DocumentTextRange(0, 2), link.Range);
        Assert.Equal(new MarkdownFootnoteLinkTarget(3, IsBackReference: true), link.Footnote);
        Assert.Equal(string.Empty, link.Url);
    }

    [Fact]
    public void FromInlinesMarksStrikethroughSpanWithoutBold()
    {
        var styled = MarkdownStyledText.FromInlines(
        [
            new MarkdownTextInline("was "),
            new MarkdownStrikethroughInline([new MarkdownTextInline("10")]),
            new MarkdownTextInline(" now 8")
        ]);

        Assert.Equal("was 10 now 8", styled.Text);
        var span = Assert.Single(styled.Spans);
        Assert.Equal(new DocumentTextRange(4, 6), span.Range);
        Assert.True(span.Style.IsStrikethrough);
        Assert.False(span.Style.IsBold);
    }

    [Fact]
    public void FromInlinesCombinesStrikethroughWithNestedStrong()
    {
        var styled = MarkdownStyledText.FromInlines(
        [
            new MarkdownStrikethroughInline(
            [
                new MarkdownTextInline("a"),
                new MarkdownStrongInline([new MarkdownTextInline("b")])
            ])
        ]);

        Assert.Collection(
            styled.Spans,
            span =>
            {
                Assert.Equal(new DocumentTextRange(0, 1), span.Range);
                Assert.Equal(MarkdownInlineStyleState.Default with { IsStrikethrough = true }, span.Style);
            },
            span =>
            {
                Assert.Equal(new DocumentTextRange(1, 2), span.Range);
                Assert.Equal(MarkdownInlineStyleState.Default with { IsStrikethrough = true, IsBold = true }, span.Style);
            });
    }

    [Fact]
    public void FromInlinesFallsBackToUrlWhenLinkHasNoLabel()
    {
        var styled = MarkdownStyledText.FromInlines(
        [
            new MarkdownLinkInline(Array.Empty<MarkdownInline>(), "https://example.com", null)
        ]);

        Assert.Equal("https://example.com", styled.Text);
        Assert.Single(styled.Links);
        Assert.Equal(new DocumentTextRange(0, styled.Text.Length), styled.Links[0].Range);
    }

    [Fact]
    public void FromInlinesCreatesAtomicImageSpanForDataUriImage()
    {
        const string dataUri = "data:image/png;base64,AQIDBA==";
        var styled = MarkdownStyledText.FromInlines(
        [
            new MarkdownTextInline("Before "),
            new MarkdownImageInline(dataUri, null, null),
            new MarkdownTextInline(" after")
        ]);

        Assert.Equal("Before image after", styled.Text);
        var image = Assert.Single(styled.Images);
        Assert.Equal(new DocumentTextRange(7, 12), image.Range);
        Assert.Equal(dataUri, image.Url);
        Assert.Equal("image", image.PlaceholderText);

        var model = MarkdownDisplayLayoutModel.Create(styled);
        var imageSegment = Assert.Single(model.Segments, segment => segment.Kind == MarkdownDisplaySegmentKind.Image);
        Assert.Equal(1, imageSegment.DisplayLength);
        Assert.Equal(image.Range, imageSegment.CanonicalRange);
        Assert.Equal(image.Range.Start, model.GetCanonicalCaretForDisplayCaret(imageSegment.DisplayStart));
        Assert.Equal(image.Range.End, model.GetCanonicalCaretForDisplayCaret(imageSegment.DisplayEnd));
        Assert.Equal(imageSegment.DisplayStart, model.GetDisplayStartForCanonicalCaret(image.Range.Start + 2));
        Assert.Equal(imageSegment.DisplayEnd, model.GetDisplayEndForCanonicalCaret(image.Range.Start + 2));
    }
}
