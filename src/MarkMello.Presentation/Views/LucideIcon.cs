using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace MarkMello.Presentation.Views;

/// <summary>
/// Иконка Lucide: геометрия из <c>Themes/Icons.axaml</c> в исходной сетке 24×24,
/// вписанная в заданные <c>Width</c>/<c>Height</c>. Рисуется как в оригинале:
/// обводка 2 единицы сетки (масштабируется вместе с иконкой), скруглённые концы
/// и стыки, без заливки.
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
    private const double StrokeThickness = 2;

    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<LucideIcon, Geometry?>(nameof(Data));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<LucideIcon>();

    private Pen? _pen;

    static LucideIcon()
    {
        AffectsRender<LucideIcon>(DataProperty, ForegroundProperty);
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

    public override void Render(DrawingContext context)
    {
        var data = Data;
        var foreground = Foreground;
        var side = Math.Min(Bounds.Width, Bounds.Height);
        if (data is null || foreground is null || side <= 0)
        {
            return;
        }

        // Сетка масштабируется целиком, вместе с обводкой, и встаёт по центру,
        // если контрол не квадратный.
        var scale = side / GridSize;
        var offset = new Vector((Bounds.Width - side) / 2, (Bounds.Height - side) / 2);
        _pen ??= new Pen(foreground, StrokeThickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offset)))
        {
            context.DrawGeometry(null, _pen, data);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ForegroundProperty)
        {
            _pen = null;
        }
    }
}
