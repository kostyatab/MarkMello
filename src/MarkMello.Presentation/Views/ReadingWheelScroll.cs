using Avalonia;
using Avalonia.Controls;

namespace MarkMello.Presentation.Views;

/// <summary>
/// Прокрутка колесом в режиме чтения: шаг крупнее стандартного, как в браузере.
/// Один шаг на документ и на всё, что лежит поверх него, — иначе список рядом
/// с документом крутился бы заметно медленнее самого документа.
/// </summary>
internal static class ReadingWheelScroll
{
    public const double StepMultiplier = 6.0;

    /// <summary>
    /// Прокручивает по вертикали и отвечает, сдвинулось ли содержимое. Горизонтальные
    /// жесты не трогает — они для вложенных блоков кода; у края прокрутки возвращает
    /// false, чтобы колесо ушло дальше.
    /// </summary>
    public static bool TryScroll(ScrollViewer scroll, Vector delta)
    {
        if (Math.Abs(delta.Y) <= double.Epsilon || Math.Abs(delta.X) > Math.Abs(delta.Y))
        {
            return false;
        }

        var maxOffset = scroll.ScrollBarMaximum.Y;
        if (maxOffset <= 0)
        {
            return false;
        }

        var baseStep = scroll.SmallChange.Height > 0 ? scroll.SmallChange.Height : 16.0;
        var nextOffset = Math.Clamp(scroll.Offset.Y - delta.Y * baseStep * StepMultiplier, 0, maxOffset);
        if (Math.Abs(nextOffset - scroll.Offset.Y) <= double.Epsilon)
        {
            return false;
        }

        scroll.Offset = new Vector(scroll.Offset.X, nextOffset);
        return true;
    }
}
