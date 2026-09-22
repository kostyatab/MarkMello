using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace MarkMello.Presentation.Views.Markdown;

/// <summary>
/// Шрифты и размеры документа для видов вне текстового потока — картинок и
/// диаграмм. Строит их MarkdownDocumentView на каждой пересборке.
/// </summary>
internal sealed record MarkdownBlockTypography(
    MarkdownDocumentMetrics Metrics,
    FontFamily BodyFontFamily,
    FontFamily MonoFontFamily);

/// <summary>
/// «Место под картинку» — пунктирная рамка на месте того, что не удалось
/// показать: битой или ещё не загруженной картинки, неудавшейся диаграммы.
/// Содержимое стоит по центру рамки.
/// </summary>
/// <remarks>
/// У <see cref="Border"/> нет пунктира, поэтому рамка — прямоугольник под
/// содержимым. Строчные картинки рисуют ту же рамку сами, пером
/// <see cref="CreatePen"/>.
/// </remarks>
internal static class MarkdownMissingContentFrame
{
    // Штрих и просвет пунктира — в толщинах линии, как у CSS dashed в Chrome.
    private static readonly double[] DashPattern = [3, 3];

    private const double StrokeThickness = 1;

    public static Grid Create(MarkdownDocumentMetrics metrics, Thickness padding, double minHeight, Control content)
    {
        var radius = metrics.MissingFrameCornerRadius;
        var frame = new Rectangle
        {
            StrokeThickness = StrokeThickness,
            StrokeDashArray = [.. DashPattern],
            RadiusX = radius,
            RadiusY = radius,
            Classes = { "mm-md-missing-frame" }
        };

        return new Grid
        {
            MinHeight = minHeight,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Classes = { "mm-md-missing" },
            Children =
            {
                frame,
                // Поле отсчитывается от внутреннего края линии, как у рамки CSS.
                new Border
                {
                    Padding = padding + new Thickness(StrokeThickness),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = content
                }
            }
        };
    }

    /// <summary>Иконка над подписью: image-off у картинки, circle-alert у диаграммы — по классу.</summary>
    public static LucideIcon CreateIcon(MarkdownDocumentMetrics metrics, string className)
    {
        var size = metrics.MissingIconSize;
        return new LucideIcon
        {
            Width = size,
            Height = size,
            HorizontalAlignment = HorizontalAlignment.Center,
            Classes = { "mm-md-missing-icon", className }
        };
    }

    /// <summary>Перо пунктирной рамки для тех, кто рисует её сам.</summary>
    public static Pen CreatePen(IBrush brush)
        => new(brush, StrokeThickness, new DashStyle(DashPattern, 0));

    /// <summary>
    /// Рамка внутри <paramref name="bounds"/>: линия толщиной 1 по центру на
    /// полпикселя внутрь, чтобы не размываться и не вылезать за границы.
    /// </summary>
    public static void Draw(DrawingContext context, Pen pen, Rect bounds, double cornerRadius)
    {
        var rect = bounds.Deflate(0.5);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        context.DrawRectangle(null, pen, rect, cornerRadius, cornerRadius);
    }
}
