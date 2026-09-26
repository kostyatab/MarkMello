using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace MarkMello.Presentation.Views;

/// <summary>
/// Кольцо прогресса: проверка обновлений и загрузка на кнопке в строке окна. Это индикатор, а
/// не иконка, поэтому рисуется здесь, а не геометрией Lucide. Дорожка — тот же цвет, что дуга,
/// приглушённый; толщина — 2 px, как у кольца на холсте.
/// <para>
/// С <see cref="Value"/> от 0 до 1 дуга показывает долю. Без значения размер неизвестен:
/// четверть круга крутится — вращение задаёт стиль <c>:indeterminate</c> в <c>Controls.axaml</c>.
/// Видимость кольцу задают напрямую, а не через родителя: по ней оно решает, крутиться ли.
/// </para>
/// </summary>
public sealed class ProgressRing : Control
{
    private const double StrokeThickness = 2;
    private const double TrackOpacity = 0.3;
    private const double IndeterminateSweep = 0.25;

    public static readonly StyledProperty<double?> ValueProperty =
        AvaloniaProperty.Register<ProgressRing, double?>(nameof(Value));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<ProgressRing>();

    static ProgressRing()
    {
        AffectsRender<ProgressRing>(ValueProperty, ForegroundProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<ProgressRing>(false);
        AutomationProperties.AccessibilityViewProperty.OverrideDefaultValue<ProgressRing>(AccessibilityView.Raw);
    }

    public ProgressRing()
    {
        // Свой поворот у каждого кольца: анимация стиля крутит RotateTransform.Angle, и один
        // экземпляр из сеттера стиля дёргали бы все крутящиеся кольца разом.
        RenderTransform = new RotateTransform();
        UpdatePseudoClasses();
    }

    /// <summary>Доля от 0 до 1; <c>null</c> — размер неизвестен, кольцо крутится.</summary>
    public double? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public bool IsIndeterminate => Value is null;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ValueProperty || change.Property == IsVisibleProperty)
        {
            UpdatePseudoClasses();
        }
    }

    public override void Render(DrawingContext context)
    {
        if (Foreground is not { } foreground)
        {
            return;
        }

        var side = Math.Min(Bounds.Width, Bounds.Height);
        var radius = (side - StrokeThickness) / 2;
        if (radius <= 0)
        {
            return;
        }

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var track = new Pen(Dim(foreground), StrokeThickness);
        context.DrawEllipse(null, track, center, radius, radius);

        var sweep = Value is { } value ? Math.Clamp(value, 0, 1) : IndeterminateSweep;
        if (sweep <= 0)
        {
            return;
        }

        var arc = new Pen(foreground, StrokeThickness, lineCap: PenLineCap.Round);
        if (sweep >= 1)
        {
            context.DrawEllipse(null, arc, center, radius, radius);
            return;
        }

        context.DrawGeometry(null, arc, CreateArc(center, radius, sweep));
    }

    /// <summary>Дуга от верхней точки по часовой стрелке — как у кольца на холсте.</summary>
    private static StreamGeometry CreateArc(Point center, double radius, double sweep)
    {
        var angle = sweep * 2 * Math.PI;
        var start = new Point(center.X, center.Y - radius);
        var end = new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle));

        var geometry = new StreamGeometry();
        using (var stream = geometry.Open())
        {
            stream.BeginFigure(start, isFilled: false);
            stream.ArcTo(end, new Size(radius, radius), 0, isLargeArc: sweep > 0.5, SweepDirection.Clockwise);
            stream.EndFigure(isClosed: false);
        }

        return geometry;
    }

    private static IBrush Dim(IBrush brush)
        => brush is ISolidColorBrush solid
            ? new ImmutableSolidColorBrush(solid.Color, solid.Opacity * TrackOpacity)
            : brush;

    /// <summary>
    /// Крутится только видимое кольцо: спрятанное, но с бесконечной анимацией, держало бы
    /// перерисовку окна каждый кадр.
    /// </summary>
    private void UpdatePseudoClasses() => PseudoClasses.Set(":indeterminate", IsIndeterminate && IsVisible);
}
