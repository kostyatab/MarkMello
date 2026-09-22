using Avalonia;
using MarkMello.Domain;
using MarkMello.Presentation.Views.Markdown;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Поля ячейки таблицы — .5em × .643em, округлённые до пикселя экрана: при
/// дробном масштабе целое логическое значение пикселю не соответствует.
/// </summary>
public sealed class MarkdownTableCellPaddingTests
{
    [Theory]
    // При 14 — ровно 7 × 9, как в Notion.
    [InlineData(14, 1, 9, 7)]
    // Половина пикселя уходит вверх: 6.5 → 7 и 7.5 → 8, а не к чётному.
    [InlineData(13, 1, 8, 7)]
    [InlineData(15, 1, 10, 8)]
    // Масштаб 1.5: 9 × 1.5 = 13.5 → 14 и 7 × 1.5 = 10.5 → 11 пикселей экрана.
    [InlineData(14, 1.5, 14 / 1.5, 11 / 1.5)]
    public void PaddingIsRoundedToWholeDevicePixels(int fontSize, double scale, double horizontal, double vertical)
    {
        var metrics = new MarkdownDocumentMetrics(ReadingPreferences.Default with { FontSize = fontSize });

        var padding = metrics.GetTableCellPadding(scale);

        Assert.Equal(new Thickness(horizontal, vertical), padding);
    }
}
