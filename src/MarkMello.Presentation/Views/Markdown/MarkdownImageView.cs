using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MarkMello.Application.Abstractions;

namespace MarkMello.Presentation.Views.Markdown;

/// <summary>
/// Block-level image view. Loads the image lazily when attached to the visual
/// tree (so opening a markdown file with many images does not block the viewer
/// path). The image sits on the left in its own size, no wider than the column,
/// with the alt text as its caption. While it loads, and if it cannot be
/// loaded, a dashed "place for the image" stands in its stead -- see
/// constitution §6 "fast path must stay simple" and the viewer error-handling
/// rules in architecture.md.
///
/// The control is intentionally NOT part of the document text map: image
/// rendering lives outside MarkdownSelectionTextFragment's text-flow model
/// (ADR-0001). Selection/copy skips images.
/// </summary>
internal sealed class MarkdownImageView : ContentControl, IDisposable
{
    private readonly IImageSourceResolver? _resolver;
    private readonly string _url;
    private readonly string? _altText;
    private readonly double? _width;
    private readonly double? _height;
    private readonly string? _baseDirectory;
    private readonly MarkdownBlockTypography _typography;
    private readonly string _loadingText;
    private readonly CancellationTokenSource _cts = new();
    private IImage? _loadedImage;
    private Stream? _loadedImageBackingStream;
    private Image? _imageControl;
    private bool _loadStarted;
    private bool _loadCompleted;
    private bool _disposed;

    public MarkdownImageView(
        IImageSourceResolver? resolver,
        string url,
        string? altText,
        string? title,
        double? width,
        double? height,
        string? baseDirectory,
        MarkdownBlockTypography typography,
        string loadingText)
    {
        ArgumentNullException.ThrowIfNull(typography);

        _resolver = resolver;
        _url = url ?? string.Empty;
        _altText = altText;
        _width = width;
        _height = height;
        _baseDirectory = baseDirectory;
        _typography = typography;
        _loadingText = loadingText;

        HorizontalAlignment = HorizontalAlignment.Stretch;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        UseLayoutRounding = true;

        // Render a quiet placeholder first; the actual image (or a failure
        // placeholder) replaces it when loading completes.
        Content = BuildLoadingPlaceholder();

        // The alt text is the caption; the title is only a tooltip.
        if (!string.IsNullOrWhiteSpace(title))
        {
            ToolTip.SetTip(this, title);
        }

        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_loadStarted)
        {
            return;
        }

        _loadStarted = true;
        _ = LoadAsync(_cts.Token);
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        _cts.Cancel();
        _cts.Dispose();
        MarkdownImageLoader.DisposeLoadedImage(_loadedImage, _loadedImageBackingStream);
        _loadedImage = null;
        _loadedImageBackingStream = null;
        _imageControl = null;

        AttachedToVisualTree -= OnAttachedToVisualTree;
        DetachedFromVisualTree -= OnDetachedFromVisualTree;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        ApplyImageConstraints(availableSize.Width);
        return base.MeasureOverride(availableSize);
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (_resolver is null || string.IsNullOrWhiteSpace(_url))
        {
            ShowFailurePlaceholder();
            return;
        }

