using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.VisualTree;
using MarkMello.Application.Abstractions;
using MarkMello.Domain;
using MarkMello.Presentation.Views;
using MarkMello.Presentation.Views.Markdown;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Картинка в строке: пока грузится — пунктирная рамка «места под картинку»,
/// битая — та же рамка с иконкой image-off, обе цветами темы. Загруженная стоит
/// в своём размере, чуть ниже базовой линии. Регрессия: на месте картинки была
/// рамка с подписью на светло-сером фоне, у data-URI — зашитый светлый фон.
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
    public Task LoadingPlaceholderIsADashedFrameInTheThemeColours(string themeName)
    {
        return _fixture.Session.Dispatch(() =>
        {
            var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
            // Без ImageSourceResolver картинка не грузится и остаётся заглушкой.
            var (window, fragment) = Show(theme, resolver: null);

            var drawings = Render(fragment);

            var frame = Assert.Single(drawings.OfType<GeometryDrawing>(), static drawing => drawing.Pen is not null);
            Assert.Null(frame.Brush);
            Assert.NotNull(frame.Pen!.DashStyle);
            Assert.Equal(Colour(window, "MmKeyboardBorderBrush", theme), SolidColour(frame.Pen.Brush));

            // Подписи на заглушке больше нет.
            Assert.DoesNotContain(drawings.OfType<GlyphRunDrawing>(), static drawing => drawing.GlyphRun?.Characters.ToString() == Alt);

            window.Close();
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public Task BrokenImageShowsTheImageOffIconInsideTheFrame(string themeName)
    {
        return _fixture.RunAsync(async () =>
        {
            var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
            var (window, fragment) = Show(theme, new MissingImageResolver());

            List<Drawing> drawings = [];
            for (var attempt = 0; attempt < 200; attempt++)
            {
                window.UpdateLayout();
                drawings = Render(fragment);
                if (drawings.OfType<GeometryDrawing>().Count(static drawing => drawing.Pen is not null) > 1)
                {
                    break;
                }

                await Task.Delay(25);
            }

            var strokes = drawings.OfType<GeometryDrawing>().Where(static drawing => drawing.Pen is not null).ToArray();
            Assert.Equal(2, strokes.Length);
            Assert.Equal(Colour(window, "MmKeyboardBorderBrush", theme), SolidColour(strokes[0].Pen!.Brush));
            Assert.Equal(Colour(window, "MmTextFaintBrush", theme), SolidColour(strokes[1].Pen!.Brush));

            window.Close();
        });
    }

    [Fact]
    public Task LoadedImageKeepsItsOwnSizeAndSitsBelowTheBaseline()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var metrics = Metrics(fontSize: 14, lineHeight: 22.4);
            using var bitmap = new WriteableBitmap(new PixelSize(30, 40), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);

            var run = MarkdownInlineImageTextRun.Create(Span(), new MarkdownInlineImageState(bitmap, null, Failed: false), metrics);

            // Выше строки текста — и всё равно в своём размере.
            Assert.Equal(new Size(30, 40), run.Size);
            Assert.Equal(40 - 14 * 0.15, run.Baseline, 3);
        }, CancellationToken.None);
    }

    /// <summary>
    /// Высокая картинка в строке раздвигает свою строку, а не наезжает на строку
    /// выше: межстрочный у абзаца фиксированный, но строка с картинкой выше его.
    /// </summary>
    [Fact]
    public Task TallImageMakesItsLineTallerInsteadOfOverlappingTheLineAbove()
    {
        return _fixture.Session.Dispatch(() =>
        {
            using var bitmap = new WriteableBitmap(new PixelSize(40, 60), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            var styled = MarkdownStyledText.FromInlines(
            [
                new MarkdownTextInline("First line"),
                new MarkdownLineBreakInline(),
                new MarkdownTextInline("Second "),
                new MarkdownImageInline(OrangeSquare, Alt, Title: null),
                new MarkdownTextInline(" line")
            ]);
            var layout = new MarkdownFormattedTextLayout(
                styled,
                new Dictionary<int, MarkdownInlineImageState> { [0] = new(bitmap, null, Failed: false) },
                baseFontFamily: FontFamily.Default,
                inlineCodeFontFamily: FontFamily.Default,
                baseFontSize: 14,
                baseFontWeight: FontWeight.Normal,
                baseFontStyle: FontStyle.Normal,
                lineHeight: 22.4,
                letterSpacing: 0,
                textWrapping: TextWrapping.Wrap,
                textAlignment: TextAlignment.Left,
                maxWidth: 600,
                foreground: Brushes.Black,
                linkDecorations: null,
                imagePlaceholderBrushes: new(Brushes.Gray, Brushes.Gray));

            var lines = layout.GetLineMetrics();

            Assert.Equal(2, lines.Count);
            Assert.Equal(lines[0].Bounds.Bottom, lines[1].Bounds.Top, 3);
            // Картинка опущена на .15em под базовую линию, поэтому строка — не ниже её высоты.
            Assert.True(lines[1].Bounds.Height >= 60, $"the line with the image is {lines[1].Bounds.Height} tall");
            Assert.Equal(lines[0].Bounds.Height + lines[1].Bounds.Height, layout.Height, 3);
        }, CancellationToken.None);
    }

    [Fact]
    public Task TinyImageKeepsItsBaselineInsideItself()
    {
        return _fixture.Session.Dispatch(() =>
        {
            using var bitmap = new WriteableBitmap(new PixelSize(1, 1), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);

            var run = MarkdownInlineImageTextRun.Create(Span(), new MarkdownInlineImageState(bitmap, null, Failed: false), Metrics(fontSize: 14, lineHeight: 22.4));

            // Картинка ниже .15em не может опуститься под базовую линию на .15em — она стоит на ней.
            Assert.Equal(0, run.Baseline);
        }, CancellationToken.None);
    }

    [Fact]
    public Task LoadedImageIsNoWiderThanTheLine()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var metrics = Metrics(fontSize: 14, lineHeight: 22.4);
            using var bitmap = new WriteableBitmap(new PixelSize(400, 100), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);

            var run = MarkdownInlineImageTextRun.Create(Span(), new MarkdownInlineImageState(bitmap, null, Failed: false), metrics, maxWidth: 200);

            Assert.Equal(new Size(200, 50), run.Size);
        }, CancellationToken.None);
    }

    [Fact]
    public Task PlaceholderSizeFollowsTheTextSize()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var at14 = MarkdownInlineImageTextRun.Placeholder(Alt, Metrics(fontSize: 14, lineHeight: 14 * 1.6), failed: true);
            var at18 = MarkdownInlineImageTextRun.Placeholder(Alt, Metrics(fontSize: 18, lineHeight: 18 * 1.6), failed: true);

            Assert.Equal(at14.Size.Width * 18 / 14, at18.Size.Width, 3);
            Assert.Equal(at14.Size.Height * 18 / 14, at18.Size.Height, 3);
        }, CancellationToken.None);
    }

    private static (Window Window, MarkdownSelectionTextFragment Fragment) Show(ThemeVariant theme, IImageSourceResolver? resolver)
    {
        var view = new MarkdownDocumentView
        {
            ReadingPreferences = ReadingPreferences.Default,
            ImageSourceResolver = resolver,
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
        return (window, Assert.Single(view.GetVisualDescendants().OfType<MarkdownSelectionTextFragment>()));
    }

    private static MarkdownInlineImageMetrics Metrics(double fontSize, double lineHeight)
        => MarkdownInlineImageMetrics.Create(
            FontFamily.Default,
            fontSize,
            FontWeight.Normal,
            FontStyle.Normal,
            lineHeight,
            new MarkdownInlineImagePlaceholderBrushes(Brushes.Gray, Brushes.Gray));

    private static MarkdownInlineImageSpan Span()
        => new(0, new DocumentTextRange(0, 1), OrangeSquare, Alt, null, Alt, MarkdownInlineStyleState.Default);

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

    private sealed class MissingImageResolver : IImageSourceResolver
    {
        public Task<Stream?> TryOpenAsync(string url, string? baseDirectory, CancellationToken cancellationToken)
            => Task.FromResult<Stream?>(null);
    }
}
