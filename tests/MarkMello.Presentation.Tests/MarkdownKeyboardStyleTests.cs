using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using MarkMello.Domain;
using MarkMello.Presentation.Views.Markdown;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Клавиша (<c>&lt;kbd&gt;</c>) рисуется как в Raycast: подпись шрифтом текста .8em,
/// рамка 1 px с полями по бокам, высотой с плашку инлайн-кода, и свои цвета темы —
/// чтобы не читаться как инлайн-код. Текст клавиши — часть текста абзаца.
/// </summary>
[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class MarkdownKeyboardStyleTests
{
    private readonly AvaloniaHeadlessFixture _fixture;

    public MarkdownKeyboardStyleTests(AvaloniaHeadlessFixture fixture) => _fixture = fixture;

    [Fact]
    public void KeyTextIsPartOfTheParagraphTextWithAKeyboardStyle()
    {
        var styled = MarkdownStyledText.FromInlines(
        [
            new MarkdownTextInline("Press "),
            new MarkdownKeyboardInline("Ctrl"),
            new MarkdownTextInline(" + "),
            new MarkdownKeyboardInline("O")
        ]);

        Assert.Equal("Press Ctrl + O", styled.Text);
        Assert.Collection(
            styled.Spans,
            span => Assert.Equal((new DocumentTextRange(6, 10), true, false), (span.Range, span.Style.IsKeyboard, span.Style.IsCode)),
            span => Assert.Equal((new DocumentTextRange(13, 14), true, false), (span.Range, span.Style.IsKeyboard, span.Style.IsCode)));
    }

    [Fact]
    public void KeysNextToEachOtherGetABoxEach()
    {
        var styled = MarkdownStyledText.FromInlines(
        [
            new MarkdownKeyboardInline("Ctrl"),
            new MarkdownKeyboardInline("C"),
            new MarkdownCodeInline("code")
        ]);

        var model = MarkdownDisplayLayoutModel.Create(styled);

        Assert.Equal(
            [(new DocumentTextRange(0, 4), true), (new DocumentTextRange(4, 5), true), (new DocumentTextRange(5, 9), false)],
            model.CodeBoxes.Select(static box => (box.CanonicalRange, box.IsKeyboard)));

        // Поля по бокам каждой рамки и зазор между клавишами вплотную — только на
        // экране, в тексте их нет.
        Assert.Equal(styled.Text.Length + 2 * model.CodeBoxes.Count + 1, model.DisplayLength);
        var gap = Assert.Single(model.Segments, static segment => segment.Kind == MarkdownDisplaySegmentKind.KeyboardGap);
        Assert.Equal(model.CodeBoxes[0].DisplayEnd, gap.DisplayStart);
        Assert.Equal(gap.DisplayEnd, model.CodeBoxes[1].DisplayStart);
    }

    [Fact]
    public void KeysSeparatedByTextHaveNoExtraGap()
    {
        var styled = MarkdownStyledText.FromInlines(
        [
            new MarkdownKeyboardInline("Ctrl"),
            new MarkdownTextInline(" + "),
            new MarkdownKeyboardInline("O")
        ]);

        var model = MarkdownDisplayLayoutModel.Create(styled);

        Assert.DoesNotContain(model.Segments, static segment => segment.Kind == MarkdownDisplaySegmentKind.KeyboardGap);
    }

    [Fact]
    public Task KeyUsesTheTextFontSmallerAndUpright()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var serif = new FontFamily("Georgia");
            var mono = new FontFamily("Maple Mono NL");
            var factory = new MarkdownTextRunPropertiesFactory(
                serif,
                mono,
                fontSize: 18,
                FontWeight.Normal,
                FontStyle.Normal,
                Brushes.Black,
                linkDecorations: null);

            var key = factory.Get(MarkdownInlineStyleState.Default with { IsKeyboard = true, IsBold = true, IsItalic = true });

            Assert.Equal(serif, key.Typeface.FontFamily);
            Assert.Equal(18 * 0.8, key.FontRenderingEmSize, 3);
            Assert.Equal(FontWeight.Normal, key.Typeface.Weight);
            Assert.Equal(FontStyle.Normal, key.Typeface.Style);
            Assert.NotSame(MarkdownTextRunPropertiesFactory.CodeFontFeatures, key.FontFeatures);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public Task KeyHasItsOwnColoursInBothThemes(string themeName)
    {
        return _fixture.Session.Dispatch(() =>
        {
            var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
            var window = ThemedTestWindow.Create(theme);

            var keyFill = Colour(window, "MmKeyboardBackgroundBrush", theme);
            var keyEdge = Colour(window, "MmKeyboardBorderBrush", theme);

            // Край клавиши виден на её крышке и на фоне страницы, а сама клавиша
            // отличается от inline-кода.
            Assert.NotEqual(keyFill, keyEdge);
            Assert.NotEqual(Colour(window, "MmBackgroundBrush", theme), keyEdge);
            Assert.NotEqual(Colour(window, "MmCodeBackgroundBrush", theme), keyFill);
            Assert.NotEqual(Colour(window, "MmCodeBorderBrush", theme), keyEdge);
        }, CancellationToken.None);
    }

    private static Color Colour(Window window, string key, ThemeVariant theme)
    {
        Assert.True(window.TryFindResource(key, theme, out var value), $"{key} is not defined in the theme");
        return Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
    }
}
