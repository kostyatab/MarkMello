using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using MarkMello.Domain;
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

    [Theory]
    [InlineData("before the comma", ", and more words after it to wrap.")]
    [InlineData("before the period", ". More words after it to wrap the line.")]
    [InlineData("before a space", " and more words after it to wrap the line.")]
    public Task FootnoteReferenceNeverWrapsAwayFromItsWord(string before, string after)
    {
        return _fixture.Session.Dispatch(() =>
        {
            var styled = MarkdownStyledText.FromInlines(
            [
                new MarkdownTextInline("Some words " + before),
                new MarkdownFootnoteReferenceInline(1),
                new MarkdownTextInline(after)
            ]);
            var label = Assert.Single(styled.FootnoteReferences).Range;
            var lastLetter = new DocumentTextRange(label.Start - 1, label.Start);
            var nextCharacter = new DocumentTextRange(label.End, label.End + 1);
            var wordStart = "Some words ".Length + before.LastIndexOf(' ') + 1;
            var spaceBeforeWord = new DocumentTextRange(wordStart - 1, wordStart);
            var wordStartsALine = false;

            // Каждая ширина, при которой слово перед меткой помещается в строку:
            // перенос где угодно, но метка остаётся со словом и со знаком за ней,
            // как <sup> в браузере.
            for (var width = 120; width <= 500; width++)
            {
                using var layout = CreateLayout(styled, TextWrapping.Wrap, width);

                var letterTop = Assert.Single(layout.GetSelectionRects(lastLetter)).Y;
                var labelTop = Assert.Single(layout.GetSelectionRects(label)).Y;
                var nextTop = Assert.Single(layout.GetSelectionRects(nextCharacter)).Y;
                Assert.True(letterTop == labelTop, $"The label wrapped away from its word at width {width}.");
                Assert.True(labelTop == nextTop, $"The character after the label wrapped away from it at width {width}.");

                wordStartsALine |= Assert.Single(layout.GetSelectionRects(spaceBeforeWord)).Y != letterTop;
            }

            // Тест проверяет что-то, только если перенос хоть раз пришёлся на слово с меткой.
            Assert.True(wordStartsALine);
        }, CancellationToken.None);
    }

    [Fact]
    public Task FootnoteNumberIsLaidOutOncePerParagraphAndFreedWithIt()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var metrics = MarkdownFootnoteReferenceMetrics.Create(
                FontFamily.Default,
                14,
                FontWeight.Normal,
                FontStyle.Normal,
                Brushes.Black);

            // Метки с одним номером и все перерисовки берут одну раскладку.
            var layout = metrics.GetNumberLayout("3");
            Assert.Same(layout, metrics.GetNumberLayout("3"));
            Assert.NotSame(layout, metrics.GetNumberLayout("4"));

            metrics.Dispose();
            Assert.NotSame(layout, metrics.GetNumberLayout("3"));
            metrics.Dispose();
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

    private static MarkdownFormattedTextLayout CreateLayout(MarkdownStyledText styledText, TextWrapping textWrapping, double maxWidth)
        => new(
            styledText,
            inlineImages: null,
            baseFontFamily: FontFamily.Default,
            inlineCodeFontFamily: FontFamily.Default,
            baseFontSize: 14,
            baseFontWeight: FontWeight.Normal,
            baseFontStyle: FontStyle.Normal,
            lineHeight: 21,
            letterSpacing: 0,
            textWrapping: textWrapping,
            textAlignment: TextAlignment.Left,
            maxWidth: maxWidth,
            foreground: Brushes.Black,
            linkDecorations: null,
            imagePlaceholderBrushes: new(Brushes.Black, Brushes.Black));

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
            linkDecorations: null,
            imagePlaceholderBrushes: new(Brushes.Black, Brushes.Black));
}
