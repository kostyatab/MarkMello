using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using MarkMello.Domain;
using MarkMello.Presentation.Views;
using MarkMello.Presentation.Views.Markdown;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Пункт task list рисуется с чекбоксом вместо «•»: только для чтения, на первой
/// строке пункта, в цветах темы, и как часть текста документа — для выделения,
/// поиска и копирования (ADR-0001). Текст всех пунктов списка начинается с одной
/// вертикали, какой бы маркер ни стоял перед ним.
/// </summary>
[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class MarkdownTaskListLayoutTests
{
    private const double Tolerance = 0.5;

    private readonly AvaloniaHeadlessFixture _fixture;

    public MarkdownTaskListLayoutTests(AvaloniaHeadlessFixture fixture) => _fixture = fixture;

    [Fact]
    public Task TaskItemsShowACheckboxAndRegularItemsKeepTheirBullet()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var list = TopLevelList(CreateView(TaskListDocument(isOrdered: false)));

            Assert.True(Assert.IsType<MarkdownTaskCheckboxFragment>(Marker(list, 0)).IsChecked);
            Assert.False(Assert.IsType<MarkdownTaskCheckboxFragment>(Marker(list, 1)).IsChecked);
            Assert.Equal("• ", Assert.IsType<MarkdownSelectionTextFragment>(Marker(list, 2)).StyledText.Text);
        }, CancellationToken.None);
    }

    [Fact]
    public Task OrderedTaskItemKeepsItsNumberBeforeTheCheckbox()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var list = TopLevelList(CreateView(TaskListDocument(isOrdered: true)));

            Assert.Collection(
                Cells(list, 0),
                number => Assert.Equal("1. ", Assert.IsType<MarkdownSelectionTextFragment>(number).StyledText.Text),
                checkbox => Assert.True(Assert.IsType<MarkdownTaskCheckboxFragment>(checkbox).IsChecked),
                content => Assert.IsType<StackPanel>(content));
            Assert.Collection(
                Cells(list, 2),
                number => Assert.Equal("3. ", Assert.IsType<MarkdownSelectionTextFragment>(number).StyledText.Text),
                content => Assert.IsType<StackPanel>(content));
        }, CancellationToken.None);
    }

    [Fact]
    public Task NestedTaskItemGetsItsOwnCheckbox()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var list = TopLevelList(CreateView(new RenderedMarkdownDocument(
            [
                new MarkdownListBlock(false,
                [
                    new MarkdownListItem(
                    [
                        Paragraph("Parent"),
                        new MarkdownListBlock(false, [new MarkdownListItem([Paragraph("Child")], IsChecked: true)])
                    ],
                    IsChecked: false)
                ])
            ])));

            Assert.False(Assert.IsType<MarkdownTaskCheckboxFragment>(Marker(list, 0)).IsChecked);

            var nested = Assert.IsType<Grid>(Content(list, 0).Children[1]);
            Assert.True(Assert.IsType<MarkdownTaskCheckboxFragment>(Marker(nested, 0)).IsChecked);
        }, CancellationToken.None);
    }

    [Fact]
    public Task TextOfEveryItemStartsAtTheSameEdgeWhateverTheMarker()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(TaskListDocument(isOrdered: false));
            var window = Show(view);
            var list = TopLevelList(view);

            var textLeft = Left(Content(list, 0), list);
            Assert.Equal(textLeft, Left(Content(list, 1), list), Tolerance);
            Assert.Equal(textLeft, Left(Content(list, 2), list), Tolerance);

            // «•» и чекбоксы прижаты к правому краю одной колонки: пробел после
            // «•» ставит точку под центр чекбокса (сама ширина зависит от шрифта).
            Assert.Equal(Right(Marker(list, 0), list), Right(Marker(list, 2), list), Tolerance);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task OrderedListAlignsNumbersRightAndStartsRegularTextAtTheCheckboxes()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(TaskListDocument(isOrdered: true));
            var window = Show(view);
            var list = TopLevelList(view);

            Assert.Equal(Right(Cells(list, 0)[0], list), Right(Cells(list, 2)[0], list), Tolerance);
            Assert.Equal(Left(Content(list, 0), list), Left(Content(list, 1), list), Tolerance);
            Assert.Equal(Left(Cells(list, 0)[1], list), Left(Content(list, 2), list), Tolerance);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task OrderedListWithoutTasksHasNoCheckboxColumn()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var list = TopLevelList(CreateView(new RenderedMarkdownDocument(
            [
                new MarkdownListBlock(true, [new MarkdownListItem([Paragraph("One")]), new MarkdownListItem([Paragraph("Two")])])
            ])));

            Assert.Equal(2, list.ColumnDefinitions.Count);
        }, CancellationToken.None);
    }

    [Fact]
    public Task TaskListIsTighterThanAListWithoutCheckboxes()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var plain = TopLevelList(CreateView(new RenderedMarkdownDocument(
            [
                new MarkdownListBlock(false, [new MarkdownListItem([Paragraph("Plain")])])
            ])));
            var tasks = TopLevelList(CreateView(TaskListDocument(isOrdered: false)));

            // Обычные списки не меняются, а чекбокс, который шире «•», стоит к
            // тексту ближе, чтобы пункт не распадался на иконку и текст.
            Assert.Equal(12, Content(plain, 0).Margin.Left);
            Assert.All(
                Enumerable.Range(0, 3),
                item => Assert.True(Content(tasks, item).Margin.Left < Content(plain, 0).Margin.Left));
        }, CancellationToken.None);
    }

    [Fact]
    public Task CheckboxTakesTheFirstLineOfTheItem()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var preferences = ReadingPreferences.Default;
            var view = CreateView(new RenderedMarkdownDocument(
            [
                new MarkdownListBlock(false,
                [
                    new MarkdownListItem(
                    [
                        Paragraph("A long open task item that wraps onto a second line so the item is taller than its checkbox")
                    ],
                    IsChecked: false)
                ])
            ]));
            var window = new Window { Width = 320, Height = 400, Content = view };
            window.Show();
            window.UpdateLayout();

            var list = TopLevelList(view);
            var checkbox = Assert.IsType<MarkdownTaskCheckboxFragment>(Marker(list, 0));
            var text = Assert.IsType<MarkdownSelectionTextFragment>(Assert.Single(Content(list, 0).Children));
            var lineHeight = preferences.FontSize * preferences.LineHeight;

            Assert.True(text.Bounds.Height > 1.5 * lineHeight, "the item text should wrap");
            // Раскладка округляет высоту вверх до целого пикселя.
            Assert.Equal(Math.Ceiling(lineHeight), checkbox.Bounds.Height, Tolerance);
            Assert.Equal(text.TranslatePoint(default, list)!.Value.Y, checkbox.TranslatePoint(default, list)!.Value.Y, Tolerance);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task ClickingTheCheckboxDoesNotToggleIt()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var document = TaskListDocument(isOrdered: false);
            var view = CreateView(document);
            var window = Show(view);

            var checkbox = Assert.IsType<MarkdownTaskCheckboxFragment>(Marker(TopLevelList(view), 1));
            var centre = checkbox.TranslatePoint(new Point(checkbox.Bounds.Width / 2, checkbox.Bounds.Height / 2), window)!.Value;

            window.MouseDown(centre, MouseButton.Left);
            window.MouseUp(centre, MouseButton.Left);

            // Не кнопка и не фокус: клик — обычное начало выделения, которое
            // без протягивания ничего не выделяет.
            Assert.False(checkbox.Focusable);
            Assert.False(checkbox.IsChecked);
            Assert.False(view.HasSelection);
            Assert.Same(document, view.Document);
            Assert.Same(checkbox, Marker(TopLevelList(view), 1));

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task DraggingFromTheCheckboxSelectsItWithTheItemText()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(TaskListDocument(isOrdered: false));
            var window = Show(view);

            var list = TopLevelList(view);
            var checkbox = Assert.IsType<MarkdownTaskCheckboxFragment>(Marker(list, 1));
            var text = Assert.Single(Content(list, 1).Children);
            var start = checkbox.TranslatePoint(new Point(1, checkbox.Bounds.Height / 2), window)!.Value;
            var end = text.TranslatePoint(new Point(text.Bounds.Width - 1, text.Bounds.Height / 2), window)!.Value;

            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(end);
            window.MouseUp(end, MouseButton.Left);

            Assert.Equal("☐ Open", view.SelectedText);
            Assert.False(checkbox.SelectionRange.Intersection(checkbox.DocumentRange).IsEmpty);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task SelectAllCopiesTheCheckboxStateWithTheItemText()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(TaskListDocument(isOrdered: false));

            view.SelectAll();

            Assert.Equal("☑ Done\n☐ Open\n• Plain", view.SelectedText?.TrimEnd('\n'));
            var list = TopLevelList(view);
            Assert.False(Assert.IsType<MarkdownTaskCheckboxFragment>(Marker(list, 0)).SelectionRange.IsEmpty);
            Assert.False(Assert.IsType<MarkdownTaskCheckboxFragment>(Marker(list, 1)).SelectionRange.IsEmpty);
        }, CancellationToken.None);
    }

    [Fact]
    public Task SearchFindsTheTextOfATaskItem()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(TaskListDocument(isOrdered: false));

            view.ApplySearchQuery("open");

            Assert.Equal(1, view.MatchCount);
            var list = TopLevelList(view);
            var text = Assert.IsType<MarkdownSelectionTextFragment>(Assert.Single(Content(list, 1).Children));
            Assert.Single(text.SearchHighlightRanges);
            Assert.Empty(Assert.IsType<MarkdownTaskCheckboxFragment>(Marker(list, 1)).SearchHighlightRanges);
        }, CancellationToken.None);
    }

    [Fact]
    public Task TogglingTheMarkInTheSourceRebuildsTheList()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(SingleTaskDocument(isChecked: false));
            var before = TopLevelList(view);

            view.Document = SingleTaskDocument(isChecked: true);

            var after = TopLevelList(view);
            Assert.NotSame(before, after);
            Assert.True(Assert.IsType<MarkdownTaskCheckboxFragment>(Marker(after, 0)).IsChecked);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public Task CheckboxIsALucideSquareInThemeColours(string themeName)
    {
        return _fixture.Session.Dispatch(() =>
        {
            var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
            var view = CreateView(TaskListDocument(isOrdered: false));
            var window = ThemedTestWindow.Create(theme, view);
            window.Show();
            window.UpdateLayout();

            var list = TopLevelList(view);
            var done = Assert.IsType<MarkdownTaskCheckboxFragment>(Marker(list, 0));
            var open = Assert.IsType<MarkdownTaskCheckboxFragment>(Marker(list, 1));

            Assert.Equal("LucideSquareCheckGeometry", done.GeometryKey);
            Assert.Equal("LucideSquareGeometry", open.GeometryKey);
            AssertResource<Geometry>(window, done.GeometryKey, theme);
            AssertResource<Geometry>(window, open.GeometryKey, theme);
            Assert.Equal("MmAccentBrush", done.ForegroundKey);
            Assert.Equal("MmTextSoftBrush", open.ForegroundKey);
            AssertResource<IBrush>(window, done.ForegroundKey, theme);
            AssertResource<IBrush>(window, open.ForegroundKey, theme);

            window.Close();
        }, CancellationToken.None);
    }

    private static void AssertResource<T>(Window window, string key, ThemeVariant theme)
    {
        Assert.True(window.TryFindResource(key, theme, out var value), $"{key} is not defined in the theme");
        Assert.IsAssignableFrom<T>(value);
    }

    private static RenderedMarkdownDocument TaskListDocument(bool isOrdered)
        => new(
        [
            new MarkdownListBlock(isOrdered,
            [
                new MarkdownListItem([Paragraph("Done")], IsChecked: true),
                new MarkdownListItem([Paragraph("Open")], IsChecked: false),
                new MarkdownListItem([Paragraph("Plain")])
            ])
        ]);

    private static RenderedMarkdownDocument SingleTaskDocument(bool isChecked)
        => new([new MarkdownListBlock(false, [new MarkdownListItem([Paragraph("Task")], isChecked)])]);

    private static MarkdownParagraphBlock Paragraph(string text) => new([new MarkdownTextInline(text)]);

    private static MarkdownDocumentView CreateView(RenderedMarkdownDocument document)
        => new()
        {
            ReadingPreferences = ReadingPreferences.Default,
            Document = document
        };

    private static Window Show(MarkdownDocumentView view)
    {
        var window = new Window { Width = 600, Height = 400, Content = view };
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static Grid TopLevelList(MarkdownDocumentView view)
    {
        var viewport = Assert.IsType<Border>(view.Content);
        var root = Assert.IsType<StackPanel>(viewport.Child);
        return Assert.IsType<Grid>(Assert.Single(root.Children));
    }

    /// <summary>Контролы пункта в порядке колонок: маркеры, затем текст.</summary>
    private static Control[] Cells(Grid list, int item)
        => list.Children.Where(child => Grid.GetRow(child) == item).OrderBy(Grid.GetColumn).ToArray();

    private static Control Marker(Grid list, int item) => Cells(list, item)[0];

    private static StackPanel Content(Grid list, int item) => Assert.IsType<StackPanel>(Cells(list, item)[^1]);

    private static double Left(Control control, Visual relativeTo)
        => control.TranslatePoint(default, relativeTo)!.Value.X;

    private static double Right(Control control, Visual relativeTo)
        => control.TranslatePoint(new Point(control.Bounds.Width, 0), relativeTo)!.Value.X;
}
