using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
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

    /// <summary>
    /// Чекбокс висит в колонке маркера, а текст задачи начинается там же, где
    /// текст обычного пункта, — в 1.6em + .15em от края списка; от чекбокса до
    /// текста 6 px при 14 (.43em).
    /// </summary>
    [Fact]
    public Task TextOfEveryItemStartsAtTheSameEdgeWhateverTheMarker()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(TaskListDocument(isOrdered: false));
            var window = Show(view);
            var list = TopLevelList(view);

            var textLeft = Left(Content(list, 0), list);
            Assert.Equal(14 * (1.6 + 0.15), textLeft, Tolerance);
            Assert.Equal(textLeft, Left(Content(list, 1), list), Tolerance);
            Assert.Equal(textLeft, Left(Content(list, 2), list), Tolerance);

            Assert.Equal(textLeft - 6, Right(Marker(list, 0), list), Tolerance);
            Assert.Equal(textLeft - 6, Right(Marker(list, 1), list), Tolerance);
            Assert.Equal(14 * 1.6 - 5, Right(Marker(list, 2), list), Tolerance);

            window.Close();
        }, CancellationToken.None);
    }

    /// <summary>
    /// В нумерованном списке чекбокс стоит после номера: 3 px до него и 6 px после
    /// при 14. Текст обычного пункта — там же, где в списке без задач.
    /// </summary>
    [Fact]
    public Task OrderedListPutsTheCheckboxAfterTheNumber()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(TaskListDocument(isOrdered: true));
            var window = Show(view);
            var list = TopLevelList(view);

            var numberRight = Right(Cells(list, 0)[0], list);
            Assert.Equal(14 * 1.6, numberRight, Tolerance);
            Assert.Equal(numberRight, Right(Cells(list, 2)[0], list), Tolerance);

            var checkbox = Cells(list, 0)[1];
            Assert.Equal(numberRight + 14 * 0.15 + 3, Left(checkbox, list), Tolerance);
            Assert.Equal(Right(checkbox, list) + 6, Left(Content(list, 0), list), Tolerance);
            Assert.Equal(Left(Content(list, 0), list), Left(Content(list, 1), list), Tolerance);
            Assert.Equal(numberRight + 14 * 0.15, Left(Content(list, 2), list), Tolerance);

            window.Close();
        }, CancellationToken.None);
    }

    /// <summary>
    /// Выполненная задача — приглушённым цветом целиком, с вложенными пунктами и
    /// их маркерами, без зачёркивания; невыполненная задача внутри выполненной —
    /// снова обычным цветом.
    /// </summary>
    [Fact]
    public Task DoneTaskIsFaintWithoutStrikethroughAndAnOpenTaskInsideItIsNot()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(new RenderedMarkdownDocument(
            [
                new MarkdownListBlock(false,
                [
                    new MarkdownListItem(
                    [
                        Paragraph("Done parent"),
                        new MarkdownListBlock(false,
                        [
                            new MarkdownListItem([Paragraph("Open child")], IsChecked: false),
                            new MarkdownListItem([Paragraph("Plain child")])
                        ])
                    ],
                    IsChecked: true),
                    new MarkdownListItem([Paragraph("Open")], IsChecked: false),
                    new MarkdownListItem([Paragraph("Plain")])
                ])
            ]));
            var window = Show(view);

            var done = Text(view, "Done parent");
            Assert.Equal("MmTextFaintBrush", done.BaseForegroundResourceKey);
            Assert.DoesNotContain(done.StyledText.Spans, static span => span.Style.IsStrikethrough);
            Assert.Null(Text(view, "Open child").BaseForegroundResourceKey);
            Assert.Equal("MmTextFaintBrush", Text(view, "Plain child").BaseForegroundResourceKey);
            Assert.Equal("MmTextFaintBrush", Text(view, "◦ ").BaseForegroundResourceKey);
            Assert.Null(Text(view, "Open").BaseForegroundResourceKey);
            Assert.Null(Text(view, "Plain").BaseForegroundResourceKey);
            Assert.Null(Text(view, "• ").BaseForegroundResourceKey);

            window.Close();
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public Task DoneTaskTakesTheFaintColourOfTheTheme(string themeName)
    {
        return _fixture.Session.Dispatch(() =>
        {
            var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
            var view = CreateView(TaskListDocument(isOrdered: false));
            var window = ThemedTestWindow.Create(theme, view);
            window.Show();
            window.UpdateLayout();

            Assert.True(window.TryFindResource("MmTextFaintBrush", theme, out var faint));
            Assert.Same(faint, Text(view, "Done").ResolveBaseTextBrush());
            Assert.True(window.TryFindResource("MmTextBrush", theme, out var text));
            Assert.Same(text, Text(view, "Open").ResolveBaseTextBrush());

            window.Close();
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

    /// <summary>Окно с темой — с настоящим Inter: размеры сверяются с макетом.</summary>
    private static Window Show(MarkdownDocumentView view)
    {
        var window = ThemedTestWindow.Create(ThemeVariant.Light, view);
        window.Width = 600;
        window.Height = 400;
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

    private static MarkdownSelectionTextFragment Text(MarkdownDocumentView view, string text)
        => Assert.Single(
            view.GetVisualDescendants().OfType<MarkdownSelectionTextFragment>(),
            fragment => fragment.StyledText.Text == text);

    private static double Left(Control control, Visual relativeTo)
        => control.TranslatePoint(default, relativeTo)!.Value.X;

    private static double Right(Control control, Visual relativeTo)
        => control.TranslatePoint(new Point(control.Bounds.Width, 0), relativeTo)!.Value.X;
}