        try
        {
            var loaded = await MarkdownImageLoader
                .TryLoadAsync(_resolver, _url, _baseDirectory, cancellationToken)
                .ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (loaded is null)
            {
                await Dispatcher.UIThread.InvokeAsync(ShowFailurePlaceholder);
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() => ShowImage(loaded.Value.Image, loaded.Value.BackingStream));
        }
        catch (OperationCanceledException)
        {
            // Detached from the tree mid-flight; nothing to do.
        }
        catch
        {
            await Dispatcher.UIThread.InvokeAsync(ShowFailurePlaceholder);
        }
    }

    private void ShowImage(IImage imageSource, Stream backingStream)
    {
        if (_loadCompleted)
        {
            MarkdownImageLoader.DisposeLoadedImage(imageSource, backingStream);
            return;
        }

        _loadedImage = imageSource;
        _loadedImageBackingStream = backingStream;
        _loadCompleted = true;

        var metrics = _typography.Metrics;
        var image = new Image
        {
            Source = imageSource,
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Left,
            UseLayoutRounding = true,
        };

        _imageControl = image;
        ApplyImageConstraints(Bounds.Width);

        // The corners are clipped by the border, so the image has no frame of its own.
        var frame = new Border
        {
            CornerRadius = new CornerRadius(metrics.ImageCornerRadius),
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = image
        };

        if (string.IsNullOrWhiteSpace(_altText))
        {
            Content = frame;
            return;
        }

        var caption = new TextBlock
        {
            Text = _altText,
            FontFamily = _typography.BodyFontFamily,
            FontSize = metrics.ImageCaptionFontSize,
            LineHeight = metrics.ImageCaptionLineHeight,
            Margin = metrics.ImageCaptionMargin,
            HorizontalAlignment = HorizontalAlignment.Left,
            TextWrapping = TextWrapping.Wrap,
            Classes = { "mm-md-image-caption" }
        };

        Content = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Left,
            Children = { frame, caption }
        };
    }

    private void ShowFailurePlaceholder()
    {
        if (_loadCompleted)
        {
            return;
        }
        _loadCompleted = true;
        _imageControl = null;

        // The alt text says what is missing, the path says where it was looked for.
        var metrics = _typography.Metrics;
        var text = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = metrics.MissingTextGap,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        if (!string.IsNullOrWhiteSpace(_altText))
        {
            text.Children.Add(BuildPlaceholderText(_altText));
        }

        // Путь есть не у всякой картинки: у data-URI вместо него были бы
        // строки base64.
        if (!string.IsNullOrWhiteSpace(_url) && !_url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            text.Children.Add(new TextBlock
            {
                Text = _url,
                FontFamily = _typography.MonoFontFamily,
                FontSize = metrics.MissingPathFontSize,
                LineHeight = metrics.MissingPathFontSize * metrics.LineHeightRatio,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 2,
                Classes = { "mm-md-image-missing-path" }
            });
        }

        var content = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = metrics.MissingIconGap,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { MarkdownMissingContentFrame.CreateIcon(metrics, "mm-md-image-missing-icon") }
        };

        if (text.Children.Count > 0)
        {
            content.Children.Add(text);
        }

        Content = BuildFrame(content);
    }

    private Grid BuildLoadingPlaceholder()
        => BuildFrame(BuildPlaceholderText(string.IsNullOrWhiteSpace(_altText) ? _loadingText : _altText));

    private Grid BuildFrame(Control content)
    {
        var metrics = _typography.Metrics;
        return MarkdownMissingContentFrame.Create(
            metrics,
            new Thickness(metrics.MissingFramePadding),
            metrics.MissingFrameMinHeight,
            content);
    }

    private TextBlock BuildPlaceholderText(string text)
    {
        var metrics = _typography.Metrics;
        return new TextBlock
        {
            Text = text,
            FontFamily = _typography.BodyFontFamily,
            FontSize = metrics.FontSize,
            LineHeight = metrics.BodyLineHeight,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Classes = { "mm-md-image-missing-text" }
        };
    }

    private void ApplyImageConstraints(double availableWidth)
    {
        if (_imageControl is null)
        {
            return;
        }

        _imageControl.Width = double.NaN;
        _imageControl.Height = double.NaN;
        _imageControl.MaxWidth = ResolveMaxWidth(_width, availableWidth);
        _imageControl.MaxHeight = ResolveMaxHeight(_height);
    }

    internal static double ResolveMaxWidth(double? requestedWidth, double availableWidth)
    {
        var normalizedAvailableWidth = double.IsFinite(availableWidth) && availableWidth > 0
            ? availableWidth
            : double.PositiveInfinity;

        if (requestedWidth is > 0)
        {
            return Math.Min(requestedWidth.Value, normalizedAvailableWidth);
        }

        return normalizedAvailableWidth;
    }

    internal static double ResolveMaxHeight(double? requestedHeight)
        => requestedHeight is > 0
            ? requestedHeight.Value
            : double.PositiveInfinity;
}
