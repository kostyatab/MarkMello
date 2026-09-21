using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace MarkMello.Presentation.Views;

/// <summary>
/// Однострочная подпись, которая не помещается, плавно затухает у правого края
/// вместо многоточия: так видно на несколько букв больше, и край не рвётся.
/// </summary>
/// <remarks>
/// Содержимое меряется без ограничения по ширине и раскладывается целиком, а лишнее
/// обрезается краем декоратора. Затухание (<see cref="FadeWidth"/>) — маска
/// прозрачности на самом декораторе и только при переполнении; ширина маски не
/// больше скрытого остатка, как у таблиц, поэтому она сужается к нулю без скачка.
/// Маска считается по уже готовой раскладке, отдельного прохода не нужно.
/// <para>
/// Резать подпись нужно только справа, а и обрезка, и маска прозрачности идут по
/// границам декоратора. Поэтому границы выходят за строку текста сверху, снизу и
/// слева на <see cref="Bleed"/>: отрицательный <see cref="Layoutable.Margin"/>
/// отдаёт это поле наружу, такой же <see cref="Decorator.Padding"/> возвращает
/// подпись на место. В раскладке декоратор занимает ровно строку, а диакритика
/// над и под ней и глифы, выступающие влево (курсивные «j», «f»), не срезаются.
/// </para>
/// </remarks>
public sealed class TrailingFadeDecorator : Decorator
{
    /// <summary>Ширина затухания у переполненной подписи.</summary>
    public const double FadeWidth = 24;

    private const double Tolerance = 0.5;

    /// <summary>Поле вокруг подписи, где она рисуется без обрезки: справа его нет.</summary>
    private static readonly Thickness Bleed = new(4, 6, 0, 6);

    private double _maskWidth;

    public TrailingFadeDecorator()
    {
        ClipToBounds = true;
        Margin = new Thickness(-Bleed.Left, -Bleed.Top, -Bleed.Right, -Bleed.Bottom);
        Padding = Bleed;
    }

    /// <summary>Ширина затухания у правого края, 0 — подпись помещается.</summary>
    internal double Fade { get; private set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child is not { } child)
        {
            return default;
        }

        var padding = Padding;
        child.Measure(availableSize.Deflate(padding).WithWidth(double.PositiveInfinity));
        var desired = child.DesiredSize.Inflate(padding);
        return new Size(Math.Min(desired.Width, availableSize.Width), desired.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Child is not { } child)
        {
            return finalSize;
        }

        var slot = new Rect(finalSize).Deflate(Padding);
        var contentWidth = Math.Max(child.DesiredSize.Width, slot.Width);
        child.Arrange(slot.WithWidth(contentWidth));
        UpdateFade(contentWidth - slot.Width, finalSize.Width);
        return finalSize;
    }

    private void UpdateFade(double hiddenWidth, double width)
    {
        var fade = hiddenWidth < Tolerance || width <= 0 ? 0 : Math.Min(hiddenWidth, FadeWidth);

        // The mask stop is a fraction of the width, so a new width needs a new
        // mask even when the fade itself stays the same.
        if (Math.Abs(fade - Fade) < Tolerance
            && (fade > 0) == (OpacityMask is not null)
            && (fade <= 0 || Math.Abs(width - _maskWidth) < Tolerance))
        {
            return;
        }

        Fade = fade;
        _maskWidth = width;
        OpacityMask = fade <= 0 ? null : CreateMask(Math.Max(0, 1 - fade / width));
    }

    /// <summary>
    /// Маске важна только прозрачность: непрозрачный цвет оставляет подпись видимой,
    /// прозрачный — прячет.
    /// </summary>
    private static LinearGradientBrush CreateMask(double fadeStart)
        => new()
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Colors.Black, 0),
                new GradientStop(Colors.Black, fadeStart),
                new GradientStop(Colors.Transparent, 1)
            }
        };
}
