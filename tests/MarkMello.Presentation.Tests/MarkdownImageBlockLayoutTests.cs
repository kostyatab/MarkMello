using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using MarkMello.Application.Abstractions;
using MarkMello.Domain;
using MarkMello.Presentation.Localization;
using MarkMello.Presentation.Views;
using MarkMello.Presentation.Views.Markdown;
using AvaloniaApplication = Avalonia.Application;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Картинка — по левому краю, в своём размере, со скруглением; подпись под ней —
/// alt, title — только подсказкой. На месте битой картинки — «место под
/// картинку»: пунктирная рамка, иконка image-off, alt и путь; пока грузится — та
/// же рамка с alt или «Загрузка…».
/// </summary>
[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class MarkdownImageBlockLayoutTests
{
    private const double Tolerance = 0.5;
    private const double Em = 14;

    private readonly AvaloniaHeadlessFixture _fixture;

    public MarkdownImageBlockLayoutTests(AvaloniaHeadlessFixture fixture) => _fixture = fixture;

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public Task ImageSitsOnTheLeftWithItsAltTextAsTheCaption(string themeName)
    {
        return _fixture.RunAsync(async () =>
        {
            var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
            var (window, view) = Show(new MarkdownImageBlock("cover.svg", "MarkMello cover", "Loaded over HTTPS"), new SvgResolver(200, 100), theme);
            var image = await WaitForAsync<Image>(window, view);
            var imageView = ImageView(view);

            Assert.Equal(200, image.Bounds.Width, Tolerance);
            Assert.Equal(0, image.TranslatePoint(default, imageView)!.Value.X, Tolerance);
            var frame = Assert.IsType<Border>(image.Parent);
            Assert.Equal(new CornerRadius(Em * 0.125), frame.CornerRadius);
            Assert.True(frame.ClipToBounds);

            var caption = Assert.Single(imageView.GetVisualDescendants().OfType<TextBlock>());
            Assert.Equal("MarkMello cover", caption.Text);
            Assert.Equal(Em * 0.875, caption.FontSize, 3);
            Assert.Equal(Em * 0.875 * 1.4, caption.LineHeight, 3);
            Assert.Equal(new Thickness(Em * 0.2, Em * 0.4, 0, 0), caption.Margin);
            Assert.Same(Resource(window, "MmTextSoftBrush", theme), caption.Foreground);
            var captionTop = caption.TranslatePoint(default, imageView)!.Value.Y;
            Assert.Equal(image.Bounds.Height + Em * 0.4, captionTop, Tolerance);

            // Title — всплывающей подсказкой, не подписью.
            Assert.Equal("Loaded over HTTPS", ToolTip.GetTip(imageView));

            window.Close();
        });
    }

    [Fact]
    public Task ImageWithoutAltHasNoCaptionAndNoTooltipWithoutTitle()
    {
        return _fixture.RunAsync(async () =>
        {
            var (window, view) = Show(new MarkdownImageBlock("cover.svg", null, null), new SvgResolver(200, 100));
            await WaitForAsync<Image>(window, view);

            Assert.Empty(ImageView(view).GetVisualDescendants().OfType<TextBlock>());
            Assert.Null(ToolTip.GetTip(ImageView(view)));

            window.Close();
        });
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public Task BrokenImageIsADashedPlaceForTheImageWithItsAltAndPath(string themeName)
    {
        return _fixture.RunAsync(async () =>
        {
            var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
            var (window, view) = Show(new MarkdownImageBlock("../assets/does-not-exist.png", "This image does not exist", null), new MissingResolver(), theme);
            var icon = await WaitForAsync<LucideIcon>(window, view);
            var frame = PlaceholderFrame(view);

            Assert.True(frame.Bounds.Height >= Em * 8 - Tolerance);
            Assert.Equal(ImageView(view).Bounds.Width, frame.Bounds.Width, Tolerance);
            var dash = Assert.Single(frame.Children.OfType<Rectangle>());
            Assert.Same(Resource(window, "MmKeyboardBorderBrush", theme), dash.Stroke);
            Assert.NotEmpty(dash.StrokeDashArray!);
            Assert.Equal(Em * 0.3, dash.RadiusX, 3);

            Assert.Same(Resource(window, "LucideImageOffGeometry", theme), icon.Data);
            Assert.Same(Resource(window, "MmTextFaintBrush", theme), icon.Foreground);
            Assert.Equal(Em * 1.7, icon.Width, 3);

            // Вместо «Image unavailable — …» — сам alt мягким цветом и путь приглушённым.
            var texts = frame.GetVisualDescendants().OfType<TextBlock>().ToArray();
            Assert.Equal(["This image does not exist", "../assets/does-not-exist.png"], texts.Select(static text => text.Text));
            Assert.Same(Resource(window, "MmTextSoftBrush", theme), texts[0].Foreground);
            Assert.Equal(Em, texts[0].FontSize);
            Assert.Same(Resource(window, "MmTextFaintBrush", theme), texts[1].Foreground);
            Assert.Equal(Em * 0.8, texts[1].FontSize, 3);
            Assert.Same(Resource(window, "MmDocumentMonoFontFamily", theme), texts[1].FontFamily);

            // Всё по центру рамки.
            var iconCentre = icon.TranslatePoint(new Point(icon.Bounds.Width / 2, 0), frame)!.Value.X;
            Assert.Equal(frame.Bounds.Width / 2, iconCentre, Tolerance);

            window.Close();
        });
    }

    [Fact]
    public Task BrokenImageWithoutAltShowsTheIconAndThePath()
    {
        return _fixture.RunAsync(async () =>
        {
            var (window, view) = Show(new MarkdownImageBlock("missing.png", null, null), new MissingResolver());
            await WaitForAsync<LucideIcon>(window, view);

            Assert.Equal(["missing.png"], PlaceholderFrame(view).GetVisualDescendants().OfType<TextBlock>().Select(static text => text.Text));

            window.Close();
        });
    }

    [Fact]
    public Task BrokenDataUriImageShowsItsAltWithoutTheBase64()
    {
        return _fixture.RunAsync(async () =>
        {
            var (window, view) = Show(new MarkdownImageBlock("data:image/png;base64,iVBORw0KGgo=", "Embedded", null), new MissingResolver());
            await WaitForAsync<LucideIcon>(window, view);

            Assert.Equal(["Embedded"], PlaceholderFrame(view).GetVisualDescendants().OfType<TextBlock>().Select(static text => text.Text));

            window.Close();
        });
    }

    [Fact]
    public Task LoadingImageShowsTheFrameWithItsAltWithoutTheIcon()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var (window, view) = Show(new MarkdownImageBlock("slow.png", "A slow image", null), new PendingResolver());
            var frame = PlaceholderFrame(view);

            Assert.Empty(frame.GetVisualDescendants().OfType<LucideIcon>());
            Assert.Equal("A slow image", Assert.Single(frame.GetVisualDescendants().OfType<TextBlock>()).Text);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task LoadingImageWithoutAltSaysLoadingInTheInterfaceLanguage()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var resources = AvaloniaApplication.Current!.Resources;
            resources["Localization"] = new LocalizationService(AppLanguage.Russian);
            try
            {
                var (window, view) = Show(new MarkdownImageBlock("slow.png", null, null), new PendingResolver());

                Assert.Equal("Загрузка…", Assert.Single(PlaceholderFrame(view).GetVisualDescendants().OfType<TextBlock>()).Text);

                window.Close();
            }
            finally
            {
                resources.Remove("Localization");
            }
        }, CancellationToken.None);
    }

    private static (Window Window, MarkdownDocumentView View) Show(
        MarkdownImageBlock image,
        IImageSourceResolver resolver,
        ThemeVariant? theme = null)
    {
        var view = new MarkdownDocumentView
        {
            ReadingPreferences = ReadingPreferences.Default,
            ImageSourceResolver = resolver
        };
        var window = ThemedTestWindow.Create(theme ?? ThemeVariant.Light, view);
        window.Width = 700;
        window.Height = 500;
        window.Show();

        // Документ — уже в дереве, как в приложении: шрифты темы видны при сборке.
        view.Document = new RenderedMarkdownDocument([image]);
        window.UpdateLayout();
        return (window, view);
    }

    private static async Task<T> WaitForAsync<T>(Window window, MarkdownDocumentView view)
        where T : Control
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            window.UpdateLayout();
            if (ImageView(view).GetVisualDescendants().OfType<T>().FirstOrDefault() is { } found)
            {
                window.UpdateLayout();
                return found;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"No {typeof(T).Name} appeared in the image view");
    }

    private static MarkdownImageView ImageView(MarkdownDocumentView view)
        => Assert.Single(view.GetVisualDescendants().OfType<MarkdownImageView>());

    private static Grid PlaceholderFrame(MarkdownDocumentView view)
    {
        var frame = Assert.IsType<Grid>(ImageView(view).Content);
        Assert.Contains("mm-md-missing", frame.Classes);
        return frame;
    }

    private static object Resource(Window window, string key, ThemeVariant theme)
    {
        Assert.True(window.TryFindResource(key, theme, out var value), $"{key} is not defined in the theme");
        return value!;
    }

    private sealed class SvgResolver(int width, int height) : IImageSourceResolver
    {
        public Task<Stream?> TryOpenAsync(string url, string? baseDirectory, CancellationToken cancellationToken)
            => Task.FromResult<Stream?>(new MemoryStream(Encoding.UTF8.GetBytes(
                $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\"><rect width=\"{width}\" height=\"{height}\" fill=\"#888\"/></svg>")));
    }

    private sealed class MissingResolver : IImageSourceResolver
    {
        public Task<Stream?> TryOpenAsync(string url, string? baseDirectory, CancellationToken cancellationToken)
            => Task.FromResult<Stream?>(null);
    }

    private sealed class PendingResolver : IImageSourceResolver
    {
        public Task<Stream?> TryOpenAsync(string url, string? baseDirectory, CancellationToken cancellationToken)
            => new TaskCompletionSource<Stream?>().Task;
    }
}
