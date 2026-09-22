using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using MarkMello.Domain;
using MarkMello.Presentation.Views;
using MarkMello.Presentation.Views.Markdown;

namespace MarkMello.Presentation.Tests;

[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class MarkdownListLayoutTests
{
    private const double Tolerance = 0.5;

    // Дробные просветы в em раскладка округляет до пикселя.
    private const double PixelTolerance = 1;

    private readonly AvaloniaHeadlessFixture _fixture;

    public MarkdownListLayoutTests(AvaloniaHeadlessFixture fixture) => _fixture = fixture;

    [Fact]
    public void ListMarkerHasNoArtificialTopOffset()
    {
        var document = new RenderedMarkdownDocument(
        [
            new MarkdownListBlock(true,
            [
                new MarkdownListItem(
                [
                    new MarkdownParagraphBlock(
                    [
                        new MarkdownStrongInline([new MarkdownTextInline("Viewer first.")]),
                        new MarkdownTextInline(" Reading comes before editing, always.")
                    ])
                ])
            ])
        ]);

        var view = new MarkdownDocumentView
        {
            Document = document,
            ReadingPreferences = ReadingPreferences.Default
        };

        var viewport = Assert.IsType<Border>(view.Content);
        var root = Assert.IsType<StackPanel>(viewport.Child);
        var list = Assert.IsType<Grid>(Assert.Single(root.Children));
        var marker = Assert.IsType<MarkdownSelectionTextFragment>(list.Children[0]);

        Assert.Equal(0, marker.Margin.Top);
    }

    [Fact]
    public Task OrderedListIsNumberedFromItsStartNumber()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var list = TopLevelList(CreateView(OrderedListDocument(startNumber: 7, "Seventh", "Eighth", "Ninth")));

            Assert.Equal(["7. ", "8. ", "9. "], MarkerTexts(list));
        }, CancellationToken.None);
    }

    [Fact]
    public Task NestedOrderedListIsNumberedFromItsOwnStartNumber()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var list = TopLevelList(CreateView(new RenderedMarkdownDocument(
            [
                new MarkdownListBlock(true,
                [
                    new MarkdownListItem(
                    [
                        Paragraph("Parent"),
                        new MarkdownListBlock(true, [new MarkdownListItem([Paragraph("Child")]), new MarkdownListItem([Paragraph("Child")])], StartNumber: 5)
                    ])
                ],
                StartNumber: 3)
            ])));

            var nested = Assert.IsType<Grid>(Content(list, 0).Children[1]);

            // Вложенный список из цифр — второй уровень: буквы от его старта.
            Assert.Equal(["3. "], MarkerTexts(list));
            Assert.Equal(["e. ", "f. "], MarkerTexts(nested));
        }, CancellationToken.None);
    }

    [Fact]
    public Task NumberColumnFitsMultiDigitNumbersWithoutOverlappingText()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(OrderedListDocument(startNumber: 98, "Ninety-eight", "Ninety-nine", "One hundred"));
            var window = new Window { Width = 600, Height = 400, Content = view };
            window.Show();
            window.UpdateLayout();
            var list = TopLevelList(view);

            Assert.Equal(["98. ", "99. ", "100. "], MarkerTexts(list));

            var textLeft = Left(Content(list, 0), list);
            for (var item = 0; item < 3; item++)
            {
                var number = Assert.IsType<MarkdownSelectionTextFragment>(Cells(list, item)[0]);

                // Номер целиком помещается в колонку: не обрезан и не заходит на текст.
                Assert.True(number.Bounds.Width >= number.DesiredSize.Width - Tolerance);
                Assert.True(Right(number, list) <= textLeft + Tolerance);
                Assert.Equal(textLeft, Left(Content(list, item), list), Tolerance);
                Assert.Equal(Right(Cells(list, 0)[0], list), Right(number, list), Tolerance);
            }

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task ChangingStartNumberRenumbersTheShownList()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(OrderedListDocument(startNumber: 1, "One", "Two"));

            view.Document = OrderedListDocument(startNumber: 7, "One", "Two");

            Assert.Equal(["7. ", "8. "], MarkerTexts(TopLevelList(view)));
        }, CancellationToken.None);
    }

    /// <summary>
    /// Между пунктами .25em, между пунктами с абзацами (loose) — .6em; абзацы
    /// внутри пункта — через .5em. Раскладка округляет дробные просветы до
    /// пикселя, поэтому допуск — пиксель.
    /// </summary>
    [Fact]
    public Task ItemsAreSpacedByTheirKindInEm()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(new RenderedMarkdownDocument(
            [
                BulletList(isLoose: false, "Tight one", "Tight two"),
                new MarkdownListBlock(true,
                [
                    new MarkdownListItem([Paragraph("Loose one"), Paragraph("Second paragraph")]),
                    new MarkdownListItem([Paragraph("Loose two")])
                ],
                IsLoose: true)
            ]));
            var window = Show(view);

            Assert.Equal(14 * 0.25, Gap(view, "Tight one", "Tight two"), PixelTolerance);
            Assert.Equal(14 * 0.5, Gap(view, "Loose one", "Second paragraph"), PixelTolerance);
            Assert.Equal(14 * 0.6, Gap(view, "Second paragraph", "Loose two"), PixelTolerance);

            window.Close();
        }, CancellationToken.None);
    }

    /// <summary>
    /// Колонка маркеров — 1.6em, маркер прижат к её правому краю (точка — с
    /// зазором 5 px при 14, как disc в браузере), текст — ещё в .15em. Маркеры —
    /// цветом текста.
    /// </summary>
    [Fact]
    public Task MarkerSitsAtTheRightOfA16EmColumnAndTextFollowsAt015Em()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(new RenderedMarkdownDocument([BulletList(isLoose: false, "One", "Two")]));
            var window = Show(view);
            var list = TopLevelList(view);

            var marker = Assert.IsType<MarkdownSelectionTextFragment>(Cells(list, 0)[0]);
            Assert.Equal(14 * 1.6 - 5, Right(marker, list), Tolerance);
            Assert.Equal(14 * (1.6 + 0.15), Left(Content(list, 0), list), Tolerance);
            Assert.Null(marker.BaseForegroundResourceKey);

            window.Close();
        }, CancellationToken.None);
    }

    /// <summary>
    /// Маркер по уровню среди списков того же вида: • ◦ ▪, 1. a. i.; глубже третий
    /// повторяется, а нумерованный внутри маркированного — всё ещё первый.
    /// </summary>
    [Fact]
    public Task MarkersFollowTheLevelAmongListsOfTheSameKind()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(new RenderedMarkdownDocument(
            [
                Nested(isOrdered: false, depth: 4),
                Nested(isOrdered: true, depth: 4),
                new MarkdownListBlock(false,
                [
                    new MarkdownListItem([Paragraph("Bullet"), new MarkdownListBlock(true, [new MarkdownListItem([Paragraph("Ordered")])])])
                ])
            ]));

            Assert.Equal(["• ", "◦ ", "▪ ", "▪ "], LevelMarkers(view, 0));
            Assert.Equal(["1. ", "a. ", "i. ", "i. "], LevelMarkers(view, 1));
            Assert.Equal(["• ", "1. "], LevelMarkers(view, 2));
        }, CancellationToken.None);
    }

    /// <summary>Вид маркера автора (MM-47) важнее уровня, старт — из маркера первого пункта.</summary>
    [Theory]
    [InlineData(MarkdownListNumbering.LowerAlpha, 3, "c. ", "d. ")]
    [InlineData(MarkdownListNumbering.UpperAlpha, 1, "A. ", "B. ")]
    [InlineData(MarkdownListNumbering.LowerRoman, 4, "iv. ", "v. ")]
    [InlineData(MarkdownListNumbering.UpperRoman, 1, "I. ", "II. ")]
    public Task AuthorsNumberingIsShownFromItsStart(MarkdownListNumbering numbering, int start, string first, string second)
    {
        return _fixture.Session.Dispatch(() =>
        {
            var list = TopLevelList(CreateView(new RenderedMarkdownDocument(
            [
                new MarkdownListBlock(true, [new MarkdownListItem([Paragraph("One")]), new MarkdownListItem([Paragraph("Two")])], start, Numbering: numbering)
            ])));

            Assert.Equal([first, second], MarkerTexts(list));
        }, CancellationToken.None);
    }

    /// <summary>Колонка номеров — по самому длинному номеру: «viii.» не наезжает на текст.</summary>
    [Fact]
    public Task RomanNumberColumnFitsTheLongestNumber()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(new RenderedMarkdownDocument(
            [
                new MarkdownListBlock(true,
                    [new MarkdownListItem([Paragraph("Seventh")]), new MarkdownListItem([Paragraph("Eighth")])],
                    7,
                    Numbering: MarkdownListNumbering.LowerRoman)
            ]));
            var window = Show(view);
            var list = TopLevelList(view);

            Assert.Equal(["vii. ", "viii. "], MarkerTexts(list));
            var longest = Cells(list, 1)[0];
            Assert.True(longest.Bounds.Width > 14 * 1.6, "viii. should be wider than the 1.6em column");
            Assert.True(Right(longest, list) <= Left(Content(list, 1), list) + Tolerance);
            Assert.Equal(Right(Cells(list, 0)[0], list), Right(longest, list), Tolerance);

            window.Close();
        }, CancellationToken.None);
    }

    /// <summary>
    /// После вложенного списка до следующего пункта столько же места, сколько между
    /// пунктами, а после списка до следующего абзаца — сколько между абзацами.
    /// Регрессия: нижние отступы последнего абзаца, вложенного списка и сетки
    /// складывались, и после вложенного списка зазор был вдвое больше обычного.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task NestedListEndsWithoutExtraSpace(bool isLoose)
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(new RenderedMarkdownDocument(
            [
                new MarkdownListBlock(false,
                [
                    new MarkdownListItem(
                    [
                        Paragraph("Parent"),
                        BulletList(isLoose: false, "Child one", "Child two")
                    ]),
                    new MarkdownListItem([Paragraph("Second")]),
                    new MarkdownListItem([Paragraph("Third")])
                ],
                IsLoose: isLoose),
                Paragraph("After"),
                Paragraph("Paragraph")
            ]));
            var window = Show(view);

            var betweenItems = Gap(view, "Second", "Third");
            Assert.Equal(betweenItems, Gap(view, "Child two", "Second"), PixelTolerance);
            Assert.Equal(Gap(view, "After", "Paragraph"), Gap(view, "Third", "After"), PixelTolerance);

            // Вложенный список — через .25em, как пункты tight-списка.
            Assert.Equal(14 * 0.25, Gap(view, "Parent", "Child one"), PixelTolerance);
            Assert.Equal(14 * 0.25, Gap(view, "Child one", "Child two"), PixelTolerance);

            window.Close();
        }, CancellationToken.None);
    }

    private static MarkdownListBlock BulletList(bool isLoose, params string[] items)
        => new(false, [.. items.Select(static text => new MarkdownListItem([Paragraph(text)]))], IsLoose: isLoose);

    /// <summary>Окно с темой — с настоящим Inter: размеры сверяются с макетом.</summary>
    private static Window Show(MarkdownDocumentView view)
    {
        var window = ThemedTestWindow.Create(ThemeVariant.Light, view);
        window.Width = 600;
        window.Height = 800;
        window.Show();
        window.UpdateLayout();
        return window;
    }

    /// <summary>Расстояние от низа одного текста до верха следующего.</summary>
    private static double Gap(MarkdownDocumentView view, string upper, string lower)
    {
        var upperText = Text(view, upper);
        var lowerText = Text(view, lower);
        return lowerText.TranslatePoint(default, view)!.Value.Y
            - upperText.TranslatePoint(new Point(0, upperText.Bounds.Height), view)!.Value.Y;
    }

    private static MarkdownSelectionTextFragment Text(MarkdownDocumentView view, string text)
        => view.GetVisualDescendants().OfType<MarkdownSelectionTextFragment>()
            .Single(fragment => fragment.StyledText.Text == text);

    /// <summary>Маркеры первых пунктов списка и всех вложенных в него по первой ветке.</summary>
    private static string[] LevelMarkers(MarkdownDocumentView view, int blockIndex)
    {
        var viewport = Assert.IsType<Border>(view.Content);
        var root = Assert.IsType<StackPanel>(viewport.Child);
        var markers = new List<string>();
        for (Grid? list = Assert.IsType<Grid>(root.Children[blockIndex]); list is not null;)
        {
            markers.Add(Assert.IsType<MarkdownSelectionTextFragment>(Cells(list, 0)[0]).StyledText.Text);
            list = Content(list, 0).Children.OfType<Grid>().FirstOrDefault();
        }

        return [.. markers];
    }

    private static MarkdownListBlock Nested(bool isOrdered, int depth, int level = 1)
    {
        MarkdownBlock[] blocks = level == depth
            ? [Paragraph($"L{level}")]
            : [Paragraph($"L{level}"), Nested(isOrdered, depth, level + 1)];
        return new MarkdownListBlock(isOrdered, [new MarkdownListItem(blocks)]);
    }

    private static RenderedMarkdownDocument OrderedListDocument(int startNumber, params string[] items)
        => new([new MarkdownListBlock(true, [.. items.Select(static text => new MarkdownListItem([Paragraph(text)]))], startNumber)]);

    private static MarkdownParagraphBlock Paragraph(string text) => new([new MarkdownTextInline(text)]);

    private static MarkdownDocumentView CreateView(RenderedMarkdownDocument document)
        => new()
        {
            ReadingPreferences = ReadingPreferences.Default,
            Document = document
        };

    private static Grid TopLevelList(MarkdownDocumentView view)
    {
        var viewport = Assert.IsType<Border>(view.Content);
        var root = Assert.IsType<StackPanel>(viewport.Child);
        return Assert.IsType<Grid>(Assert.Single(root.Children));
    }

    private static string[] MarkerTexts(Grid list)
        => [.. Enumerable.Range(0, list.RowDefinitions.Count)
            .Select(item => Assert.IsType<MarkdownSelectionTextFragment>(Cells(list, item)[0]).StyledText.Text)];

    /// <summary>Контролы пункта в порядке колонок: маркеры, затем текст.</summary>
    private static Control[] Cells(Grid list, int item)
        => list.Children.Where(child => Grid.GetRow(child) == item).OrderBy(Grid.GetColumn).ToArray();

    private static StackPanel Content(Grid list, int item) => Assert.IsType<StackPanel>(Cells(list, item)[^1]);

    private static double Left(Control control, Visual relativeTo)
        => control.TranslatePoint(default, relativeTo)!.Value.X;

    private static double Right(Control control, Visual relativeTo)
        => control.TranslatePoint(new Point(control.Bounds.Width, 0), relativeTo)!.Value.X;
}
