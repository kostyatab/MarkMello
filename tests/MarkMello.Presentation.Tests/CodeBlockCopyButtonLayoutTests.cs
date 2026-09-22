using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using MarkMello.Domain;
using MarkMello.Presentation.Views;
using MarkMello.Presentation.Views.Markdown;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Блок кода — лист с рамкой без шапки: язык и «Копировать» стоят в верхнем поле,
/// все поля — в долях размера текста. Без языка поле узкое, «Копировать» — на
/// уровне первой строки, и код не доходит до кнопки: регрессия — длинная первая
/// строка уходила под иконку.
/// </summary>
[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class CodeBlockCopyButtonLayoutTests
{
    private const double Tolerance = 0.5;

    private readonly AvaloniaHeadlessFixture _fixture;

    public CodeBlockCopyButtonLayoutTests(AvaloniaHeadlessFixture fixture) => _fixture = fixture;

    [Theory]
    [InlineData(14)]
    [InlineData(18)]
    public Task WithALanguageTheLabelAndTheCopyButtonSitInTheTopPadding(double fontSize)
    {
        return _fixture.Session.Dispatch(() =>
        {
            var sheet = LayOut(new MarkdownCodeBlock("bash", "dotnet test"), fontSize);
            var em = fontSize;
            var label = FindInfoLabel(sheet.Block);
            var labelBox = BoxIn(label, sheet.Block);
            var buttonBox = BoxIn(sheet.Button, sheet.Block);
            var codeBox = BoxIn(sheet.Code, sheet.Block);

            Assert.Equal(em * 1.71, buttonBox.Width, Tolerance);
            Assert.Equal(em * 1.71, buttonBox.Height, Tolerance);
            Assert.Equal(em * 6 / 14, sheet.Button.CornerRadius.TopLeft, 3);
            Assert.Equal(sheet.Inner.Top + em * 0.55, buttonBox.Top, Tolerance);
            Assert.Equal(sheet.Inner.Right - em * 0.6, buttonBox.Right, Tolerance);

            // Язык — слева на одной линии с кнопкой, по центру её высоты.
            Assert.Equal(em * 0.786, label.FontSize, 3);
            Assert.Equal(sheet.Inner.Left + em * 1.15, labelBox.Left, Tolerance);
            Assert.Equal(buttonBox.Center.Y, labelBox.Center.Y, Tolerance);

            // Код — под верхним полем 2em, по бокам 1.15em, снизу 1em.
            Assert.Equal(sheet.Inner.Top + em * 2, codeBox.Top, Tolerance);
            Assert.Equal(sheet.Inner.Left + em * 1.15, codeBox.Left, Tolerance);
            Assert.Equal(sheet.Inner.Right - em * 1.15, codeBox.Right, Tolerance);
            Assert.Equal(sheet.Inner.Bottom - em, BoxIn(sheet.CodeText, sheet.Block).Bottom, Tolerance);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(14)]
    [InlineData(18)]
    public Task WithoutALanguageTheCopyButtonSitsOnTheFirstLineAndTheCodeStopsShortOfIt(double fontSize)
    {
        return _fixture.Session.Dispatch(() =>
        {
            var sheet = LayOut(new MarkdownCodeBlock(null, "dotnet test"), fontSize);
            var em = fontSize;
            var buttonBox = BoxIn(sheet.Button, sheet.Block);
            var codeBox = BoxIn(sheet.Code, sheet.Block);

            Assert.DoesNotContain(sheet.Block.GetVisualDescendants().OfType<TextBlock>(), static text => text.Classes.Contains("mm-md-code-info"));
            Assert.Equal(sheet.Inner.Top + em * 0.55, buttonBox.Top, Tolerance);
            Assert.Equal(sheet.Inner.Right - em * 0.45, buttonBox.Right, Tolerance);

            Assert.Equal(sheet.Inner.Top + em * 0.85, codeBox.Top, Tolerance);
            // Просвет до кнопки — 2.6em кегля кода (.85em текста).
            Assert.Equal(sheet.Inner.Right - em * (1.15 + 2.6 * 0.85), codeBox.Right, Tolerance);
            Assert.True(codeBox.Right < buttonBox.Left);

            // Кнопка — на уровне первой строки кода: её центр внутри строки.
            var firstLine = new Rect(codeBox.Left, codeBox.Top, codeBox.Width, em * 0.85 * 1.5);
            Assert.InRange(buttonBox.Center.Y, firstLine.Top, firstLine.Bottom);
        }, CancellationToken.None);
    }

    /// <summary>
    /// Regression: in a block without a language the long first line of code ran
    /// underneath the copy icon. The code now stops short of the button.
    /// </summary>
    [Fact]
    public Task WithoutALanguageLongCodeStopsShortOfTheCopyButton()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var longLine = string.Concat(Enumerable.Repeat("2026-09-18T09:14:03.512Z INFO Startup stage reached ", 6));

            var sheet = LayOut(new MarkdownCodeBlock(null, longLine + "\nshort line"), 14);

            Assert.True(sheet.Code.Extent.Width > sheet.Code.Viewport.Width, "the code should be wider than the block");
            Assert.True(
                BoxIn(sheet.Code, sheet.Block).Right <= BoxIn(sheet.Button, sheet.Block).Left,
                $"the code ends at {BoxIn(sheet.Code, sheet.Block).Right}, under the button that starts at {BoxIn(sheet.Button, sheet.Block).Left}");
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(14)]
    [InlineData(18)]
    public Task CodeIsMonospaceAtAFractionOfTheTextSize(double fontSize)
    {
        return _fixture.Session.Dispatch(() =>
        {
            var sheet = LayOut(new MarkdownCodeBlock("csharp", "var x = 1;\nvar y = 2;"), fontSize);
            var code = Assert.IsType<MarkdownSelectionTextFragment>(sheet.CodeText);

            Assert.Equal(fontSize * 0.85, code.BaseFontSize, 3);
            Assert.Equal(fontSize * 0.85 * 1.5, code.BaseLineHeight, 3);
            Assert.Equal(TextWrapping.NoWrap, code.LayoutTextWrapping);
            Assert.Equal(new CornerRadius(fontSize * 0.625), sheet.Block.CornerRadius);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public Task TheSheetTakesItsColoursFromTheTheme(string themeName)
    {
        return _fixture.Session.Dispatch(() =>
        {
            var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
            var sheet = LayOut(new MarkdownCodeBlock("bash", "dotnet test"), 14, theme);
            var window = TopLevel.GetTopLevel(sheet.Block)!;

            Assert.Same(Resource(window, "MmCodeBlockBackgroundBrush", theme), sheet.Block.Background);
            Assert.Same(Resource(window, "MmBorderBrush", theme), sheet.Block.BorderBrush);
            Assert.Equal(new Thickness(1), sheet.Block.BorderThickness);
            Assert.Same(Resource(window, "MmTextFaintBrush", theme), FindInfoLabel(sheet.Block).Foreground);
        }, CancellationToken.None);
    }

    private static CodeSheet LayOut(MarkdownCodeBlock codeBlock, double fontSize, ThemeVariant? theme = null)
    {
        // Preferences go in before the document: a later change only rebuilds
        // the blocks after a short animated delay.
        var view = new MarkdownDocumentView
        {
            ReadingPreferences = ReadingPreferences.Default with { FontSize = (int)fontSize },
            Document = new RenderedMarkdownDocument([codeBlock])
        };

        // The border and the scroll viewer's template come from the theme.
        var window = ThemedTestWindow.Create(theme ?? ThemeVariant.Light, view);
        window.Width = 600;
        window.Height = 400;
        window.Show();
        window.UpdateLayout();

        var viewport = Assert.IsType<Border>(view.Content);
        var root = Assert.IsType<StackPanel>(viewport.Child);
        var block = Assert.IsType<Border>(Assert.Single(root.Children));
        var contentGrid = Assert.IsType<Grid>(block.Child);
        var code = Assert.Single(contentGrid.Children.OfType<ScrollViewer>());
        var codeText = Assert.IsType<Border>(code.Content).Child!;
        var inner = new Rect(block.Bounds.Size).Deflate(block.BorderThickness);
        return new CodeSheet(block, Assert.Single(contentGrid.Children.OfType<Button>()), code, codeText, inner);
    }

    private static TextBlock FindInfoLabel(Border block)
        => Assert.Single(Assert.IsType<Grid>(block.Child).Children.OfType<TextBlock>());

    private static Rect BoxIn(Control control, Visual ancestor)
        => new(control.TranslatePoint(default, ancestor)!.Value, control.Bounds.Size);

    private static object Resource(TopLevel window, string key, ThemeVariant theme)
    {
        Assert.True(window.TryFindResource(key, theme, out var value), $"{key} is not defined in the theme");
        return value!;
    }

    private sealed record CodeSheet(Border Block, Button Button, ScrollViewer Code, Control CodeText, Rect Inner);
}
