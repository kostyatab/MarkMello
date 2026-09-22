using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MarkMello.Application.Abstractions;
using MarkMello.Domain;
using MarkMello.Presentation.Views;
using MarkMello.Presentation.Views.Markdown;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Колонка таблицы шириной в своё содержимое, текст ячеек не переносится. Таблица
/// шире колонки чтения прокручивается целиком по горизонтали и, как в Notion,
/// выходит на поля страницы: в покое начинается от колонки, при прокрутке уходит в
/// левое поле. Таблица, которая помещается, шириной в свои колонки, как в Notion.
/// Регрессия: все колонки получали равную долю ширины, и узкие «#» и «CPU»
/// занимали столько же места, сколько длинное «Rationale».
/// </summary>
[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class MarkdownTableLayoutTests
{
    private const double Tolerance = 0.5;
    private const string LongText =
        "Terminates TLS and routes traffic to the application nodes; it holds no state, so it stays small, " +
        "restarts in seconds and can be replaced at any time without draining the connections it serves.";

    private readonly AvaloniaHeadlessFixture _fixture;

    public MarkdownTableLayoutTests(AvaloniaHeadlessFixture fixture) => _fixture = fixture;

    [Fact]
    public Task WideTableKeepsNarrowColumnsNarrowAndScrollsHorizontally()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(WideTable());
            var window = Show(view);
            var (scrollViewer, panel) = Table(view);

            Assert.True(
                scrollViewer.Extent.Width > scrollViewer.Viewport.Width + 1,
                "a table wider than the reading column should scroll");
            Assert.Equal(scrollViewer.Extent.Width, panel.Bounds.Width, Tolerance);

            // Каждая колонка ровно по своей самой широкой ячейке: на растягивание
            // лишнего места нет.
            for (var column = 0; column < panel.ColumnCount; column++)
            {
                var widest = Enumerable.Range(0, RowCount(panel))
                    .Max(row => Cell(panel, row, column).DesiredSize.Width);
                Assert.Equal(widest, Cell(panel, 0, column).Bounds.Width, Tolerance);
            }

            Assert.True(Cell(panel, 1, 0).Bounds.Width < Cell(panel, 1, 2).Bounds.Width / 5);

            // Длинный текст — одна строка, как у соседней короткой ячейки.
            Assert.Equal(Content(panel, 1, 0).Bounds.Height, Content(panel, 1, 2).Bounds.Height, Tolerance);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task WideTableReservesRoomForTheScrollBarBelowTheLastRow()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(WideTable());
            var window = Show(view);
            var (_, panel) = Table(view);

            Assert.True(panel.Margin.Bottom > 0);

            window.Close();
        }, CancellationToken.None);
    }

    /// <summary>Как в Notion: таблица, которая помещается, шириной в свои колонки, а не в колонку чтения.</summary>
    [Fact]
    public Task TableThatFitsIsAsWideAsItsColumnsWithoutScrolling()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(Document(Table(
                ["Setting", "Value"],
                ["Theme", "Dark"],
                ["Font", "Serif"])));
            var window = Show(view);
            var (scrollViewer, panel) = Table(view);

            Assert.True(panel.Bounds.Width < scrollViewer.Viewport.Width / 2);
            Assert.True(scrollViewer.Extent.Width <= scrollViewer.Viewport.Width + Tolerance, "a table that fits should not scroll");
            Assert.Equal(0, panel.Bounds.X, Tolerance);
            Assert.Equal(0, panel.Margin.Bottom);

            // Колонки смыкаются, таблица кончается вместе с последней.
            Assert.Equal(Cell(panel, 0, 0).Bounds.Right, Cell(panel, 0, 1).Bounds.X, Tolerance);
            Assert.Equal(panel.Bounds.Width, Cell(panel, 0, 1).Bounds.Right, Tolerance);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task EachColumnOfATableThatFitsIsAsWideAsItsWidestCell()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(Document(Table(
                ["#", "Name"],
                ["1", "A somewhat longer name"])));
            var window = Show(view);
            var (_, panel) = Table(view);

            for (var column = 0; column < 2; column++)
            {
                var natural = Math.Max(Cell(panel, 0, column).DesiredSize.Width, Cell(panel, 1, column).DesiredSize.Width);
                Assert.Equal(natural, Cell(panel, 1, column).Bounds.Width, 1d);
            }

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task RowsLineUpAcrossColumns()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(WideTable());
            var window = Show(view);
            var (_, panel) = Table(view);

            for (var row = 0; row < RowCount(panel); row++)
            {
                var first = Cell(panel, row, 0).Bounds;
                for (var column = 1; column < panel.ColumnCount; column++)
                {
                    var cell = Cell(panel, row, column).Bounds;
                    Assert.Equal(first.Y, cell.Y, Tolerance);
                    Assert.Equal(first.Height, cell.Height, Tolerance);
                }
            }

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task ColumnAlignmentAppliesToTheHeaderAndTheCells()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(AlignedTable());
            var (_, panel) = Table(view);

            TextAlignment[] expected = [TextAlignment.Left, TextAlignment.Center, TextAlignment.Right, TextAlignment.Left];
            for (var row = 0; row < RowCount(panel); row++)
            {
                for (var column = 0; column < panel.ColumnCount; column++)
                {
                    Assert.Equal(expected[column], Content(panel, row, column).LayoutTextAlignment);
                }
            }
        }, CancellationToken.None);
    }

    [Fact]
    public Task TextOfARightAlignedCellEndsAtTheRightEdgeOfTheCell()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(AlignedTable());
            var window = Show(view);
            var (_, panel) = Table(view);

            // Заголовок шире ячейки, поэтому текст ячейки стоит правее левого края.
            var cell = Content(panel, 1, 2);
            var middle = cell.Bounds.Height / 2;

            Assert.Equal(cell.DocumentRange.End, cell.GetDocumentOffset(new Point(cell.Bounds.Width - 1, middle)));
            Assert.Equal(cell.DocumentRange.Start, cell.GetDocumentOffset(new Point(1, middle)));

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task DraggingInACentredCellSelectsItsText()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(AlignedTable());
            var window = Show(view);
            var (_, panel) = Table(view);

            var cell = Content(panel, 1, 1);
            var middle = cell.Bounds.Height / 2;
            window.MouseDown(cell.TranslatePoint(new Point(1, middle), window)!.Value, MouseButton.Left);
            window.MouseMove(cell.TranslatePoint(new Point(cell.Bounds.Width - 1, middle), window)!.Value);
            window.MouseUp(cell.TranslatePoint(new Point(cell.Bounds.Width - 1, middle), window)!.Value, MouseButton.Left);

            Assert.Equal("mid", view.SelectedText);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task SelectionWorksInAScrolledTable()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(WideTable());
            var window = Show(view);
            var (scrollViewer, panel) = Table(view);

            scrollViewer.Offset = new Vector(scrollViewer.Extent.Width - scrollViewer.Viewport.Width, 0);
            window.UpdateLayout();

            // Конец длинной ячейки виден только после прокрутки.
            var cell = Content(panel, 1, 2);
            var middle = cell.Bounds.Height / 2;
            var start = cell.TranslatePoint(new Point(cell.Bounds.Width - 1, middle), window)!.Value;
            var end = cell.TranslatePoint(new Point(cell.Bounds.Width - 120, middle), window)!.Value;
            Assert.InRange(start.X, 0, window.Width);
            Assert.InRange(end.X, 0, window.Width);

            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(end);
            window.MouseUp(end, MouseButton.Left);

            var selected = view.SelectedText;
            Assert.NotNull(selected);
            Assert.True(selected.Length > 3, $"selected '{selected}'");
            Assert.EndsWith(selected, LongText, StringComparison.Ordinal);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task SelectAllCopiesTableCellsSeparatedByTabs()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(Document(Table(["A", "B"], ["1", "2"])));

            view.SelectAll();

            Assert.Equal("A\tB\n1\t2", view.SelectedText?.TrimEnd('\n'));
        }, CancellationToken.None);
    }

    [Fact]
    public Task ChangingColumnAlignmentInTheSourceRebuildsTheTable()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(AlignedTable());
            var (_, before) = Table(view);
            var table = Assert.IsType<MarkdownTableBlock>(Assert.Single(AlignedTable().Blocks));

            view.Document = Document(table with { ColumnAlignments = [MarkdownTableColumnAlignment.Right] });

            var (_, after) = Table(view);
            Assert.NotSame(before, after);
            Assert.Equal(TextAlignment.Right, Content(after, 1, 0).LayoutTextAlignment);
        }, CancellationToken.None);
    }

    [Fact]
    public Task WideTableUsesThePageMarginsAndStartsAtTheReadingColumn()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(WideTable());
            var (window, page) = ShowOnPage(view);
            var (scrollViewer, panel) = Table(view);
            var column = ReadingColumn(view, page);

            // Прокрутка таблицы — во всю страницу за вычетом отступа от краёв.
            Assert.Equal(MarkdownTableHost.PageEdgeInset, Left(scrollViewer, page), Tolerance);
            Assert.Equal(page.Viewport.Width - MarkdownTableHost.PageEdgeInset, Right(scrollViewer, page), Tolerance);
            Assert.True(scrollViewer.Extent.Width > scrollViewer.Viewport.Width + 1);

            // В покое таблица начинается от колонки чтения и уходит в правое поле.
            Assert.Equal(column.Left, Left(Cell(panel, 0, 0), page), Tolerance);
            Assert.True(Right(Cell(panel, 1, 1), page) > column.Left);
            Assert.True(Right(Cell(panel, 1, 2), page) > column.Right);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task ScrolledWideTableMovesIntoTheLeftMarginAndEndsAtThePageEdge()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(WideTable());
            var (window, page) = ShowOnPage(view);
            var (scrollViewer, panel) = Table(view);

            scrollViewer.Offset = new Vector(scrollViewer.Extent.Width - scrollViewer.Viewport.Width, 0);
            window.UpdateLayout();

            Assert.Equal(page.Viewport.Width - MarkdownTableHost.PageEdgeInset, Right(Cell(panel, 1, 2), page), Tolerance);
            Assert.True(Left(Cell(panel, 1, 2), page) < MarkdownTableHost.PageEdgeInset);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task TableThatFitsStaysInTheReadingColumnOnAPage()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(Document(Table(
                ["Setting", "Value"],
                ["Theme", "Dark"])));
            var (window, page) = ShowOnPage(view);
            var (scrollViewer, panel) = Table(view);
            var column = ReadingColumn(view, page);

            Assert.Equal(column.Left, Left(Cell(panel, 0, 0), page), Tolerance);
            Assert.True(Right(Cell(panel, 0, 1), page) < column.Right - 100);
            Assert.True(scrollViewer.Extent.Width <= scrollViewer.Viewport.Width + Tolerance, "a table that fits should not scroll");
            Assert.Equal(0, panel.Margin.Bottom);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task WideTableStopsShortOfTheReservedEndOfThePage()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(WideTable());
            var (window, page) = ShowOnPage(view);
            var (scrollViewer, _) = Table(view);

            const double minimap = 150;
            MarkdownTableHost.SetPageEndReserve(page, minimap);
            window.UpdateLayout();
            window.UpdateLayout();

            Assert.Equal(
                page.Viewport.Width - MarkdownTableHost.PageEdgeInset - minimap,
                Right(scrollViewer, page),
                Tolerance);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task SelectionWorksInThePageMarginOutsideTheDocumentView()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(WideTable());
            var (window, page) = ShowOnPage(view);
            var (scrollViewer, panel) = Table(view);

            scrollViewer.Offset = new Vector(scrollViewer.Extent.Width - scrollViewer.Viewport.Width, 0);
            window.UpdateLayout();

            // Середина длинной ячейки теперь в левом поле — левее самого вида.
            var cell = Content(panel, 1, 2);
            var y = cell.TranslatePoint(new Point(0, cell.Bounds.Height / 2), page)!.Value.Y;
            var start = new Point(MarkdownTableHost.PageEdgeInset + 20, y);
            var end = new Point(Left(view, page) - 20, y);
            Assert.True(end.X > start.X + 50);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(end);
            window.MouseUp(end, MouseButton.Left);

            var selected = view.SelectedText;
            Assert.NotNull(selected);
            Assert.True(selected.Length > 3, $"selected '{selected}'");
            Assert.Contains(selected, LongText, StringComparison.Ordinal);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task TableThatFitsDoesNotFade()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(Document(Table(["Setting", "Value"], ["Theme", "Dark"])));
            var (window, _) = ShowOnPage(view);
            var host = Host(view);

            Assert.Equal(0, host.LeftFade);
            Assert.Equal(0, host.RightFade);
            Assert.Null(host.ScrollViewer.Presenter!.OpacityMask);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task WideTableFadesOnlyAtTheEdgeItContinuesBeyond()
    {
        return _fixture.Session.Dispatch(() =>
        {
            // На середине прокрутки таблица должна уходить за оба края дальше, чем
            // на ширину затухания, — крупный текст делает её для этого достаточно широкой.
            var view = new MarkdownDocumentView
            {
                ReadingPreferences = ReadingPreferences.Default with { FontSize = 18 },
                Document = WideTable()
            };
            var (window, _) = ShowOnPage(view);
            var host = Host(view);
            var scrollViewer = host.ScrollViewer;
            var maxOffset = scrollViewer.Extent.Width - scrollViewer.Viewport.Width;

            // В покое таблица продолжается только справа.
            Assert.Equal(0, host.LeftFade);
            Assert.Equal(MarkdownTableHost.EdgeFadeWidth, host.RightFade);
            var mask = Assert.IsType<LinearGradientBrush>(scrollViewer.Presenter!.OpacityMask);
            Assert.Equal(255, mask.GradientStops[0].Color.A);
            Assert.Equal(0, mask.GradientStops[^1].Color.A);

            scrollViewer.Offset = new Vector(maxOffset / 2, 0);
            window.UpdateLayout();
            Assert.Equal(MarkdownTableHost.EdgeFadeWidth, host.LeftFade);
            Assert.Equal(MarkdownTableHost.EdgeFadeWidth, host.RightFade);

            scrollViewer.Offset = new Vector(maxOffset, 0);
            window.UpdateLayout();
            Assert.Equal(MarkdownTableHost.EdgeFadeWidth, host.LeftFade);
            Assert.Equal(0, host.RightFade);
            mask = Assert.IsType<LinearGradientBrush>(scrollViewer.Presenter.OpacityMask);
            Assert.Equal(0, mask.GradientStops[0].Color.A);
            Assert.Equal(255, mask.GradientStops[^1].Color.A);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task FadeNarrowsWithTheLastHiddenPixels()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(WideTable());
            var (window, _) = ShowOnPage(view);
            var host = Host(view);
            var scrollViewer = host.ScrollViewer;

            scrollViewer.Offset = new Vector(scrollViewer.Extent.Width - scrollViewer.Viewport.Width - 10, 0);
            window.UpdateLayout();

            Assert.Equal(10, host.RightFade, Tolerance);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task TableAtTheStartOfTheLeftMarginDoesNotFadeOnTheLeft()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(WideTable());
            var (window, page) = ShowOnPage(view);
            var host = Host(view);
            var scrollViewer = host.ScrollViewer;

            // Сдвинута в левое поле, но её левый край ещё виден: скрытого нет.
            scrollViewer.Offset = new Vector(Left(host, page) - MarkdownTableHost.PageEdgeInset - 20, 0);
            window.UpdateLayout();

            Assert.Equal(0, host.LeftFade);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task ScreenshotsInATwoColumnTableShareTheReadingColumn()
    {
        // Обычный для README случай: | Light | Dark | и два скриншота 1920×1080.
        return _fixture.RunAsync(async () =>
        {
            var view = CreateView(Document(new MarkdownTableBlock(
                Row("Light", "Dark"),
                [[Image("light.svg"), Image("dark.svg")]])));
            view.ImageSourceResolver = new SvgImageSourceResolver(1920, 1080);
            var window = Show(view);
            var (scrollViewer, panel) = Table(view);

            await WaitUntilAsync(() =>
            {
                window.UpdateLayout();
                var image = Assert.IsType<MarkdownImageFlowFragment>(Cell(panel, 1, 0).Child);
                return image.Bounds.Height is > 1 && image.Bounds.Height < PlaceholderImageHeight(image) - 1;
            });

            // Обе картинки ужаты до половины колонки и стоят рядом, без прокрутки.
            Assert.Equal(scrollViewer.Viewport.Width, panel.Bounds.Width, Tolerance);
            Assert.True(scrollViewer.Extent.Width <= scrollViewer.Viewport.Width + Tolerance);
            Assert.Equal(Cell(panel, 1, 0).Bounds.Width, Cell(panel, 1, 1).Bounds.Width, 1);

            var image = Cell(panel, 1, 0).Child!;
            Assert.True(image.Bounds.Width <= Cell(panel, 1, 0).Bounds.Width);
            // Раскладка округляет высоту вверх до пикселя.
            Assert.Equal(image.Bounds.Width * 1080 / 1920, image.Bounds.Height, 1d);

            window.Close();
        });
    }

    [Fact]
    public void ColumnsWithImagesShrinkToFitAndTextColumnsKeepTheirWidth()
    {
        // Текст 100, картинки 1000 и 600 в колонке 800: текст целиком, картинки
        // делят остаток в пропорции своего запаса над минимумом.
        var widths = MarkdownTablePanel.ComputeColumnWidths([100, 0, 0], [0, 1000, 600], 800);

        Assert.Equal(100, widths[0]);
        Assert.Equal(700, widths[1] + widths[2], 3);
        Assert.True(widths[1] > widths[2]);
        Assert.Equal(
            (800d - MarkdownTablePanel.ShrinkableColumnMinWidth) / (600 - MarkdownTablePanel.ShrinkableColumnMinWidth),
            (widths[1] - MarkdownTablePanel.ShrinkableColumnMinWidth) / (widths[2] - MarkdownTablePanel.ShrinkableColumnMinWidth),
            3);
    }

    [Fact]
    public void ImagesStopShrinkingAtTheMinimumAndTheTableScrolls()
    {
        var widths = MarkdownTablePanel.ComputeColumnWidths([900, 0], [0, 1000], 800);

        Assert.Equal([900, MarkdownTablePanel.ShrinkableColumnMinWidth], widths);
    }

    [Fact]
    public void ImageNarrowerThanItsTextColumnDoesNotChangeTheWidth()
    {
        var widths = MarkdownTablePanel.ComputeColumnWidths([300, 50], [200, 0], 800);

        Assert.Equal([300, 50], widths);
    }

    [Fact]
    public Task SearchScrollsAWideTableToAMatchOffToTheRight()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(WideTable());
            var (window, _) = ShowOnPage(view);
            var (scrollViewer, panel) = Table(view);
            Assert.Equal(0, scrollViewer.Offset.X);

            view.ApplySearchQuery("connections it serves");
            window.UpdateLayout();

            Assert.Equal(1, view.MatchCount);
            Assert.True(scrollViewer.Offset.X > 0);
            var cell = Content(panel, 1, 2);
            var right = cell.TranslatePoint(new Point(cell.Bounds.Width, 0), scrollViewer)!.Value.X;
            Assert.InRange(right, 0, scrollViewer.Viewport.Width);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task SearchScrollsBackToAMatchOffToTheLeft()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(WideTable());
            var (window, _) = ShowOnPage(view);
            var (scrollViewer, panel) = Table(view);
            var maxOffset = scrollViewer.ScrollBarMaximum.X;
            scrollViewer.Offset = new Vector(maxOffset, 0);
            window.UpdateLayout();

            view.ApplySearchQuery("vCPU");
            window.UpdateLayout();

            Assert.True(scrollViewer.Offset.X < maxOffset);
            var cell = Content(panel, 1, 1);
            Assert.InRange(cell.TranslatePoint(default, scrollViewer)!.Value.X, 0, scrollViewer.Viewport.Width);
            Assert.InRange(
                cell.TranslatePoint(new Point(cell.Bounds.Width, 0), scrollViewer)!.Value.X,
                0,
                scrollViewer.Viewport.Width);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task SearchScrollsALongCodeLineToTheMatch()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(new RenderedMarkdownDocument(
            [
                new MarkdownCodeBlock(null, "let first = 1; " + string.Concat(Enumerable.Repeat("padding ", 60)) + "needle")
            ]));
            var (window, _) = ShowOnPage(view);
            var codeScrollViewer = view.GetVisualDescendants().OfType<ScrollViewer>().Single();

            view.ApplySearchQuery("needle");
            window.UpdateLayout();

            Assert.True(codeScrollViewer.Offset.X > 0);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task FadeMaskFollowsTheViewportWidthAfterAResize()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(WideTable());
            var (window, _) = ShowOnPage(view);
            var host = Host(view);
            var presenter = host.ScrollViewer.Presenter!;

            window.Width = 1600;
            window.UpdateLayout();
            window.UpdateLayout();

            var mask = Assert.IsType<LinearGradientBrush>(presenter.OpacityMask);
            Assert.Equal(MarkdownTableHost.EdgeFadeWidth, host.RightFade);
            Assert.Equal(
                1 - MarkdownTableHost.EdgeFadeWidth / host.ScrollViewer.Viewport.Width,
                mask.GradientStops[2].Offset,
                4);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task CellsUseTheTextSizeAndTheNotionCellPadding()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(Document(Table(["Setting", "Value"], ["Theme", "Dark"])));
            var window = Show(view);
            var (_, panel) = Table(view);

            foreach (var (row, column) in new[] { (0, 0), (1, 1) })
            {
                var content = Content(panel, row, column);
                Assert.Equal(14, content.BaseFontSize, 3);
                Assert.Equal(21, content.BaseLineHeight, 3);
                Assert.Equal(TextWrapping.NoWrap, content.LayoutTextWrapping);
                Assert.Equal(new Thickness(9, 7), Cell(panel, row, column).Padding);
            }

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task CellPaddingKeepsItsProportionAtALargerTextSize()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(Document(Table(["Setting", "Value"], ["Theme", "Dark"])));
            view.ReadingPreferences = ReadingPreferences.Default with { FontSize = 18 };
            var window = Show(view);
            var (_, panel) = Table(view);

            Assert.Equal(new Thickness(12, 9), Cell(panel, 1, 0).Padding);
            Assert.Equal(27, Content(panel, 1, 0).BaseLineHeight, 3);

            window.Close();
        }, CancellationToken.None);
    }

    /// <summary>Таблица набрана шрифтом документа: смена Sans на Mono или Serif меняет и её.</summary>
    [Theory]
    [InlineData(FontFamilyMode.Sans, "MmDocumentSansFontFamily")]
    [InlineData(FontFamilyMode.Serif, "MmDocumentSerifFontFamily")]
    [InlineData(FontFamilyMode.Mono, "MmDocumentMonoFontFamily")]
    public Task CellsUseTheDocumentFont(FontFamilyMode mode, string resourceKey)
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(Document(Table(["Setting", "Value"], ["Theme", "Dark"])));
            view.ReadingPreferences = ReadingPreferences.Default with { FontFamily = mode };
            var window = Show(view);
            var (_, panel) = Table(view);

            Assert.True(window.TryFindResource(resourceKey, out var expected));
            Assert.Equal(expected, Content(panel, 0, 0).BaseFontFamily);
            Assert.Equal(expected, Content(panel, 1, 1).BaseFontFamily);

            window.Close();
        }, CancellationToken.None);
    }

    /// <summary>Шапка — тихая: тот же кегль и цвет текста, 600, без разрядки.</summary>
    [Fact]
    public Task TheHeaderIsSemiboldTextOfTheSameSizeWithoutLetterSpacing()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(Document(Table(["Setting", "Value"], ["Theme", "Dark"])));
            var window = Show(view);
            var (_, panel) = Table(view);

            var header = Content(panel, 0, 0);
            Assert.Equal(FontWeight.SemiBold, header.BaseFontWeight);
            Assert.Equal(0, header.BaseLetterSpacing);
            Assert.Equal(Content(panel, 1, 0).BaseFontSize, header.BaseFontSize);
            Assert.Equal(Color.Parse("#1F1915"), Assert.IsAssignableFrom<ISolidColorBrush>(header.ResolveBaseTextBrush()).Color);
            Assert.Equal(FontWeight.Normal, Content(panel, 1, 0).BaseFontWeight);

            window.Close();
        }, CancellationToken.None);
    }

    /// <summary>
    /// Сетка в 1 px у всех ячеек, без двойных линий между соседними: у ячейки линии
    /// слева и сверху, у последней колонки — ещё справа, у последней строки — снизу.
    /// </summary>
    [Theory]
    [InlineData("Light", "#E2DDD7", "#F4F0EA")]
    [InlineData("Dark", "#3F3935", "#29241F")]
    public Task EveryCellIsInAOnePixelGridUnderAQuietHeader(string theme, string lineColor, string headerColor)
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(Document(Table(["A", "B", "C"], ["1", "2", "3"], ["4", "5", "6"])));
            var window = ThemedTestWindow.Create(theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light, view);
            window.Width = 600;
            window.Height = 400;
            window.Show();
            window.UpdateLayout();
            var (_, panel) = Table(view);

            for (var row = 0; row < 3; row++)
            {
                for (var column = 0; column < 3; column++)
                {
                    var cell = Cell(panel, row, column);
                    Assert.Equal(new Thickness(1, 1, column == 2 ? 1 : 0, row == 2 ? 1 : 0), cell.BorderThickness);
                    Assert.Equal(Color.Parse(lineColor), Assert.IsAssignableFrom<ISolidColorBrush>(cell.BorderBrush).Color);

                    if (row == 0)
                    {
                        Assert.Equal(Color.Parse(headerColor), Assert.IsAssignableFrom<ISolidColorBrush>(cell.Background).Color);
                    }
                    else
                    {
                        Assert.Null(cell.Background);
                    }
                }
            }

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task ATableWithoutRowsClosesTheGridUnderTheHeader()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(Document(new MarkdownTableBlock(Row("A", "B"), [])));
            var window = Show(view);
            var (_, panel) = Table(view);

            Assert.Equal(new Thickness(1, 1, 0, 1), Cell(panel, 0, 0).BorderThickness);
            Assert.Equal(new Thickness(1), Cell(panel, 0, 1).BorderThickness);

            window.Close();
        }, CancellationToken.None);
    }

    private static RenderedMarkdownDocument WideTable()
        => Document(Table(
            ["#", "CPU", "Rationale"],
            ["1", "2 vCPU", LongText],
            ["3", "8 vCPU", "Mirror of the first node."]));

    private static RenderedMarkdownDocument AlignedTable()
        => Document(new MarkdownTableBlock(
            Row("Left", "Centred column", "Right aligned column", "Plain"),
            [Row("left", "mid", "alpha beta", "plain")],
            [MarkdownTableColumnAlignment.Left, MarkdownTableColumnAlignment.Center, MarkdownTableColumnAlignment.Right]));

    private static MarkdownTableBlock Table(string[] header, params string[][] rows)
        => new(Row(header), rows.Select(Row).ToArray());

    private static MarkdownTableCell[] Row(params string[] cells)
        => cells.Select(static text => new MarkdownTableCell([new MarkdownTextInline(text)])).ToArray();

    private static RenderedMarkdownDocument Document(MarkdownTableBlock table) => new([table]);

    private static MarkdownDocumentView CreateView(RenderedMarkdownDocument document)
        => new()
        {
            ReadingPreferences = ReadingPreferences.Default,
            Document = document
        };

    /// <summary>
    /// Окно с темой: прокрутку таблицы делает шаблон Fluent у <see cref="ScrollViewer"/>,
    /// отступы ячеек задаёт Controls.axaml.
    /// </summary>
    private static Window Show(MarkdownDocumentView view)
    {
        var window = ThemedTestWindow.Create(ThemeVariant.Light, view);
        window.Width = 600;
        window.Height = 400;
        window.Show();
        window.UpdateLayout();
        return window;
    }

    /// <summary>
    /// Документ на странице, как в ViewerView: прокручиваемая область во всё окно,
    /// колонка чтения по центру с отступами документа.
    /// </summary>
    private static (Window Window, ScrollViewer Page) ShowOnPage(MarkdownDocumentView view)
    {
        view.DocumentPadding = new Thickness(72, 24, 72, 24);
        var page = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new Grid
            {
                Children =
                {
                    new Border
                    {
                        MaxWidth = 500,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Child = view
                    }
                }
            }
        };

        var window = ThemedTestWindow.Create(ThemeVariant.Light, page);
        window.Width = 1200;
        window.Height = 600;
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return (window, page);
    }

    /// <summary>Края колонки чтения в координатах страницы.</summary>
    private static (double Left, double Right) ReadingColumn(MarkdownDocumentView view, Visual page)
        => (Left(view, page) + view.DocumentPadding.Left, Right(view, page) - view.DocumentPadding.Right);

    private static double Left(Visual control, Visual relativeTo)
        => control.TranslatePoint(default, relativeTo)!.Value.X;

    private static double Right(Visual control, Visual relativeTo)
        => control.TranslatePoint(new Point(control.Bounds.Width, 0), relativeTo)!.Value.X;

    /// <summary>Высота заглушки одиночной картинки, пока она не загрузилась: 180 px при тексте 14.</summary>
    private static double PlaceholderImageHeight(MarkdownImageFlowFragment image) => image.BaseFontSize * 180 / 14;

    private static MarkdownTableCell Image(string url) => new([new MarkdownImageInline(url, url, null)]);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(25);
        }
    }

    private sealed class SvgImageSourceResolver(int width, int height) : IImageSourceResolver
    {
        public Task<Stream?> TryOpenAsync(string url, string? baseDirectory, CancellationToken cancellationToken)
            => Task.FromResult<Stream?>(new MemoryStream(Encoding.UTF8.GetBytes(
                $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\"><rect width=\"{width}\" height=\"{height}\" fill=\"#888\"/></svg>")));
    }

    private static MarkdownTableHost Host(MarkdownDocumentView view)
    {
        var (scrollViewer, _) = Table(view);
        return Assert.IsType<MarkdownTableHost>(scrollViewer.Parent);
    }

    private static (ScrollViewer ScrollViewer, MarkdownTablePanel Panel) Table(MarkdownDocumentView view)
    {
        var viewport = Assert.IsType<Border>(view.Content);
        var root = Assert.IsType<StackPanel>(viewport.Child);
        var table = Assert.IsType<Border>(Assert.Single(root.Children));
        Assert.Contains("mm-md-table", table.Classes);
        var host = Assert.IsType<MarkdownTableHost>(table.Child);
        Assert.Same(host.ScrollViewer, host.Child);
        Assert.Same(host.Panel, host.ScrollViewer.Content);
        return (host.ScrollViewer, host.Panel);
    }

    private static int RowCount(MarkdownTablePanel panel) => panel.Children.Count / panel.ColumnCount;

    private static Border Cell(MarkdownTablePanel panel, int row, int column)
        => Assert.IsType<Border>(panel.Children[row * panel.ColumnCount + column]);

    private static MarkdownSelectionTextFragment Content(MarkdownTablePanel panel, int row, int column)
        => Assert.IsType<MarkdownSelectionTextFragment>(Cell(panel, row, column).Child);
}
