using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using MarkMello.Domain.Outline;

namespace MarkMello.Presentation.Views.Markdown.Outline;

/// <summary>
/// Рельс оглавления: столбик чёрточек у правого края документа, по одной на раздел.
/// Чёрточки прижаты вправо, чем глубже уровень — тем короче; текущая темнее.
/// Не помещаются по высоте — шаг сжимается, затем рельс показывает окно вокруг
/// текущей, и края окна тают.
/// </summary>
internal sealed class DocumentOutlineRail : Control
{
    public const double DashThickness = 2;
    public const double PreferredPitch = 10;
    public const double MinimumPitch = 6;

    /// <summary>Сверху и снизу от рельса остаётся место — он не прилипает к краям окна.</summary>
    public const double VerticalMargin = 72;

    /// <summary>Зона наведения шире чёрточек, чтобы по ним легко было попасть мышью.</summary>
    public const double HitPadding = 8;

    public static double MaxDashLength => GetDashLength(1);

    public static double RailWidth => MaxDashLength + 2 * HitPadding;

    private const double FadeDistance = 40;

    private IReadOnlyList<int> _levels = [];
    private int _currentIndex;
    private DocumentOutlineRailLayout _layout;

    public DocumentOutlineRail()
    {
        Cursor = new Cursor(StandardCursorType.Hand);
        Width = RailWidth;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    /// <summary>Нажатие на чёрточку: индекс ближайшего по высоте пункта.</summary>
    public event EventHandler<int>? EntryInvoked;

    public int EntryCount => _levels.Count;

    public int CurrentIndex => _currentIndex;

    public void SetEntries(IReadOnlyList<int> levels, int currentIndex)
    {
        _levels = levels;
        _currentIndex = currentIndex;
        InvalidateMeasure();
        InvalidateVisual();
    }

    public void SetCurrentIndex(int currentIndex)
    {
        if (_currentIndex == currentIndex)
        {
            return;
        }

        _currentIndex = currentIndex;

        // Окно вокруг текущего едет вместе с чтением — раскладка меняется.
        if (_layout.IsWindowed(_levels.Count))
        {
            InvalidateMeasure();
        }

        InvalidateVisual();
    }

    public static double GetDashLength(int level) => level switch
    {
        1 => 20,
        2 => 14,
        _ => 8
    };

    protected override Size MeasureOverride(Size availableSize)
    {
        var available = double.IsInfinity(availableSize.Height)
            ? double.PositiveInfinity
            : availableSize.Height - 2 * VerticalMargin;
        _layout = DocumentOutlineRailLayout.Compute(
            _levels.Count,
            _currentIndex,
            available,
            PreferredPitch,
            MinimumPitch);

        if (_layout.VisibleCount == 0)
        {
            return new Size(RailWidth, 0);
        }

        var dashesHeight = (_layout.VisibleCount - 1) * _layout.Pitch + DashThickness;
        return new Size(RailWidth, dashesHeight + 2 * HitPadding);
    }

    public override void Render(DrawingContext context)
    {
        // Прозрачная подложка — вся зона рельса ловит наведение, а не только чёрточки.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

        if (_layout.VisibleCount == 0)
        {
            return;
        }

        var inactive = FindBrush("MmBorderBrush");
        var active = FindBrush("MmTextBrush");
        var first = _layout.FirstVisibleIndex;
        var hasMoreAbove = first > 0;
        var hasMoreBelow = first + _layout.VisibleCount < _levels.Count;
        var lastY = (_layout.VisibleCount - 1) * _layout.Pitch;

        for (var visibleIndex = 0; visibleIndex < _layout.VisibleCount; visibleIndex++)
        {
            var index = first + visibleIndex;
            var length = GetDashLength(_levels[index]);
            var y = visibleIndex * _layout.Pitch;
            var rect = new Rect(HitPadding + MaxDashLength - length, HitPadding + y, length, DashThickness);
            var brush = index == _currentIndex ? active : inactive;

            var opacity = 1.0;
            if (hasMoreAbove)
            {
                opacity = Math.Min(opacity, y / FadeDistance);
            }

            if (hasMoreBelow)
            {
                opacity = Math.Min(opacity, (lastY - y) / FadeDistance);
            }

            using (context.PushOpacity(Math.Clamp(opacity, 0.15, 1)))
            {
                context.DrawRectangle(brush, null, rect, DashThickness / 2, DashThickness / 2);
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || _layout.VisibleCount == 0)
        {
            return;
        }

        var y = e.GetPosition(this).Y - HitPadding - DashThickness / 2;
        var visibleIndex = (int)Math.Round(y / _layout.Pitch);
        visibleIndex = Math.Clamp(visibleIndex, 0, _layout.VisibleCount - 1);
        e.Handled = true;
        EntryInvoked?.Invoke(this, _layout.FirstVisibleIndex + visibleIndex);
    }

    private IBrush FindBrush(string key)
        => this.TryFindResource(key, ActualThemeVariant, out var resource) && resource is IBrush brush
            ? brush
            : Brushes.Gray;
}
