using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using MarkMello.Domain;
using MarkMello.Presentation.Views;
using MarkMello.Presentation.Views.Markdown;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Front matter — свойства, как в Notion, а не таблица: колонка ключей мягким
/// цветом, значения цветом текста, линии только сверху и снизу. Вид не зависит
/// от раскладки таблиц.
/// </summary>
[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class MarkdownFrontMatterLayoutTests
{
    private const double FontSize = 14;
    private const double BlockFontSize = FontSize * 0.875;

    private readonly AvaloniaHeadlessFixture _fixture;

    public MarkdownFrontMatterLayoutTests(AvaloniaHeadlessFixture fixture) => _fixture = fixture;

    [Fact]
    public Task FrontMatterIsDrawnAsPropertiesAndNotAsATable()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(Document());
            var window = Show(view);
            var block = FrontMatter(view);

            Assert.Empty(block.GetVisualDescendants().OfType<MarkdownTableHost>());
            Assert.DoesNotContain(
                block.GetVisualDescendants().OfType<Border>(),
                static border => border.Classes.Any(static name => name.StartsWith("mm-md-table", StringComparison.Ordinal)));

            var grid = Grid(block);
            Assert.Equal(2, grid.ColumnDefinitions.Count);
            Assert.Equal(2, grid.RowDefinitions.Count);
            Assert.Equal("id", Key(grid, 0).StyledText.Text);
            Assert.Equal("MM-48", Value(grid, 0).StyledText.Text);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task KeyAndValueAreRegularTextAtSevenEighthsOfTheTextSize()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(Document());
            var window = Show(view);
            var grid = Grid(FrontMatter(view));

            var key = Key(grid, 0);
            var value = Value(grid, 0);
            Assert.Empty(key.StyledText.Spans);
            Assert.Empty(value.StyledText.Spans);
            Assert.Equal(FontWeight.Normal, key.BaseFontWeight);
            Assert.Equal(FontWeight.Normal, value.BaseFontWeight);
            Assert.Equal(BlockFontSize, key.BaseFontSize, 3);
            Assert.Equal(BlockFontSize, value.BaseFontSize, 3);
            Assert.Equal(BlockFontSize * 1.45, value.BaseLineHeight, 3);
            Assert.Equal(TextWrapping.Wrap, value.LayoutTextWrapping);

            // Колонка ключей — 10 размеров текста, от ключа до значения — 1em блока.
            Assert.Equal(FontSize * 10, grid.ColumnDefinitions[0].ActualWidth, 1d);
            Assert.Equal(BlockFontSize, key.Margin.Right, 3);

            window.Close();
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("Light", "#5A544F", "#1F1915", "#E2DDD7")]
    [InlineData("Dark", "#ABA7A1", "#E7E4DF", "#3F3935")]
    public Task KeyIsSoftTheValueIsTextColouredAndTheLinesAreBorderColoured(
        string theme, string keyColor, string valueColor, string lineColor)
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(Document());
            var window = Show(view, theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light);
            var block = FrontMatter(view);
            var grid = Grid(block);

            Assert.Equal(Color.Parse(keyColor), Assert.IsAssignableFrom<ISolidColorBrush>(Key(grid, 0).ResolveBaseTextBrush()).Color);
            Assert.Equal(Color.Parse(valueColor), Assert.IsAssignableFrom<ISolidColorBrush>(Value(grid, 0).ResolveBaseTextBrush()).Color);
            Assert.Equal(Color.Parse(lineColor), Assert.IsAssignableFrom<ISolidColorBrush>(block.BorderBrush).Color);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task LinesRunAboveAndBelowOneBlockEmFromTheRowsAndNotBetweenThem()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(Document());
            var window = Show(view);
            var block = FrontMatter(view);

            Assert.Equal(new Thickness(0, 1, 0, 1), block.BorderThickness);
            Assert.Equal(new Thickness(0, BlockFontSize, 0, BlockFontSize), block.Padding);
            Assert.Equal(Root(view).Bounds.Width, block.Bounds.Width, 1d);
            Assert.DoesNotContain(
                block.GetVisualDescendants().OfType<Border>(),
                static border => border.BorderThickness != default);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task RowsAreAtLeastTwoAndAnEighthTextSizesTallWithTheTextCentred()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(Document());
            var window = Show(view);
            var grid = Grid(FrontMatter(view));

            var row = grid.RowDefinitions[0];
            Assert.Equal(FontSize * 2.125, row.ActualHeight, 1d);

            var key = Key(grid, 0);
            var value = Value(grid, 0);
            Assert.Equal(row.ActualHeight / 2, key.Bounds.Center.Y, 1d);
            Assert.Equal(row.ActualHeight / 2, value.Bounds.Center.Y, 1d);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task ALongValueWrapsInsideItsColumn()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(new RenderedMarkdownDocument(
            [
                new MarkdownFrontMatterBlock(
                [
                    new MarkdownFrontMatterEntry(
                        "description",
                        string.Join(' ', Enumerable.Repeat("Every common Markdown element in one file", 6)))
                ])
            ]));
            var window = Show(view);
            var grid = Grid(FrontMatter(view));
            var value = Value(grid, 0);

            Assert.True(value.Bounds.Height > BlockFontSize * 1.45 * 2);
            Assert.True(value.Bounds.Right <= grid.Bounds.Width + 0.5);
            Assert.True(grid.RowDefinitions[0].ActualHeight > FontSize * 2.125);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task AnEmptyValueIsAnEmptyCell()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(Document());
            var window = Show(view);
            var grid = Grid(FrontMatter(view));

            Assert.Equal("empty", Key(grid, 1).StyledText.Text);
            Assert.DoesNotContain(
                grid.Children,
                static child => Avalonia.Controls.Grid.GetRow(child) == 1 && Avalonia.Controls.Grid.GetColumn(child) == 1 && child.Bounds.Height > 0);
            Assert.Equal(FontSize * 2.125, grid.RowDefinitions[1].ActualHeight, 1d);

            window.Close();
        }, CancellationToken.None);
    }

    /// <summary>
    /// До H1 под свойствами — просвет как перед H2, а не больший из двух просветов.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task TheBlockBelowIsTwoPointTwoNineTextSizesAway(bool heading)
    {
        return _fixture.Session.Dispatch(() =>
        {
            MarkdownBlock next = heading
                ? new MarkdownHeadingBlock(1, [new MarkdownTextInline("Title")])
                : new MarkdownParagraphBlock([new MarkdownTextInline("Text")]);
            var view = CreateView(new RenderedMarkdownDocument([Document().Blocks[0], next]));
            var window = Show(view);

            var children = Root(view).Children;
            Assert.Equal(0, children[0].Margin.Top);
            Assert.Equal(FontSize * 2.29, children[1].Margin.Top, 3);

            window.Close();
        }, CancellationToken.None);
    }

    /// <summary>
    /// Компаратор блоков сравнивает front matter по содержимому: неизменившийся
    /// документ переиспользует готовый контрол, а не пересобирает его.
    /// </summary>
    [Fact]
    public Task AnUnchangedDocumentReusesTheFrontMatterControl()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(Document());
            var window = Show(view);
            var before = Assert.Single(Root(view).Children);

            view.Document = Document();
            var afterSameDocument = Assert.Single(Root(view).Children);

            view.Document = new RenderedMarkdownDocument(
            [
                new MarkdownFrontMatterBlock([new MarkdownFrontMatterEntry("id", "MM-49")])
            ]);
            var afterEdit = Assert.Single(Root(view).Children);

            Assert.Same(before, afterSameDocument);
            Assert.NotSame(before, afterEdit);

            window.Close();
        }, CancellationToken.None);
    }

    private static RenderedMarkdownDocument Document()
        => new(
        [
            new MarkdownFrontMatterBlock(
            [
                new MarkdownFrontMatterEntry("id", "MM-48"),
                new MarkdownFrontMatterEntry("empty", string.Empty)
            ])
        ]);

    private static MarkdownDocumentView CreateView(RenderedMarkdownDocument document)
        => new()
        {
            ReadingPreferences = ReadingPreferences.Default,
            Document = document
        };

    private static Window Show(MarkdownDocumentView view, ThemeVariant? theme = null)
    {
        var window = ThemedTestWindow.Create(theme ?? ThemeVariant.Light, view);
        window.Width = 600;
        window.Height = 400;
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static StackPanel Root(MarkdownDocumentView view)
    {
        var viewport = Assert.IsType<Border>(view.Content);
        return Assert.IsType<StackPanel>(viewport.Child);
    }

    private static Border FrontMatter(MarkdownDocumentView view)
    {
        var block = Assert.IsType<Border>(Root(view).Children[0]);
        Assert.Contains("mm-md-front-matter", block.Classes);
        return block;
    }

    private static Grid Grid(Border block) => Assert.IsType<Grid>(block.Child);

    private static MarkdownSelectionTextFragment Key(Grid grid, int row) => Cell(grid, row, 0);

    private static MarkdownSelectionTextFragment Value(Grid grid, int row) => Cell(grid, row, 1);

    private static MarkdownSelectionTextFragment Cell(Grid grid, int row, int column)
        => Assert.IsType<MarkdownSelectionTextFragment>(Assert.Single(
            grid.Children,
            child => Avalonia.Controls.Grid.GetRow(child) == row && Avalonia.Controls.Grid.GetColumn(child) == column));
}
