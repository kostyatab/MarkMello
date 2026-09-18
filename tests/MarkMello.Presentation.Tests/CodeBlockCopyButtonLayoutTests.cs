using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using MarkMello.Application.Abstractions;
using MarkMello.Domain;
using MarkMello.Presentation.Views;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// The copy icon in a code block's corner has to read as the counterpart of the
/// language label: on the same line, and as far from the right edge as the label
/// is from the left. The regression these guard against: the button box, not
/// the icon, was pinned to the content corner, which left the icon 5px lower and
/// 6px further in than the label.
/// </summary>
/// <remarks>
/// The test session has no theme, so the button is measured by its box; the
/// theme centres the icon inside it.
/// </remarks>
[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class CodeBlockCopyButtonLayoutTests
{
    // Lucide's copy glyph spans 2..22 of its 24 grid.
    private const double IconInkInsetInGrid = 2.0 / 24;
    private const double Tolerance = 0.5;

    private readonly AvaloniaHeadlessFixture _fixture;

    public CodeBlockCopyButtonLayoutTests(AvaloniaHeadlessFixture fixture) => _fixture = fixture;

    [Fact]
    public Task CopyIconSitsOnTheLanguageLabelLineAndMirrorsItsInset()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var (block, button) = LayOut(new MarkdownCodeBlock("bash", "dotnet test"));
            var label = FindInfoLabel(block);
            var labelOrigin = label.TranslatePoint(default, block)!.Value;
            var buttonBox = BoxIn(button, block);

            Assert.Equal(labelOrigin.Y + label.Bounds.Height / 2, buttonBox.Center.Y, Tolerance);
            Assert.Equal(labelOrigin.X, block.Bounds.Width - (buttonBox.Right - IconInkInset(button)), Tolerance);
        }, CancellationToken.None);
    }

    [Fact]
    public Task WithoutALanguageTheCopyIconSitsOnTheFirstLineOfCode()
    {
        return _fixture.Session.Dispatch(() =>
        {
            // At 14px the code line is 18px tall, so the button (24px) has to be
            // pulled up; at the default size the two happen to coincide.
            var preferences = ReadingPreferences.Default with { FontSize = 14 };
            var (block, button) = LayOut(new MarkdownCodeBlock(null, "dotnet test"), preferences);
            var contentTop = block.BorderThickness.Top + block.Padding.Top;
            var codeLineHeight = Math.Max(16, (preferences.FontSize - 2) * 1.5);

            Assert.Equal(contentTop + codeLineHeight / 2, BoxIn(button, block).Center.Y, Tolerance);
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

            // The scroll viewer needs the theme's template to scroll.
            var (block, button) = LayOut(new MarkdownCodeBlock(null, longLine + "\nshort line"), theme: ThemeVariant.Light);
            var code = FindCodeScrollViewer(block);

            Assert.True(code.Extent.Width > code.Viewport.Width, "the code should be wider than the block");
            Assert.True(
                BoxIn(code, block).Right <= BoxIn(button, block).Left,
                $"the code ends at {BoxIn(code, block).Right}, under the button that starts at {BoxIn(button, block).Left}");
        }, CancellationToken.None);
    }

    [Fact]
    public Task WithALanguageTheCodeTakesTheFullWidthBelowTheLabel()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var (block, _) = LayOut(new MarkdownCodeBlock("bash", "dotnet test"));
            var code = FindCodeScrollViewer(block);

            Assert.Equal(FindInfoLabel(block).Bounds.Width, code.Bounds.Width, Tolerance);
        }, CancellationToken.None);
    }

    private static (Border Block, Button Button) LayOut(
        MarkdownCodeBlock codeBlock,
        ReadingPreferences? preferences = null,
        ThemeVariant? theme = null)
    {
        // Preferences go in before the document: a later change only rebuilds
        // the blocks after a short animated delay.
        var view = new MarkdownDocumentView
        {
            ReadingPreferences = preferences ?? ReadingPreferences.Default,
            Document = new RenderedMarkdownDocument([codeBlock])
        };

        var window = theme is null
            ? new Window { Content = view }
            : ThemedTestWindow.Create(theme, view);
        window.Width = 600;
        window.Height = 400;
        window.Show();
        window.UpdateLayout();

        var viewport = Assert.IsType<Border>(view.Content);
        var root = Assert.IsType<StackPanel>(viewport.Child);
        var block = Assert.IsType<Border>(Assert.Single(root.Children));
        var contentGrid = Assert.IsType<Grid>(block.Child);
        return (block, Assert.IsType<Button>(contentGrid.Children[1]));
    }

    private static TextBlock FindInfoLabel(Border block)
    {
        var contentGrid = Assert.IsType<Grid>(block.Child);
        var body = Assert.IsType<StackPanel>(contentGrid.Children[0]);
        return Assert.IsType<TextBlock>(body.Children[0]);
    }

    private static ScrollViewer FindCodeScrollViewer(Border block)
    {
        var contentGrid = Assert.IsType<Grid>(block.Child);
        var body = Assert.IsType<StackPanel>(contentGrid.Children[0]);
        return Assert.IsType<ScrollViewer>(body.Children[^1]);
    }

    private static Rect BoxIn(Control control, Visual ancestor)
        => new(control.TranslatePoint(default, ancestor)!.Value, control.Bounds.Size);

    private static double IconInkInset(Button button)
    {
        var icon = Assert.IsType<LucideIcon>(button.Content);
        return (button.Bounds.Width - icon.Width) / 2 + icon.Width * IconInkInsetInGrid;
    }
}
