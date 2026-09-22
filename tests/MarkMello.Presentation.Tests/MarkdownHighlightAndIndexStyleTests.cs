using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using MarkMello.Domain;
using MarkMello.Presentation.Views.Markdown;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// <c>&lt;mark&gt;</c> — фон подсветки поиска с полями .1em сверху и снизу, .15em по
/// бокам, который рвётся по строкам; <c>&lt;sub&gt;</c> и <c>&lt;sup&gt;</c> — .75em,
/// сдвинуты на .1875em вниз и .375em вверх и не раздвигают строку. Цвет и
/// начертание — от окружения.
/// </summary>
[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class MarkdownHighlightAndIndexStyleTests
{
    private const double Tolerance = 0.5;

    private static readonly FontFamily Mono = new("avares://MarkMello.Presentation/Assets/Fonts/JetBrainsMono#JetBrains Mono");
    private static readonly FontFamily Sans = new("avares://MarkMello.Presentation/Assets/Fonts/Inter#Inter");

    private readonly AvaloniaHeadlessFixture _fixture;

    public MarkdownHighlightAndIndexStyleTests(AvaloniaHeadlessFixture fixture) => _fixture = fixture;

    [Fact]
    public void HighlightAndIndexesAreStylesOfTheParagraphText()
    {
        var styled = MarkdownStyledText.FromInlines(
        [
            new MarkdownHighlightInline([new MarkdownStrongInline([new MarkdownTextInline("Key")]), new MarkdownTextInline(" part")]),
            new MarkdownTextInline(" H"),
            new MarkdownSubscriptInline([new MarkdownTextInline("2")]),
            new MarkdownTextInline("O mc"),
            new MarkdownSuperscriptInline([new MarkdownTextInline("2")])
        ]);

        Assert.Equal("Key part H2O mc2", styled.Text);
        Assert.Equal([new DocumentTextRange(0, 8)], styled.Highlights);
        Assert.Collection(
            styled.Spans,
            span => Assert.Equal((new DocumentTextRange(0, 3), true), (span.Range, span.Style.IsBold)),
            span => Assert.Equal((new DocumentTextRange(10, 11), MarkdownScriptPosition.Subscript), (span.Range, span.Style.Script)),
            span => Assert.Equal((new DocumentTextRange(15, 16), MarkdownScriptPosition.Superscript), (span.Range, span.Style.Script)));
    }

    /// <summary>
    /// Поля выделения — только на экране: жирный и код внутри не разрывают его, в
    /// тексте абзаца полей нет.
    /// </summary>
    [Fact]
    public void HighlightGetsSidePaddingAroundItsWholeContent()
    {
        var styled = MarkdownStyledText.FromInlines(
        [
            new MarkdownTextInline("a "),
            new MarkdownHighlightInline([new MarkdownStrongInline([new MarkdownTextInline("b")]), new MarkdownCodeInline("c")]),
            new MarkdownTextInline(" d")
        ]);

        var model = MarkdownDisplayLayoutModel.Create(styled);

        var box = Assert.Single(model.HighlightBoxes);
        var left = Assert.Single(model.Segments, static segment => segment.Kind == MarkdownDisplaySegmentKind.HighlightPaddingLeft);
        var right = Assert.Single(model.Segments, static segment => segment.Kind == MarkdownDisplaySegmentKind.HighlightPaddingRight);
        Assert.Equal(box.DisplayStart, left.DisplayStart);
        Assert.Equal(box.DisplayEnd, right.DisplayEnd);
        Assert.Equal(2, model.GetCanonicalCaretForDisplayCaret(box.DisplayStart));
        Assert.Equal(4, model.GetCanonicalCaretForDisplayCaret(box.DisplayEnd));
        Assert.Single(model.CodeBoxes);
    }

    /// <summary>
    /// Выделения вплотную — два фона со своими полями, как два <c>mark</c> в
    /// браузере; вложенное выделение рисуется внешним, а метка сноски внутри
    /// выделения его не разрывает.
    /// </summary>
    [Fact]
    public void AdjacentHighlightsGetABoxEachAndNestedOnesJoinTheOuter()
    {
        var adjacent = MarkdownDisplayLayoutModel.Create(MarkdownStyledText.FromInlines(
        [
            new MarkdownHighlightInline([new MarkdownTextInline("ab")]),
            new MarkdownHighlightInline([new MarkdownTextInline("cd")])
        ]));
        var nested = MarkdownDisplayLayoutModel.Create(MarkdownStyledText.FromInlines(
        [
            new MarkdownHighlightInline(
            [
                new MarkdownTextInline("a"),
                new MarkdownHighlightInline([new MarkdownTextInline("b")]),
                new MarkdownFootnoteReferenceInline(1),
                new MarkdownTextInline("c")
            ]),
            new MarkdownFootnoteReferenceInline(2)
        ]));

        Assert.Equal(2, adjacent.HighlightBoxes.Count);
        Assert.Equal(adjacent.HighlightBoxes[0].DisplayEnd, adjacent.HighlightBoxes[1].DisplayStart);
        Assert.Equal([0, 2], adjacent.HighlightBoxes.Select(box => adjacent.GetCanonicalCaretForDisplayCaret(box.DisplayStart)));

        var box = Assert.Single(nested.HighlightBoxes);
        var footnotes = nested.Segments.Where(static segment => segment.Kind == MarkdownDisplaySegmentKind.FootnoteReference).ToArray();
        Assert.InRange(footnotes[0].DisplayStart, box.DisplayStart, box.DisplayEnd - 1);
        Assert.True(footnotes[1].DisplayStart >= box.DisplayEnd);
    }

    [Fact]
    public Task IndexRunIsThreeQuartersInvisibleAndItsGlyphsKeepTheSurroundingLook()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var factory = new MarkdownTextRunPropertiesFactory(Sans, Mono, 14, FontWeight.Normal, FontStyle.Normal, Brushes.Black, linkDecorations: null);
            var style = MarkdownInlineStyleState.Default with { Script = MarkdownScriptPosition.Superscript, IsBold = true, IsItalic = true };

            var inLine = factory.Get(style);
            var glyphs = factory.GetVisibleScript(style);

            Assert.Equal(14 * 0.75, inLine.FontRenderingEmSize, 3);
            Assert.Same(Brushes.Transparent, inLine.ForegroundBrush);
            Assert.Equal(14 * 0.75, glyphs.FontRenderingEmSize, 3);
            Assert.Same(Brushes.Black, glyphs.ForegroundBrush);
            Assert.Equal(FontWeight.Bold, glyphs.Typeface.Weight);
            Assert.Equal(FontStyle.Italic, glyphs.Typeface.Style);

            // Код и клавиша внутри индекса остаются как в строке.
            var code = factory.Get(style with { IsCode = true });
            Assert.Equal(14 * 0.85, code.FontRenderingEmSize, 3);
            Assert.NotSame(Brushes.Transparent, code.ForegroundBrush);
        }, CancellationToken.None);
    }

    [Fact]
    public Task IndexesAreShiftedFromTheBaselineAndDoNotChangeTheLineHeight()
    {
        return _fixture.Session.Dispatch(() =>
        {
            using var plain = CreateLayout(MarkdownStyledText.FromInlines([new MarkdownTextInline("H2O and mc2")]));
            using var layout = CreateLayout(MarkdownStyledText.FromInlines(
            [
                new MarkdownTextInline("H"),
                new MarkdownSubscriptInline([new MarkdownTextInline("2")]),
                new MarkdownTextInline("O and mc"),
                new MarkdownSuperscriptInline([new MarkdownTextInline("2")])
            ]));

            Assert.Equal(
                Assert.Single(plain.GetLineMetrics()).Bounds.Height,
                Assert.Single(layout.GetLineMetrics()).Bounds.Height,
                Tolerance);
            Assert.Collection(
                layout.GetScriptPlacements(),
                sub => Assert.Equal(("2", 14 * 0.1875), (sub.Text, Math.Round(sub.BaselineShift, 4))),
                sup => Assert.Equal(("2", -14 * 0.375), (sup.Text, Math.Round(sup.BaselineShift, 4))));

            // Индекс уже обычного текста: он занимает место своего кегля.
            Assert.True(layout.Width < plain.Width);
        }, CancellationToken.None);
    }

    [Fact]
    public Task HighlightHasPaddingAroundTheTextLine()
    {
        return _fixture.Session.Dispatch(() =>
        {
            using var plain = CreateLayout(MarkdownStyledText.FromInlines([new MarkdownTextInline("a note b")]));
            using var layout = CreateLayout(MarkdownStyledText.FromInlines(
            [
                new MarkdownTextInline("a "),
                new MarkdownHighlightInline([new MarkdownTextInline("note")]),
                new MarkdownTextInline(" b")
            ]));
            using var probe = new Avalonia.Media.TextFormatting.TextLayout("M", new Typeface(Sans), 14, Brushes.Black);
            using var word = new Avalonia.Media.TextFormatting.TextLayout("note", new Typeface(Sans), 14, Brushes.Black);

            var rect = Assert.Single(layout.GetHighlightRects(Assert.Single(layout.HighlightBoxes)));

            // Поля по бокам занимают место в строке, как padding у mark.
            Assert.Equal(plain.Width + 2 * 14 * 0.15, layout.Width, Tolerance);
            Assert.Equal(word.WidthIncludingTrailingWhitespace + 2 * 14 * 0.15, rect.Width, Tolerance);
            Assert.Equal(probe.Height + 2 * 14 * 0.1, rect.Height, Tolerance);

            // Строку фон не раздвигает, а стоит на базовой линии текста.
            var line = Assert.Single(layout.GetLineMetrics()).Bounds;
            Assert.Equal(14 * 1.6, line.Height, Tolerance);
            Assert.Equal(rect.Top + 14 * 0.1 + probe.Baseline, line.Top + (line.Height - probe.Height) / 2 + probe.Baseline, Tolerance);
        }, CancellationToken.None);
    }

    /// <summary>
    /// При переносе фон рвётся на куски по строкам, и у каждого куска свои поля,
    /// как <c>box-decoration-break: clone</c>; пробел переноса в фон не входит.
    /// </summary>
    [Fact]
    public Task WrappedHighlightIsAPieceOnEveryLineWithItsOwnPadding()
    {
        return _fixture.Session.Dispatch(() =>
        {
            using var layout = CreateLayout(
                MarkdownStyledText.FromInlines([new MarkdownHighlightInline([new MarkdownTextInline("one two three four five six seven")])]),
                maxWidth: 120);

            var lines = layout.GetLineMetrics();
            var rects = layout.GetHighlightRects(Assert.Single(layout.HighlightBoxes));

            Assert.True(lines.Count > 1);
            Assert.Equal(lines.Count, rects.Count);
            for (var index = 0; index < rects.Count; index++)
            {
                var line = lines[index].Bounds;
                Assert.InRange(rects[index].Top, line.Top, line.Bottom);

                // Кусок начинается за полем до первого знака строки.
                Assert.Equal(line.Left - (index == 0 ? 0 : 14 * 0.15), rects[index].Left, Tolerance);
            }

            // Пробел, на котором строка перенесена, в кусок не входит: кусок кончается
            // полем после последнего слова, а не после пробела.
            using var space = new Avalonia.Media.TextFormatting.TextLayout(" ", new Typeface(Sans), 14, Brushes.Black);
            Assert.Equal(lines[0].Bounds.Right - space.WidthIncludingTrailingWhitespace + 14 * 0.15, rects[0].Right, Tolerance);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public Task HighlightUsesTheSearchHighlightColourOfTheTheme(string themeName)
    {
        return _fixture.Session.Dispatch(() =>
        {
            var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
            var window = ThemedTestWindow.Create(theme);

            Assert.True(window.TryFindResource("MmFindHighlightBrush", theme, out var value));
            var fill = Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
            Assert.True(window.TryFindResource("MmBackgroundBrush", theme, out var background));
            Assert.NotEqual(Assert.IsAssignableFrom<ISolidColorBrush>(background).Color, fill);
        }, CancellationToken.None);
    }

    private static MarkdownFormattedTextLayout CreateLayout(MarkdownStyledText styled, double maxWidth = 100_000)
        => new(
            styled,
            inlineImages: null,
            baseFontFamily: Sans,
            inlineCodeFontFamily: Mono,
            baseFontSize: 14,
            baseFontWeight: FontWeight.Normal,
            baseFontStyle: FontStyle.Normal,
            lineHeight: 14 * 1.6,
            letterSpacing: 0,
            textWrapping: TextWrapping.Wrap,
            textAlignment: TextAlignment.Left,
            maxWidth: maxWidth,
            foreground: Brushes.Black,
            linkDecorations: null,
            imagePlaceholderBrushes: new(Brushes.Black, Brushes.Black));
}
