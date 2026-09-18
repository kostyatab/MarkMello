using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using MarkMello.Presentation.Views.Markdown;

namespace MarkMello.Presentation.Tests;

[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class MarkdownFormattedTextLayoutTests
{
    private readonly AvaloniaHeadlessFixture _fixture;

    public MarkdownFormattedTextLayoutTests(AvaloniaHeadlessFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public Task LayoutCreatesOneVisualLinePerExplicitLineBreak()
    {
        return _fixture.Session.Dispatch(() =>
        {
            using var layout = CreateLayout("first\nsecond\nthird");

            Assert.Equal(3, layout.GetLineMetrics().Count);
        }, CancellationToken.None);
    }

    [Fact]
    public Task CaretHitTestMapsVisualLinesToCanonicalLineStarts()
    {
        return _fixture.Session.Dispatch(() =>
        {
            const string text = "first\nsecond\nthird";
            using var layout = CreateLayout(text);
            var lines = layout.GetLineMetrics();

            Assert.Equal(text.IndexOf("second", StringComparison.Ordinal), layout.GetCanonicalCaretOffset(GetLineStartPoint(lines[1])));
            Assert.Equal(text.IndexOf("third", StringComparison.Ordinal), layout.GetCanonicalCaretOffset(GetLineStartPoint(lines[2])));
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(TextAlignment.Center)]
    [InlineData(TextAlignment.Right)]
    public Task AlignedLinesStartAtTheirOffsetAndHitTestFromIt(TextAlignment alignment)
    {
        return _fixture.Session.Dispatch(() =>
        {
            const string text = "short\na longer line";
            const double width = 400;
            using var layout = CreateLayout(text, alignment, width);
            var lines = layout.GetLineMetrics();

            foreach (var line in lines)
            {
                var expectedLeft = alignment == TextAlignment.Right
                    ? width - line.Bounds.Width
                    : (width - line.Bounds.Width) / 2;
                Assert.Equal(expectedLeft, line.Bounds.X, 1);
            }

            var first = lines[0].Bounds;
            var second = lines[1].Bounds;
            var middleY = first.Y + first.Height / 2;

            // Правее начала строки — первый символ, а не символ под тем же X у
            // строки по левому краю.
            Assert.Equal(0, layout.GetCanonicalCaretOffset(new Point(first.X + 1, middleY)));
            Assert.Equal(text.IndexOf('\n', StringComparison.Ordinal), layout.GetCanonicalCaretOffset(new Point(first.Right - 0.5, middleY)));
            Assert.Equal(text.IndexOf('a', StringComparison.Ordinal), layout.GetCanonicalCaretOffset(new Point(second.X + 1, second.Y + second.Height / 2)));

            // Пустое место слева от выровненной строки — не текст.
            Assert.False(layout.IsPointInsideText(new Point(first.X - 2, middleY)));
            Assert.True(layout.IsPointInsideText(new Point(first.X + 2, middleY)));

            var selection = Assert.Single(layout.GetSelectionRects(new(0, 5)));
            Assert.Equal(first.X, selection.X, 1);
        }, CancellationToken.None);
    }

    [Fact]
    public Task StrikethroughStyleDrawsStrikethroughInTextForegroundWithoutBold()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var factory = CreatePropertiesFactory(linkDecorations: null);

            var properties = factory.Get(MarkdownInlineStyleState.Default with { IsStrikethrough = true });

            var decoration = Assert.Single(properties.TextDecorations!);
            Assert.Equal(TextDecorationLocation.Strikethrough, decoration.Location);
            Assert.Null(decoration.Stroke);
            Assert.Equal(FontWeight.Normal, properties.Typeface.Weight);
        }, CancellationToken.None);
    }

    [Fact]
    public Task StrikethroughLinkKeepsLinkUnderlineAndAddsStrikethrough()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var factory = CreatePropertiesFactory(new TextDecorationCollection
            {
                new TextDecoration { Location = TextDecorationLocation.Underline }
            });

            var properties = factory.Get(MarkdownInlineStyleState.Default with { IsLink = true, IsStrikethrough = true });

            Assert.Equal(
                [TextDecorationLocation.Underline, TextDecorationLocation.Strikethrough],
                properties.TextDecorations!.Select(static decoration => decoration.Location));
        }, CancellationToken.None);
    }

    private static MarkdownTextRunPropertiesFactory CreatePropertiesFactory(TextDecorationCollection? linkDecorations)
        => new(
            FontFamily.Default,
            FontFamily.Default,
            fontSize: 14,
            FontWeight.Normal,
            FontStyle.Normal,
            Brushes.Black,
            linkDecorations);

    private static Point GetLineStartPoint(MarkdownFormattedTextLineMetrics metrics)
        => new(0, metrics.Bounds.Y + metrics.Bounds.Height / 2);

    private static MarkdownFormattedTextLayout CreateLayout(
        string text,
        TextAlignment textAlignment = TextAlignment.Left,
        double maxWidth = 100_000)
        => new(
            new MarkdownStyledText(
                text,
                Array.Empty<MarkdownTextStyleSpan>(),
                Array.Empty<MarkdownLinkSpan>(),
                Array.Empty<MarkdownInlineImageSpan>()),
            inlineImages: null,
            baseFontFamily: FontFamily.Default,
            inlineCodeFontFamily: FontFamily.Default,
            baseFontSize: 14,
            baseFontWeight: FontWeight.Normal,
            baseFontStyle: FontStyle.Normal,
            lineHeight: 21,
            letterSpacing: 0,
            textWrapping: TextWrapping.NoWrap,
            textAlignment: textAlignment,
            maxWidth: maxWidth,
            foreground: Brushes.Black,
            linkDecorations: null);
}
