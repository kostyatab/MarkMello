using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using MarkMello.Application.Abstractions;
using MarkMello.Domain;

namespace MarkMello.Presentation.Views.Markdown;

internal sealed class MarkdownImageFlowFragment : MarkdownDocumentSelectionFragmentBase
{
    // Все размеры — в долях размера текста (прежние пиксели при тексте 14).
    private const double InlineGapRatio = 8.0 / 14;
    private const double RowGapRatio = 10.0 / 14;
    private const double MinRowHeightRatio = 18.0 / 14;
    private const double InlineHeightRatio = 0.85;
    private const double SingleImageMaxHeightRatio = 720.0 / 14;
    private const double PlaceholderCharWidthRatio = 0.55;
    private const double PlaceholderPaddingRatio = 20.0 / 14;
    private const double PlaceholderMinWidthRatio = 44.0 / 14;
    private const double PlaceholderMaxWidthRatio = 220.0 / 14;
    private const double SinglePlaceholderMinWidthRatio = 260.0 / 14;
    private const double SinglePlaceholderCharWidthRatio = 9.0 / 14;
    private const double SinglePlaceholderHeightRatio = 180.0 / 14;
    private const double PlaceholderIconRatio = 0.7;
    private const double PlaceholderMaxIconRatio = 1.7;
    private const double ImageCornerRadiusRatio = MarkdownDocumentMetrics.ImageCornerRadiusRatio;

    private readonly List<MarkdownImageFlowItem> _items;
    private readonly Dictionary<int, LoadedImageState> _loadedImages = [];
    private readonly HashSet<int> _pendingImages = [];
    private CancellationTokenSource _loadCts = new();
    private IImageSourceResolver? _imageSourceResolver;
    private string? _baseDirectory;
    private FontFamily _baseFontFamily = FontFamily.Default;
    private double _baseFontSize = 16;
    private double _lineHeight = 24;
    private ImageFlowLayout? _layout;
    private double _layoutWidth = double.NaN;
    private bool _disposed;

    public MarkdownImageFlowFragment(IReadOnlyList<MarkdownImageFlowItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = [.. items];

        UseLayoutRounding = true;
        ClipToBounds = false;
        Focusable = false;
        Cursor = TryCreateCursor(StandardCursorType.Arrow);

        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        PointerMoved += OnPointerMoved;
        PointerExited += OnPointerExited;
    }

    public IImageSourceResolver? ImageSourceResolver
    {
        get => _imageSourceResolver;
        set
        {
            if (ReferenceEquals(_imageSourceResolver, value))
            {
                return;
            }

            _imageSourceResolver = value;
            RestartImageLoading();
        }
    }

    public string? BaseDirectory
    {
        get => _baseDirectory;
        set
        {
            if (string.Equals(_baseDirectory, value, StringComparison.Ordinal))
            {
                return;
            }

            _baseDirectory = value;
            RestartImageLoading();
        }
    }

    public FontFamily BaseFontFamily
    {
        get => _baseFontFamily;
        set
        {
            _baseFontFamily = value;
            InvalidateVisual();
        }
    }

