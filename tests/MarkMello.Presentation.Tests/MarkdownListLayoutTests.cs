using Avalonia;
using Avalonia.Controls;
using MarkMello.Domain;
using MarkMello.Presentation.Views;
using MarkMello.Presentation.Views.Markdown;

namespace MarkMello.Presentation.Tests;

[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class MarkdownListLayoutTests
{
    private const double Tolerance = 0.5;

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

            Assert.Equal(["3. "], MarkerTexts(list));
            Assert.Equal(["5. ", "6. "], MarkerTexts(nested));
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
