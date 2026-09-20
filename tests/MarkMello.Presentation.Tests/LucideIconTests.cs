using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Media;
using MarkMello.Presentation.Views;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Контрол, которым нарисованы все иконки интерфейса: размер задаёт разметка,
/// цвет — кнопка, а обводка держится заданной толщины в пикселях независимо
/// от размера значка.
/// </summary>
[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class LucideIconTests
{
    private const string Chevron = "M9 18l6-6-6-6";

    private readonly AvaloniaHeadlessFixture _fixture;

    public LucideIconTests(AvaloniaHeadlessFixture fixture) => _fixture = fixture;

    [Theory]
    [InlineData(14)]
    [InlineData(12)]
    public Task LaysOutAtTheGivenSize(double size)
    {
        return _fixture.Session.Dispatch(() =>
        {
            var icon = new LucideIcon { Data = Geometry.Parse(Chevron), Width = size, Height = size };
            var window = new Window { Content = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Children = { icon } } };
            window.Show();
            window.UpdateLayout();

            Assert.Equal(new Size(size, size), icon.Bounds.Size);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task TakesItsColourFromTheButtonForeground()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var icon = new LucideIcon { Data = Geometry.Parse(Chevron), Width = 14, Height = 14 };
            var button = new Button { Foreground = Brushes.Red, Content = icon };
            var window = new Window { Content = button };
            window.Show();

            Assert.Same(Brushes.Red, icon.Foreground);

            button.Foreground = Brushes.Blue;

            Assert.Same(Brushes.Blue, icon.Foreground);
            Assert.Same(Brushes.Blue, StrokeOf(icon).Brush);

            window.Close();
        }, CancellationToken.None);
    }

    /// <summary>
    /// Иконка декоративная: указатель достаётся кнопке под ней, а экранный диктор
    /// читает имя кнопки, а не пустой элемент внутри.
    /// </summary>
    [Fact]
    public Task IsInvisibleToThePointerAndToScreenReaders()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var icon = new LucideIcon { Data = Geometry.Parse(Chevron), Width = 14, Height = 14 };
            var peer = ControlAutomationPeer.CreatePeerForElement(icon);

            Assert.False(icon.IsHitTestVisible);
            Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(icon));
            Assert.False(peer.IsControlElement());
            Assert.False(peer.IsContentElement());
        }, CancellationToken.None);
    }

    /// <summary>
    /// Геометрия рисуется на сетке 24 со скруглёнными концами и стыками, без заливки;
    /// масштаб — сторона иконки к 24. Перо задано в единицах сетки так, чтобы после
    /// масштабирования линия вышла заданной толщины в пикселях.
    /// </summary>
    [Theory]
    [InlineData(24, 24, 1, 0, 0)]
    [InlineData(12, 12, 0.5, 0, 0)]
    [InlineData(14, 12, 0.5, 1, 0)]
    public Task DrawsTheGeometryOnLucidesGridWithTheDefaultStroke(
        double width,
        double height,
        double expectedScale,
        double expectedOffsetX,
        double expectedOffsetY)
    {
        return _fixture.Session.Dispatch(() =>
        {
            var data = Geometry.Parse(Chevron);
            var icon = new LucideIcon { Data = data, Foreground = Brushes.Black };
            icon.Measure(new Size(width, height));
            icon.Arrange(new Rect(0, 0, width, height));

            var (transform, drawing) = RenderOnce(icon);
            var pen = Assert.IsAssignableFrom<IPen>(drawing.Pen);

            Assert.Equal(Matrix.CreateScale(expectedScale, expectedScale) * Matrix.CreateTranslation(expectedOffsetX, expectedOffsetY), transform);
            Assert.Equal(data.Bounds, Assert.IsAssignableFrom<Geometry>(drawing.Geometry).Bounds);
            Assert.Null(drawing.Brush);
            Assert.Equal(1.75, pen.Thickness * expectedScale, 6);
            Assert.Equal(PenLineCap.Round, pen.LineCap);
            Assert.Equal(PenLineJoin.Round, pen.LineJoin);
        }, CancellationToken.None);
    }

    /// <summary>
    /// Регресс: обводка задавалась в единицах сетки и росла вместе со значком —
    /// иконка 28 px выходила вдвое жирнее иконки 14 px. Теперь толщина в пикселях
    /// та, что задана, а крупным значкам её можно убавить.
    /// </summary>
    [Theory]
    [InlineData(14, 1.75)]
    [InlineData(24, 1.75)]
    [InlineData(28, 1.5)]
    public Task KeepsTheStrokeAtTheGivenWidthInPixels(double size, double strokeThickness)
    {
        return _fixture.Session.Dispatch(() =>
        {
            var icon = new LucideIcon
            {
                Data = Geometry.Parse(Chevron),
                Foreground = Brushes.Black,
                StrokeThickness = strokeThickness
            };
            icon.Measure(new Size(size, size));
            icon.Arrange(new Rect(0, 0, size, size));

            var (transform, drawing) = RenderOnce(icon);
            var pen = Assert.IsAssignableFrom<IPen>(drawing.Pen);

            Assert.Equal(strokeThickness, pen.Thickness * transform.M11, 6);
        }, CancellationToken.None);
    }

    [Fact]
    public Task DrawsNothingWithoutAGeometry()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var icon = new LucideIcon { Foreground = Brushes.Black };
            icon.Measure(new Size(14, 14));
            icon.Arrange(new Rect(0, 0, 14, 14));

            var group = new DrawingGroup();
            using (var context = group.Open())
            {
                icon.Render(context);
            }

            Assert.Empty(group.Children);
        }, CancellationToken.None);
    }

    private static IPen StrokeOf(LucideIcon icon)
    {
        icon.Measure(new Size(icon.Width, icon.Height));
        icon.Arrange(new Rect(0, 0, icon.Width, icon.Height));
        return Assert.IsAssignableFrom<IPen>(RenderOnce(icon).Drawing.Pen);
    }

    private static (Matrix Transform, GeometryDrawing Drawing) RenderOnce(LucideIcon icon)
    {
        var group = new DrawingGroup();
        using (var context = group.Open())
        {
            icon.Render(context);
        }

        var transformed = Assert.IsType<DrawingGroup>(Assert.Single(group.Children));
        var drawing = Assert.IsType<GeometryDrawing>(Assert.Single(transformed.Children));
        return (Assert.IsAssignableFrom<Transform>(transformed.Transform).Value, drawing);
    }
}
