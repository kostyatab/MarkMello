using Avalonia.Media;
using MarkMello.Presentation.Views.Markdown;

namespace MarkMello.Presentation.Tests;

[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class MarkdownInlineCodePadMetricsTests
{
    private readonly AvaloniaHeadlessFixture _fixture;

    public MarkdownInlineCodePadMetricsTests(AvaloniaHeadlessFixture fixture) => _fixture = fixture;

    [Fact]
    public void SpacerRunsUseAbsoluteBaselineFromPadMetrics()
    {
        var metrics = new MarkdownInlineCodePadMetrics(
            LeftWidth: 4,
            RightWidth: 4,
            Height: 12,
            Baseline: 9,
            BoxHeight: 16);

        var left = MarkdownSpacerTextRun.Left(metrics);
        var right = MarkdownSpacerTextRun.Right(metrics);

        Assert.Equal(9, left.Baseline);
        Assert.Equal(9, right.Baseline);

        // Поля по высоте — строка кода, а не плашка: строку они не раздвигают.
        Assert.Equal(12, left.Size.Height);
    }

    [Theory]
    [InlineData(14)]
    [InlineData(18)]
    public Task PaddingIsInEmOfTheCodeFont(double textFontSize)
    {
        return _fixture.Session.Dispatch(() =>
        {
            // Инлайн-код .85em текста, поля .2em × .4em кегля кода, как em в CSS.
            var codeFontSize = textFontSize * 0.85;
            var metrics = MarkdownInlineCodePadMetrics.Create(
                new FontFamily("avares://MarkMello.Presentation/Assets/Fonts/JetBrainsMono#JetBrains Mono"),
                codeFontSize,
                FontWeight.Normal,
                FontStyle.Normal,
                Brushes.Black);

            Assert.Equal(codeFontSize * 0.4, metrics.LeftWidth, 3);
            Assert.Equal(codeFontSize * 0.4, metrics.RightWidth, 3);
            Assert.Equal(metrics.Height + codeFontSize * 0.4, metrics.BoxHeight, 3);

            // Плашка кода — около 1.45 размера текста (высота клавиши по макету).
            Assert.InRange(metrics.BoxHeight / textFontSize, 1.40, 1.50);
        }, CancellationToken.None);
    }
}
