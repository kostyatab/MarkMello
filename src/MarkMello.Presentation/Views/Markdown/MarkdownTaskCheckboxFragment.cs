using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using MarkMello.Domain;

namespace MarkMello.Presentation.Views.Markdown;

/// <summary>
/// Чекбокс пункта task list (<c>- [ ]</c> / <c>- [x]</c>): иконка Lucide
/// <c>square</c> или <c>square-check</c> на первой строке пункта.
/// </summary>
/// <remarks>
/// Только показывает отметку из исходника: это не кнопка, клик по нему — такое же
/// начало выделения, как по тексту, и документ не меняется (constitution §1, §4).
/// <para>
/// В document-wide selection (ADR-0001) чекбокс — фрагмент текста
/// <see cref="MarkdownDocumentTextMap.GetTaskCheckboxText"/>: подсвечивается при
/// выделении и в поиске и попадает в буфер обмена вместе с пунктом. Иконку рисует
/// сам фрагмент через <see cref="LucideIcon.Draw"/>, а не дочерним контролом.
/// </para>
/// </remarks>
internal sealed class MarkdownTaskCheckboxFragment : MarkdownDocumentSelectionFragmentBase
{
    private double _iconSize = 16;
    private double _lineHeight = 24;
    private Pen? _pen;
    private bool _disposed;

    public MarkdownTaskCheckboxFragment(bool isChecked)
    {
        IsChecked = isChecked;

        Focusable = false;
        UseLayoutRounding = true;
        Cursor = TryCreateCursor(StandardCursorType.Arrow);

        ActualThemeVariantChanged += OnActualThemeVariantChanged;
        ResourcesChanged += OnResourcesChanged;
    }

    public bool IsChecked { get; }

    /// <summary>Сторона иконки (сетки Lucide 24×24) и ширина фрагмента.</summary>
    public double IconSize
    {
        get => _iconSize;
        set
        {
            _iconSize = value;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Высота строки текста пункта: иконка встаёт по центру первой строки.
    /// </summary>
    public double LineHeight
    {
        get => _lineHeight;
        set
        {
            _lineHeight = value;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    internal string GeometryKey => IsChecked ? "LucideSquareCheckGeometry" : "LucideSquareGeometry";

    // Отмеченный пункт выделен акцентом, пустой — вторичным цветом текста.
    internal string ForegroundKey => IsChecked ? "MmAccentBrush" : "MmTextSoftBrush";

    protected override Size MeasureOverride(Size availableSize)
        => new(IconSize, Math.Max(IconSize, LineHeight));

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        // Тот же порядок, что у текста: совпадения поиска, выделение, текущее
        // совпадение, сама иконка.
        var bounds = new Rect(Bounds.Size);
        DrawSearchHighlight(context, bounds);
        DrawSelection(context, bounds);
        DrawActiveSearchHighlight(context, bounds);

        if (this.TryFindResource(GeometryKey, ActualThemeVariant, out var value) && value is Geometry geometry)
        {
            _pen ??= LucideIcon.CreatePen(ResolveOptionalBrush(ForegroundKey) ?? Brushes.Gray);
            LucideIcon.Draw(context, geometry, _pen, bounds.Size);
        }
    }

    public override int GetDocumentOffset(Point localPoint)
        => localPoint.X < Bounds.Width / 2 ? DocumentRange.Start : DocumentRange.End;

    public override DocumentTextRange GetDocumentWordRange(Point localPoint) => DocumentRange;

    public override bool TryGetLinkAt(Point localPoint, out MarkdownLinkSpan linkSpan)
    {
        linkSpan = default;
        return false;
    }

    public override void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ActualThemeVariantChanged -= OnActualThemeVariantChanged;
        ResourcesChanged -= OnResourcesChanged;
        GC.SuppressFinalize(this);
    }

    private void DrawSearchHighlight(DrawingContext context, Rect bounds)
    {
        if (!SearchHighlightRanges.Any(static range => !range.IsEmpty))
        {
            return;
        }

        if (ResolveOptionalBrush("MmFindHighlightBrush") is { } brush)
        {
            context.FillRectangle(brush, bounds);
        }
    }

    private void DrawSelection(DrawingContext context, Rect bounds)
    {
        if (DocumentRange.Intersection(SelectionRange).IsEmpty)
        {
            return;
        }

        var brush = ResolveOptionalBrush("MmSelectionBrush")
            ?? ResolveOptionalBrush("MmAccentSoftBrush")
            ?? Brushes.LightBlue;
        context.FillRectangle(brush, bounds);
    }

    private void DrawActiveSearchHighlight(DrawingContext context, Rect bounds)
    {
        if (ActiveSearchHighlight is not { } active
            || DocumentRange.Intersection(active).IsEmpty
            || ResolveOptionalBrush("MmFindActiveBrush") is not { } brush)
        {
            return;
        }

        context.FillRectangle(brush, bounds);
        if (ResolveOptionalBrush("MmAccentBrush") is { } stroke)
        {
            context.DrawRectangle(new Pen(stroke, 1), bounds);
        }
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
        => InvalidateForAppearanceChange();

    private void OnResourcesChanged(object? sender, ResourcesChangedEventArgs e)
        => InvalidateForAppearanceChange();

    private void InvalidateForAppearanceChange()
    {
        _pen = null;
        InvalidateVisual();
    }

    private IBrush? ResolveOptionalBrush(string resourceKey)
        => this.TryFindResource(resourceKey, ActualThemeVariant, out var value) && value is IBrush brush
            ? brush
            : null;

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
}
