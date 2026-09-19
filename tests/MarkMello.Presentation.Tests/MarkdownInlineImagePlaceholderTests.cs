using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using MarkMello.Domain;
using MarkMello.Presentation.Views;
using MarkMello.Presentation.Views.Markdown;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Пока строчная картинка грузится, на её месте рамка с подписью. Цвета рамки —
/// из палитры темы, как у заглушки блочной картинки: со светло-серым фоном
/// светлая подпись тёмной темы почти не читалась.
/// </summary>
[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class MarkdownInlineImagePlaceholderTests
{
    private const string Alt = "Small orange square";

    private const string OrangeSquare =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAIAAACQkWg2AAAAXklEQVR42pXSbQoAEAyH8edYruks7uSTO6BWkpf5q4mtfi2MWlKPHIMYGNANfX0ZbNMN4yQa5kQxLPnTsJd8w7HqGG6tbwbnfkeD/4i74flTi0EZh9kgztww6INtoAGEjtoCnf+5iQAAAABJRU5ErkJggg==";

    private readonly AvaloniaHeadlessFixture _fixture;

    public MarkdownInlineImagePlaceholderTests(AvaloniaHeadlessFixture fixture) => _fixture = fixture;

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public Task LoadingPlaceholderTakesItsColoursFromTheTheme(string themeName)
    {
        return _fixture.Session.Dispatch(() =>
        {
            var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
            // Без ImageSourceResolver картинка не грузится и остаётся заглушкой.
            var view = new MarkdownDocumentView
            {
                ReadingPreferences = ReadingPreferences.Default,
                Document = new RenderedMarkdownDocument(
                [
                    new MarkdownParagraphBlock(
                    [
                        new MarkdownTextInline("An image embedded as a data URI: "),
                        new MarkdownImageInline(OrangeSquare, Alt, Title: null)
                    ])
                ])
            };
            var window = ThemedTestWindow.Create(theme, view);
            window.Show();
            window.UpdateLayout();

            var drawings = Render(Assert.Single(view.GetVisualDescendants().OfType<MarkdownSelectionTextFragment>()));

            var box = Assert.Single(drawings.OfType<GeometryDrawing>(), static drawing => drawing.Pen is not null);
            Assert.Equal(Colour(window, "MmCodeBackgroundBrush", theme), SolidColour(box.Brush));
            Assert.Equal(Colour(window, "MmCodeBorderBrush", theme), SolidColour(box.Pen?.Brush));

            var label = Assert.Single(
                drawings.OfType<GlyphRunDrawing>(),
                static drawing => drawing.GlyphRun?.Characters.ToString() == Alt);
            Assert.Equal(Colour(window, "MmTextSoftBrush", theme), SolidColour(label.Foreground));

            window.Close();
        }, CancellationToken.None);
    }

    private static List<Drawing> Render(MarkdownSelectionTextFragment fragment)
    {
        var group = new DrawingGroup();
        using (var context = group.Open())
        {
            fragment.Render(context);
        }

        var drawings = new List<Drawing>();
        Collect(group, drawings);
        return drawings;
    }

    private static void Collect(Drawing drawing, List<Drawing> drawings)
    {
        if (drawing is DrawingGroup group)
        {
            foreach (var child in group.Children)
            {
                Collect(child, drawings);
            }

            return;
        }

        drawings.Add(drawing);
    }

    private static Color SolidColour(IBrush? brush)
        => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    private static Color Colour(Window window, string key, ThemeVariant theme)
    {
        Assert.True(window.TryFindResource(key, theme, out var value), $"{key} is not defined in the theme");
        return Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
    }
}
