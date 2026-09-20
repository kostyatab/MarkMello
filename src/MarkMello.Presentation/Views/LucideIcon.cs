using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace MarkMello.Presentation.Views;

/// <summary>
/// Иконка Lucide: геометрия из <c>Themes/Icons.axaml</c> в исходной сетке 24×24,
/// вписанная в заданные <c>Width</c>/<c>Height</c>. Рисуется как в оригинале:
/// скруглённые концы и стыки, без заливки. Обводка (<see cref="StrokeThickness"/>)
/// задана в единицах сетки и масштабируется вместе со значком, как
/// <c>stroke-width</c> у SVG с <c>viewBox="0 0 24 24"</c>.
/// </summary>
/// <remarks>
/// Цвет наследуется через <see cref="TextElement.ForegroundProperty"/>, как у текста:
/// кнопка перекрашивает иконку в своих состояниях, меняя <c>Foreground</c>
/// своего ContentPresenter. Иконка декоративная — смысл кнопки несут тултип или
/// подпись, — поэтому она прозрачна для мыши и не попадает в дерево доступности.
/// <para>
/// Своего размера у иконки нет: без <c>Width</c>/<c>Height</c> она занимает то, что
/// оставил родитель, а в StackPanel или Auto-колонке схлопывается в 0×0 и молча
/// пропадает. Поэтому размер задаётся на каждой иконке явно — в разметке это
/// проверяет <c>IconMarkupTests</c>.
/// </para>
/// </remarks>
public sealed class LucideIcon : Control
{
    private const double GridSize = 24;

    /// <summary>Обводка иконки оболочки — 1.75 единицы сетки, как на холсте «Варианта A».</summary>
    private const double DefaultStrokeThickness = 1.75;

    /// <summary>Каноничная обводка Lucide — 2 единицы сетки; ею рисует документ.</summary>
    private const double CanonicalStrokeThickness = 2;

    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<LucideIcon, Geometry?>(nameof(Data));

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<LucideIcon, double>(nameof(StrokeThickness), DefaultStrokeThickness);

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<LucideIcon>();

    private Pen? _pen;

    static LucideIcon()
    {
        AffectsRender<LucideIcon>(DataProperty, ForegroundProperty, StrokeThicknessProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<LucideIcon>(false);
        AutomationProperties.AccessibilityViewProperty.OverrideDefaultValue<LucideIcon>(AccessibilityView.Raw);
    }

    public Geometry? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>
    /// Толщина обводки в единицах сетки 24. Крупным значкам холст ставит меньше
    /// (1.5): при том же правиле линия у них иначе выглядела бы тяжелее.
    /// </summary>
    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var data = Data;
        var foreground = Foreground;
        if (data is null || foreground is null)
        {
            return;
        }

        _pen ??= CreatePen(foreground, StrokeThickness);
        Draw(context, data, _pen, Bounds.Size);
    }

    /// <summary>
    /// Перо иконки Lucide: обводка в единицах сетки 24 со скруглёнными концами и
    /// стыками. Без толщины — каноничные 2 единицы, которыми рисует документ.
    /// </summary>
    internal static Pen CreatePen(IBrush foreground, double thickness = CanonicalStrokeThickness)
        => new(foreground, thickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

    /// <summary>
    /// Рисует геометрию Lucide в области <paramref name="size"/> так же, как сама
    /// иконка. Нужен тем, кто рисует иконку внутри своего <c>Render</c>, а не
    /// дочерним контролом, — например, чекбоксу task list в документе.
    /// </summary>
    internal static void Draw(DrawingContext context, Geometry data, Pen pen, Size size)
    {
        var side = Math.Min(size.Width, size.Height);
        if (side <= 0)
        {
            return;
        }

        // Сетка масштабируется целиком, вместе с обводкой, и встаёт по центру,
        // если область не квадратная.
        var scale = side / GridSize;
        var offset = new Vector((size.Width - side) / 2, (size.Height - side) / 2);

        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offset)))
        {
            context.DrawGeometry(null, pen, data);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ForegroundProperty || change.Property == StrokeThicknessProperty)
        {
            _pen = null;
        }
    }
}
