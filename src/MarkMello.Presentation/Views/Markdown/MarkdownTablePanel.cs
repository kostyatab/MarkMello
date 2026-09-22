using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace MarkMello.Presentation.Views.Markdown;

/// <summary>
/// Раскладка ячеек markdown-таблицы: колонка шириной в свою самую широкую ячейку,
/// строка высотой в свою самую высокую ячейку. Текст ячеек не переносится, а
/// картинки сжимаются, чтобы таблица помещалась в колонку чтения
/// (<see cref="IsShrinkableProperty"/>).
/// </summary>
/// <remarks>
/// Панель живёт внутри горизонтального <see cref="ScrollViewer"/>
/// (<see cref="MarkdownTableHost"/>): таблица шире колонки чтения сохраняет свою
/// ширину и прокручивается целиком. Таблица уже колонки остаётся шириной в свои
/// колонки, как в Notion, — колонка чтения (<see cref="ReadingColumnWidth"/>)
/// ограничивает только картинки.
/// <para>
/// Ячейки — дочерние контролы по строкам слева направо, ровно
/// <see cref="ColumnCount"/> на строку.
/// </para>
/// </remarks>
internal sealed class MarkdownTablePanel : Panel
{
    private double[] _columnWidths = [];
    private double[] _rowHeights = [];
    private double _readingColumnWidth = double.NaN;

    public MarkdownTablePanel(int columnCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columnCount);
        ColumnCount = columnCount;
    }

    /// <summary>Уже этого колонку с картинками не сжимаем: дальше таблица прокручивается.</summary>
    public const double ShrinkableColumnMinWidth = 120;

    /// <summary>
    /// Ячейка, которая сжимается до выделенной ей ширины (картинки). Остальные
    /// ячейки — текст без переноса — всегда получают свою ширину целиком.
    /// </summary>
    public static readonly AttachedProperty<bool> IsShrinkableProperty =
        AvaloniaProperty.RegisterAttached<MarkdownTablePanel, Control, bool>("IsShrinkable");

    public int ColumnCount { get; }

    /// <summary>
    /// Ширина колонки чтения: в неё ужимаются колонки с картинками.
    /// <see cref="double.NaN"/> — без ограничения.
    /// </summary>
    public double ReadingColumnWidth
    {
        get => _readingColumnWidth;
        set
        {
            if (_readingColumnWidth.Equals(value))
            {
                return;
            }

            _readingColumnWidth = value;
            InvalidateMeasure();
        }
    }

    public static bool GetIsShrinkable(Control cell) => cell.GetValue(IsShrinkableProperty);

    public static void SetIsShrinkable(Control cell, bool value) => cell.SetValue(IsShrinkableProperty, value);

    protected override Size MeasureOverride(Size availableSize)
    {
        var fixedWidths = new double[ColumnCount];
        var shrinkableWidths = new double[ColumnCount];
        _rowHeights = new double[(Children.Count + ColumnCount - 1) / ColumnCount];

        for (var index = 0; index < Children.Count; index++)
        {
            var cell = Children[index];
            cell.Measure(Size.Infinity);

            var column = index % ColumnCount;
            if (GetIsShrinkable(cell))
            {
                shrinkableWidths[column] = Math.Max(shrinkableWidths[column], cell.DesiredSize.Width);
                continue;
            }

            fixedWidths[column] = Math.Max(fixedWidths[column], cell.DesiredSize.Width);
            _rowHeights[index / ColumnCount] = Math.Max(_rowHeights[index / ColumnCount], cell.DesiredSize.Height);
        }

        _columnWidths = ComputeColumnWidths(
            fixedWidths,
            shrinkableWidths,
            double.IsNaN(ReadingColumnWidth) ? double.PositiveInfinity : ReadingColumnWidth);

        // A shrinkable cell's height depends on the width its column got.
        for (var index = 0; index < Children.Count; index++)
        {
            var cell = Children[index];
            if (!GetIsShrinkable(cell))
            {
                continue;
            }

            cell.Measure(new Size(_columnWidths[index % ColumnCount], double.PositiveInfinity));
            _rowHeights[index / ColumnCount] = Math.Max(_rowHeights[index / ColumnCount], cell.DesiredSize.Height);
        }

        return new Size(_columnWidths.Sum(), _rowHeights.Sum());
    }

    /// <summary>
    /// Ширины колонок: текст получает свою ширину целиком, картинки — не шире
    /// <paramref name="limit"/>. Если таблица с картинками в него не влезает, колонки
    /// с картинками сжимаются (как колонки HTML-таблицы от максимальной ширины к
    /// минимальной), но не уже <see cref="ShrinkableColumnMinWidth"/>; дальше таблица
    /// прокручивается.
    /// </summary>
    internal static double[] ComputeColumnWidths(
        IReadOnlyList<double> fixedWidths,
        IReadOnlyList<double> shrinkableWidths,
        double limit)
    {
        var count = fixedWidths.Count;
        var maxWidths = new double[count];
        var minWidths = new double[count];
        for (var column = 0; column < count; column++)
        {
            var shrinkable = Math.Min(shrinkableWidths[column], limit);
            maxWidths[column] = Math.Max(fixedWidths[column], shrinkable);
            minWidths[column] = shrinkable > fixedWidths[column]
                ? Math.Max(fixedWidths[column], Math.Min(shrinkable, ShrinkableColumnMinWidth))
                : maxWidths[column];
        }

        var maxSum = maxWidths.Sum();
        var minSum = minWidths.Sum();
        if (maxSum <= limit)
        {
            return maxWidths;
        }

        if (minSum >= limit)
        {
            return minWidths;
        }

        var share = (limit - minSum) / (maxSum - minSum);
        for (var column = 0; column < count; column++)
        {
            minWidths[column] += (maxWidths[column] - minWidths[column]) * share;
        }

        return minWidths;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var width = _columnWidths.Sum();
        var scale = LayoutHelper.GetLayoutScale(this);

        // Границы колонок округляются до пикселя, чтобы линии сетки соседних
        // ячеек сходились без щелей и наложений.
        var columnEdges = new double[ColumnCount + 1];
        var left = 0d;
        for (var column = 0; column < ColumnCount; column++)
        {
            left += _columnWidths[column];
            columnEdges[column + 1] = column == ColumnCount - 1
                ? width
                : LayoutHelper.RoundLayoutValue(left, scale);
        }

        var top = 0d;
        for (var index = 0; index < Children.Count; index++)
        {
            var column = index % ColumnCount;
            var row = index / ColumnCount;
            if (column == 0 && row > 0)
            {
                top += _rowHeights[row - 1];
            }

            Children[index].Arrange(new Rect(
                columnEdges[column],
                top,
                columnEdges[column + 1] - columnEdges[column],
                _rowHeights[row]));
        }

        return new Size(width, _rowHeights.Sum());
    }
}