    public double BaseFontSize
    {
        get => _baseFontSize;
        set
        {
            _baseFontSize = value;
            InvalidateLayoutCache();
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    public double BaseLineHeight
    {
        get => _lineHeight;
        set
        {
            _lineHeight = value;
            InvalidateLayoutCache();
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
        => GetOrCreateLayout(availableSize.Width).Size;

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var layout = GetOrCreateLayout(Bounds.Width);
        DrawSelection(context, layout);

        foreach (var entry in layout.Entries)
        {
            DrawEntry(context, entry);
        }
    }

    public override int GetDocumentOffset(Point localPoint)
    {
        var layout = GetOrCreateLayout(Math.Max(Bounds.Width, 1));
        if (layout.Entries.Count == 0)
        {
            return DocumentRange.Start;
        }

        foreach (var entry in layout.Entries)
        {
            if (!entry.Bounds.Contains(localPoint))
            {
                continue;
            }

            return localPoint.X < entry.Bounds.Center.X
                ? DocumentRange.Start + entry.Item.LocalRange.Start
                : DocumentRange.Start + entry.Item.LocalRange.End;
        }

        var nearest = layout.Entries
            .OrderBy(entry => DistanceSquared(entry.Bounds, localPoint))
            .First();

        return localPoint.Y < nearest.Bounds.Y || localPoint.X < nearest.Bounds.X
            ? DocumentRange.Start + nearest.Item.LocalRange.Start
            : DocumentRange.Start + nearest.Item.LocalRange.End;
    }

    public override DocumentTextRange GetDocumentWordRange(Point localPoint)
    {
        var layout = GetOrCreateLayout(Math.Max(Bounds.Width, 1));
        foreach (var entry in layout.Entries)
        {
            if (entry.Bounds.Contains(localPoint))
            {
                return new DocumentTextRange(
                    DocumentRange.Start + entry.Item.LocalRange.Start,
                    DocumentRange.Start + entry.Item.LocalRange.End);
            }
        }

        return DocumentTextRange.Empty;
    }

    public override bool TryGetLinkAt(Point localPoint, out MarkdownLinkSpan linkSpan)
    {
        linkSpan = default;
        var layout = GetOrCreateLayout(Math.Max(Bounds.Width, 1));
        foreach (var entry in layout.Entries)
        {
            if (!entry.Bounds.Contains(localPoint) || string.IsNullOrWhiteSpace(entry.Item.NavigateUrl))
            {
                continue;
            }

            linkSpan = new MarkdownLinkSpan(
                new DocumentTextRange(DocumentRange.Start + entry.Item.LocalRange.Start, DocumentRange.Start + entry.Item.LocalRange.End),
                entry.Item.NavigateUrl,
                entry.Item.Title);
            return true;
        }

        return false;
    }

    public override void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _loadCts.Cancel();
        _loadCts.Dispose();

        foreach (var state in _loadedImages.Values)
        {
            MarkdownImageLoader.DisposeLoadedImage(state.Image, state.BackingStream);
        }

        _loadedImages.Clear();
        _pendingImages.Clear();

        AttachedToVisualTree -= OnAttachedToVisualTree;
        DetachedFromVisualTree -= OnDetachedFromVisualTree;
        PointerMoved -= OnPointerMoved;
        PointerExited -= OnPointerExited;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        => EnsureImagesLoaded();

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        => Dispose();

    private void OnPointerMoved(object? sender, PointerEventArgs e)
        => Cursor = TryGetLinkAt(e.GetPosition(this), out _)
            ? TryCreateCursor(StandardCursorType.Hand)
            : TryCreateCursor(StandardCursorType.Arrow);

    private void OnPointerExited(object? sender, PointerEventArgs e)
        => Cursor = TryCreateCursor(StandardCursorType.Arrow);

    private void RestartImageLoading()
    {
        if (_disposed)
        {
            return;
        }

        _loadCts.Cancel();
        _loadCts.Dispose();
        _loadCts = new CancellationTokenSource();

        foreach (var state in _loadedImages.Values)
        {
            MarkdownImageLoader.DisposeLoadedImage(state.Image, state.BackingStream);
        }

        _loadedImages.Clear();
        _pendingImages.Clear();
        InvalidateLayoutCache();
        InvalidateMeasure();
        InvalidateVisual();
        EnsureImagesLoaded();
    }

    private void EnsureImagesLoaded()
    {
        if (_disposed || _imageSourceResolver is null || VisualRoot is null)
        {
            return;
        }

        for (var index = 0; index < _items.Count; index++)
        {
            if (_loadedImages.ContainsKey(index) || _pendingImages.Contains(index))
            {
                continue;
            }

            _pendingImages.Add(index);
            _ = LoadImageAsync(index, _loadCts.Token);
        }
    }

    private async Task LoadImageAsync(int index, CancellationToken cancellationToken)
    {
        try
        {
            var item = _items[index];
            var loaded = await MarkdownImageLoader
                .TryLoadAsync(_imageSourceResolver, item.ImageUrl, _baseDirectory, cancellationToken)
                .ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested || _disposed)
            {
                if (loaded is { } canceledImage)
                {
                    MarkdownImageLoader.DisposeLoadedImage(canceledImage.Image, canceledImage.BackingStream);
                }
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() => CompleteImageLoad(index, loaded));
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            await Dispatcher.UIThread.InvokeAsync(() => CompleteImageLoad(index, null));
        }
    }

    private void CompleteImageLoad(int index, (IImage Image, Stream BackingStream)? loaded)
    {
        if (_disposed)
        {
            if (loaded is { } disposedImage)
            {
                MarkdownImageLoader.DisposeLoadedImage(disposedImage.Image, disposedImage.BackingStream);
            }
            return;
        }

        _pendingImages.Remove(index);
        _loadedImages[index] = loaded is null
            ? LoadedImageState.FailedState
            : new LoadedImageState(loaded.Value.Image, loaded.Value.BackingStream, false);

        InvalidateLayoutCache();
        InvalidateMeasure();
        InvalidateVisual();
    }

    private ImageFlowLayout GetOrCreateLayout(double availableWidth)
    {
        var normalizedWidth = NormalizeWidth(availableWidth);
        if (_layout is { } cachedLayout && Math.Abs(_layoutWidth - normalizedWidth) < 0.5)
        {
            return cachedLayout;
        }

        var layout = BuildLayout(normalizedWidth);
        _layout = layout;
        _layoutWidth = normalizedWidth;
        return layout;
    }

    private ImageFlowLayout BuildLayout(double availableWidth)
    {
        var entries = new List<ImageFlowEntry>(_items.Count);
        if (_items.Count == 0)
        {
            return new ImageFlowLayout(new Size(0, 0), entries);
        }

        var inlineGap = Em(InlineGapRatio);
        var rowGap = Em(RowGapRatio);
        var contentWidth = 0d;
        var x = 0d;
        var y = 0d;
        var rowHeight = 0d;
        var singleImage = _items.Count == 1;

        for (var index = 0; index < _items.Count; index++)
        {
            var item = _items[index];
            var size = GetDesiredSize(item, availableWidth, singleImage);

            if (item.BreakBefore && x > 0)
            {
                contentWidth = Math.Max(contentWidth, x - inlineGap);
                x = 0;
                y += rowHeight + rowGap;
                rowHeight = 0;
            }

            if (!singleImage && x > 0 && x + size.Width > availableWidth)
            {
                contentWidth = Math.Max(contentWidth, x - inlineGap);
                x = 0;
                y += rowHeight + rowGap;
                rowHeight = 0;
            }

            // Картинки стоят от левого края, как блочная картинка.
            var rect = new Rect(x, y, size.Width, size.Height);
            entries.Add(new ImageFlowEntry(item, rect, index));

            x = rect.Right + inlineGap;
            rowHeight = Math.Max(rowHeight, size.Height);
            contentWidth = Math.Max(contentWidth, rect.Right);
        }

        return new ImageFlowLayout(new Size(Math.Min(contentWidth, availableWidth), y + rowHeight), entries);
    }

    private Size GetDesiredSize(MarkdownImageFlowItem item, double availableWidth, bool singleImage)
    {
        var inlineHeight = Math.Max(
            Em(MinRowHeightRatio),
            double.IsNaN(_lineHeight) ? Em(1.2) : _lineHeight * InlineHeightRatio);

        if (_loadedImages.TryGetValue(item.Index, out var state) && state.Image is not null)
        {
            var natural = state.Image.Size;
            if (singleImage)
            {
                var maxWidth = Math.Max(1, availableWidth);
                var maxHeight = Em(SingleImageMaxHeightRatio);
                var scale = Math.Min(1d, Math.Min(maxWidth / Math.Max(1, natural.Width), maxHeight / Math.Max(1, natural.Height)));
                return new Size(Math.Max(1, natural.Width * scale), Math.Max(1, natural.Height * scale));
            }

            var scaleInline = Math.Min(1d, inlineHeight / Math.Max(1, natural.Height));
            return new Size(Math.Max(1, natural.Width * scaleInline), Math.Max(1, natural.Height * scaleInline));
        }

        if (singleImage)
        {
            return new Size(
                Math.Min(Math.Max(Em(SinglePlaceholderMinWidthRatio), item.PlainText.Length * Em(SinglePlaceholderCharWidthRatio)), availableWidth),
                Em(SinglePlaceholderHeightRatio));
        }

        var placeholderWidth = Math.Clamp(
            item.PlainText.Length * Em(PlaceholderCharWidthRatio) + Em(PlaceholderPaddingRatio),
            Em(PlaceholderMinWidthRatio),
            Em(PlaceholderMaxWidthRatio));
        return new Size(placeholderWidth, inlineHeight);
    }

    private double Em(double ratio) => _baseFontSize * ratio;

    private void DrawSelection(DrawingContext context, ImageFlowLayout layout)
    {
        var selection = DocumentRange.Intersection(SelectionRange);
        if (selection.IsEmpty)
        {
            return;
        }

        var selectionBrush = ResolveOptionalBrush("MmSelectionBrush")
            ?? ResolveOptionalBrush("MmAccentSoftBrush")
            ?? Brushes.LightBlue;

        foreach (var entry in layout.Entries)
        {
            var range = new DocumentTextRange(DocumentRange.Start + entry.Item.LocalRange.Start, DocumentRange.Start + entry.Item.LocalRange.End);
            if (!range.Intersection(selection).IsEmpty)
            {
                context.FillRectangle(selectionBrush, entry.Bounds.Inflate(2));
            }
        }
    }

    /// <summary>
    /// Картинка со скруглением или «место под картинку»: пунктирная рамка, у
    /// битой — иконка image-off по центру, как у блочной картинки.
    /// </summary>
    private void DrawEntry(DrawingContext context, ImageFlowEntry entry)
    {
        if (_loadedImages.TryGetValue(entry.Index, out var state) && state.Image is not null)
        {
            // Скругление — как у блочной картинки.
            using (context.PushClip(new RoundedRect(entry.Bounds, Em(ImageCornerRadiusRatio))))
            {
                context.DrawImage(state.Image, new Rect(state.Image.Size), entry.Bounds);
            }

            return;
        }

        var textBrush = ResolveOptionalBrush("MmTextBrush") ?? Brushes.Black;
        var borderBrush = ResolveOptionalBrush("MmKeyboardBorderBrush") ?? textBrush;
        MarkdownMissingContentFrame.Draw(
            context,
            MarkdownMissingContentFrame.CreatePen(borderBrush),
            entry.Bounds,
            Em(MarkdownDocumentMetrics.MissingFrameCornerRadiusRatio));

        if (state is not { Failed: true }
            || !this.TryFindResource("LucideImageOffGeometry", ActualThemeVariant, out var value)
            || value is not Geometry icon)
        {
            return;
        }

        var bounds = entry.Bounds;
        var side = Math.Min(Em(PlaceholderMaxIconRatio), Math.Min(bounds.Width, bounds.Height) * PlaceholderIconRatio);
        var iconBrush = ResolveOptionalBrush("MmTextFaintBrush") ?? textBrush;
        using (context.PushTransform(Matrix.CreateTranslation(
            bounds.X + (bounds.Width - side) / 2,
            bounds.Y + (bounds.Height - side) / 2)))
        {
            LucideIcon.Draw(context, icon, LucideIcon.CreatePen(iconBrush), new Size(side, side));
        }
    }

    private IBrush? ResolveOptionalBrush(string resourceKey)
        => this.TryFindResource(resourceKey, ActualThemeVariant, out var value) && value is IBrush brush
            ? brush
            : null;

    private void InvalidateLayoutCache()
    {
        _layout = null;
        _layoutWidth = double.NaN;
    }

    private static double NormalizeWidth(double availableWidth)
    {
        if (double.IsNaN(availableWidth) || availableWidth <= 0)
        {
            return 1;
        }

        if (double.IsInfinity(availableWidth))
        {
            return 100_000;
        }

        return availableWidth;
    }

    private static double DistanceSquared(Rect rect, Point point)
    {
        var dx = point.X < rect.X ? rect.X - point.X : point.X > rect.Right ? point.X - rect.Right : 0;
        var dy = point.Y < rect.Y ? rect.Y - point.Y : point.Y > rect.Bottom ? point.Y - rect.Bottom : 0;
        return dx * dx + dy * dy;
    }

    private static Cursor? TryCreateCursor(StandardCursorType cursorType)
    {
        try
        {
            return new Cursor(cursorType);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    internal static bool TryCreate(IReadOnlyList<MarkdownInline> inlines, out IReadOnlyList<MarkdownImageFlowItem> items)
    {
        ArgumentNullException.ThrowIfNull(inlines);

        var result = new List<MarkdownImageFlowItem>();
        var localOffset = 0;
        var nextBreakBefore = false;

        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case MarkdownTextInline text when string.IsNullOrWhiteSpace(text.Text):
                    localOffset += text.Text.Length;
                    break;

                case MarkdownLineBreakInline:
                    localOffset++;
                    nextBreakBefore = true;
                    break;

                case MarkdownImageInline image:
                    var imageText = MarkdownDocumentTextMap.ExtractPlainText([image]);
                    result.Add(new MarkdownImageFlowItem(
                        Index: result.Count,
                        LocalRange: new DocumentTextRange(localOffset, localOffset + imageText.Length),
                        PlainText: imageText,
                        ImageUrl: image.Url,
                        NavigateUrl: null,
                        Title: image.Title,
                        BreakBefore: nextBreakBefore));
                    localOffset += imageText.Length;
                    nextBreakBefore = false;
                    break;

                case MarkdownLinkInline link when TryUnwrapSingleImage(link, out var linkedImage):
                    var linkText = MarkdownDocumentTextMap.ExtractPlainText(link.Inlines);
                    result.Add(new MarkdownImageFlowItem(
                        Index: result.Count,
                        LocalRange: new DocumentTextRange(localOffset, localOffset + linkText.Length),
                        PlainText: linkText,
                        ImageUrl: linkedImage.Url,
                        NavigateUrl: link.Url,
                        Title: link.Title ?? linkedImage.Title,
                        BreakBefore: nextBreakBefore));
                    localOffset += linkText.Length;
                    nextBreakBefore = false;
                    break;

                default:
                    items = Array.Empty<MarkdownImageFlowItem>();
                    return false;
            }
        }

        if (result.Count == 0)
        {
            items = Array.Empty<MarkdownImageFlowItem>();
            return false;
        }

        items = result;
        return true;
    }

    private static bool TryUnwrapSingleImage(MarkdownLinkInline link, out MarkdownImageInline image)
    {
        image = null!;
        if (link.Inlines.Count != 1 || link.Inlines[0] is not MarkdownImageInline childImage)
        {
            return false;
        }

        image = childImage;
        return true;
    }

    private sealed record LoadedImageState(IImage? Image, Stream? BackingStream, bool Failed)
    {
        public static LoadedImageState FailedState { get; } = new(null, null, true);
    }

    private readonly record struct ImageFlowLayout(Size Size, IReadOnlyList<ImageFlowEntry> Entries);
    private readonly record struct ImageFlowEntry(MarkdownImageFlowItem Item, Rect Bounds, int Index);
}

internal sealed record MarkdownImageFlowItem(
    int Index,
    DocumentTextRange LocalRange,
    string PlainText,
    string ImageUrl,
    string? NavigateUrl,
    string? Title,
    bool BreakBefore);
