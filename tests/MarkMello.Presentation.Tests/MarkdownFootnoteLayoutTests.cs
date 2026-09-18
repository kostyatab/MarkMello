using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MarkMello.Domain;
using MarkMello.Presentation.Views;
using MarkMello.Presentation.Views.Markdown;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Метка сноски — номер верхним индексом, сноски — отделённый блок с номерами в
/// конце документа. Клик по метке прокручивает к сноске, клик по номеру сноски —
/// обратно к метке, как переход по якорю заголовка. Метка и сноски — часть текста
/// документа для выделения, поиска и копирования (ADR-0001).
/// </summary>
[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class MarkdownFootnoteLayoutTests
{
    // Отступ цели от верха страницы после перехода — как у якорей заголовков.
    private const double ScrollTopInset = 24;
    private const double Tolerance = 1;

    private readonly AvaloniaHeadlessFixture _fixture;

    public MarkdownFootnoteLayoutTests(AvaloniaHeadlessFixture fixture) => _fixture = fixture;

    [Fact]
    public Task FootnotesFollowAShortRuleAndAreNumbered()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(new RenderedMarkdownDocument(
            [
                Paragraph("Text", new MarkdownFootnoteReferenceInline(1), new MarkdownFootnoteReferenceInline(2)),
                new MarkdownFootnotesBlock(
                [
                    new MarkdownFootnote(1, [Paragraph("One.")]),
                    new MarkdownFootnote(2, [Paragraph("Two."), Paragraph("Two, second paragraph.")])
                ])
            ]));
            var window = Show(view);

            var footnotes = FootnotesBlock(view);
            Assert.Contains("mm-md-footnotes", footnotes.Classes);

            var ruleRow = Assert.IsType<Grid>(footnotes.Children[0]);
            var rule = Assert.IsType<Border>(Assert.Single(ruleRow.Children));
            Assert.Contains("mm-md-hr", rule.Classes);
            Assert.Equal(1, rule.Bounds.Height, Tolerance);

            // Черта короткая и слева, как в книге: треть колонки.
            Assert.Equal(0, rule.Bounds.X, Tolerance);
            Assert.Equal(footnotes.Bounds.Width / 3, rule.Bounds.Width, Tolerance);

            var list = Assert.IsType<Grid>(footnotes.Children[1]);
            Assert.Equal("1. ", Marker(list, 0).StyledText.Text);
            Assert.Equal("2. ", Marker(list, 1).StyledText.Text);
            Assert.Single(Content(list, 0).Children);
            Assert.Equal(2, Content(list, 1).Children.Count);

            // Текст всех сносок начинается с одной вертикали, справа от номеров.
            Assert.Equal(Content(list, 0).Bounds.X, Content(list, 1).Bounds.X, Tolerance);
            Assert.True(Content(list, 0).Bounds.X >= Marker(list, 0).Bounds.Right);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task ReferenceIsASmallNumberThatDoesNotMakeTheLineTaller()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(new RenderedMarkdownDocument(
            [
                Paragraph("Short", new MarkdownFootnoteReferenceInline(1)),
                Paragraph("Short[1]"),
                new MarkdownFootnotesBlock([new MarkdownFootnote(1, [Paragraph("Note.")])])
            ]));
            var window = Show(view);

            var withReference = TextFragment(view, 0);
            var plain = TextFragment(view, 1);

            // В тексте документа — «[1]», как в строке без сноски.
            Assert.Equal("Short[1]", withReference.StyledText.Text);
            Assert.Equal(plain.Bounds.Height, withReference.Bounds.Height, Tolerance);

            // На экране — один номер мельче текста, а не «[1]».
            Assert.True(withReference.TryGetHorizontalExtentForLocalRange(5, 8, out var referenceLeft, out var referenceRight));
            Assert.True(plain.TryGetHorizontalExtentForLocalRange(5, 8, out var bracketsLeft, out var bracketsRight));
            Assert.True(plain.TryGetHorizontalExtentForLocalRange(6, 7, out var digitLeft, out var digitRight));
            Assert.True(referenceRight - referenceLeft < (bracketsRight - bracketsLeft) / 2);
            Assert.True(referenceRight - referenceLeft < digitRight - digitLeft + 2 + Tolerance);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task ClickingTheReferenceScrollsToTheFootnoteAndItsNumberScrollsBack()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(new RenderedMarkdownDocument(
            [
                .. Fillers("Before", 20),
                Paragraph("The mention", new MarkdownFootnoteReferenceInline(1), new MarkdownTextInline(" in the middle.")),
                .. Fillers("After", 40),
                new MarkdownFootnotesBlock([new MarkdownFootnote(1, [Paragraph("The footnote.")])])
            ]));
            var (window, page) = ShowOnPage(view);
            var mention = FragmentContaining(view, "The mention[1]");
            ScrollTo(window, page, mention);

            ClickReference(window, mention, "The mention".Length);

            var marker = Marker(FootnotesList(view), 0);
            var markerTop = marker.TranslatePoint(default, page)!.Value.Y;
            Assert.InRange(markerTop, 0, page.Viewport.Height - marker.Bounds.Height);
            Assert.True(page.Offset.Y > 0);

            ClickMarker(window, marker);

            Assert.Equal(ScrollTopInset, LineTop(mention, "The mention".Length, page), Tolerance);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task FootnoteNumberReturnsToTheReferenceTheReaderCameFrom()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(RepeatedReferenceDocument());
            var (window, page) = ShowOnPage(view);
            var second = FragmentContaining(view, "Second mention[1]");
            ScrollTo(window, page, second);

            ClickReference(window, second, "Second mention".Length);
            ClickMarker(window, Marker(FootnotesList(view), 0));

            Assert.Equal(ScrollTopInset, LineTop(second, "Second mention".Length, page), Tolerance);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task FootnoteNumberGoesToTheFirstReferenceWhenTheReaderDidNotComeFromOne()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(RepeatedReferenceDocument());
            var (window, page) = ShowOnPage(view);
            var marker = Marker(FootnotesList(view), 0);
            ScrollTo(window, page, marker);

            ClickMarker(window, marker);

            var first = FragmentContaining(view, "First mention[1]");
            Assert.Equal(ScrollTopInset, LineTop(first, "First mention".Length, page), Tolerance);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task FootnoteNumberGoesToTheFirstReferenceWhenTheOneTheReaderCameFromWasEdited()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(RepeatedReferenceDocument());
            var (window, page) = ShowOnPage(view);
            var second = FragmentContaining(view, "Second mention[1]");
            ScrollTo(window, page, second);
            ClickReference(window, second, "Second mention".Length);

            view.Document = RepeatedReferenceDocument(secondMention: "Edited mention");
            window.UpdateLayout();
            ClickMarker(window, Marker(FootnotesList(view), 0));

            var first = FragmentContaining(view, "First mention[1]");
            Assert.Equal(ScrollTopInset, LineTop(first, "First mention".Length, page), Tolerance);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task FootnoteLinksHaveNoAddressToCopy()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(new RenderedMarkdownDocument(
            [
                Paragraph("Text", new MarkdownFootnoteReferenceInline(1)),
                new MarkdownFootnotesBlock([new MarkdownFootnote(1, [Paragraph("Note.")])])
            ]));
            var window = Show(view);

            var reference = Assert.Single(TextFragment(view, 0).StyledText.Links);
            var back = Assert.Single(Marker(FootnotesList(view), 0).StyledText.Links);

            Assert.Equal(string.Empty, reference.Url);
            Assert.Equal(new MarkdownFootnoteLinkTarget(1, IsBackReference: false), reference.Footnote);
            Assert.Equal(string.Empty, back.Url);
            Assert.Equal(new MarkdownFootnoteLinkTarget(1, IsBackReference: true), back.Footnote);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task SelectAllCopiesTheLabelsAndTheNumberedFootnotes()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(new RenderedMarkdownDocument(
            [
                Paragraph("Markdig", new MarkdownFootnoteReferenceInline(1), new MarkdownTextInline(" is fast.")),
                new MarkdownFootnotesBlock(
                [
                    new MarkdownFootnote(1, [Paragraph("First paragraph."), Paragraph("Second paragraph.")])
                ])
            ]));

            view.SelectAll();

            Assert.Equal(
                "Markdig[1] is fast.\n\n1. First paragraph.\nSecond paragraph.",
                view.SelectedText.TrimEnd('\n'));
        }, CancellationToken.None);
    }

    [Fact]
    public Task DraggingOverTheReferenceSelectsItsLabel()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(new RenderedMarkdownDocument(
            [
                Paragraph("Markdig", new MarkdownFootnoteReferenceInline(1), new MarkdownTextInline(" is fast.")),
                new MarkdownFootnotesBlock([new MarkdownFootnote(1, [Paragraph("Note.")])])
            ]));
            var window = Show(view);

            var fragment = TextFragment(view, 0);
            var middle = fragment.Bounds.Height / 2;
            window.MouseDown(fragment.TranslatePoint(new Point(1, middle), window)!.Value, MouseButton.Left);
            window.MouseMove(fragment.TranslatePoint(new Point(fragment.Bounds.Width - 1, middle), window)!.Value);
            window.MouseUp(fragment.TranslatePoint(new Point(fragment.Bounds.Width - 1, middle), window)!.Value, MouseButton.Left);

            Assert.Equal("Markdig[1] is fast.", view.SelectedText);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task SearchFindsTheLabelsAndTheFootnoteText()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(new RenderedMarkdownDocument(
            [
                Paragraph("Markdig", new MarkdownFootnoteReferenceInline(1), new MarkdownTextInline(", again"), new MarkdownFootnoteReferenceInline(1)),
                new MarkdownFootnotesBlock([new MarkdownFootnote(1, [Paragraph("Markdig is a parser.")])])
            ]));

            view.ApplySearchQuery("[1]");
            Assert.Equal(2, view.MatchCount);

            view.ApplySearchQuery("markdig");
            Assert.Equal(2, view.MatchCount);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public Task FootnoteNumberTakesTheAccentColourOfTheTheme(string themeName)
    {
        return _fixture.Session.Dispatch(() =>
        {
            var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
            var view = CreateView(new RenderedMarkdownDocument(
            [
                Paragraph("Text", new MarkdownFootnoteReferenceInline(1)),
                new MarkdownFootnotesBlock([new MarkdownFootnote(1, [Paragraph("Note.")])])
            ]));
            var window = ThemedTestWindow.Create(theme, view);
            window.Show();
            window.UpdateLayout();

            var marker = Marker(FootnotesList(view), 0);
            Assert.Equal("MmAccentBrush", marker.BaseForegroundResourceKey);
            Assert.Same(Resource<IBrush>(window, "MmAccentBrush", theme), marker.ResolveBaseTextBrush());

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task FootnotesBlockIsReusedWhenTextAboveItChanges()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var footnotes = new MarkdownFootnotesBlock([new MarkdownFootnote(1, [Paragraph("Note.")])]);
            var view = CreateView(new RenderedMarkdownDocument(
            [
                Paragraph("Before", new MarkdownFootnoteReferenceInline(1)),
                footnotes
            ]));
            var before = FootnotesBlock(view);

            view.Document = new RenderedMarkdownDocument(
            [
                Paragraph("Edited", new MarkdownFootnoteReferenceInline(1)),
                new MarkdownFootnotesBlock([new MarkdownFootnote(1, [Paragraph("Note.")])])
            ]);
            Assert.Same(before, FootnotesBlock(view));

            // Другой номер — другая сноска: блок строится заново.
            view.Document = new RenderedMarkdownDocument(
            [
                Paragraph("Edited", new MarkdownFootnoteReferenceInline(2)),
                new MarkdownFootnotesBlock([new MarkdownFootnote(2, [Paragraph("Note.")])])
            ]);
            Assert.NotSame(before, FootnotesBlock(view));
        }, CancellationToken.None);
    }

    private static RenderedMarkdownDocument RepeatedReferenceDocument(string secondMention = "Second mention")
        => new(
        [
            .. Fillers("Before", 10),
            Paragraph("First mention", new MarkdownFootnoteReferenceInline(1), new MarkdownTextInline(".")),
            .. Fillers("Between", 20),
            Paragraph(secondMention, new MarkdownFootnoteReferenceInline(1), new MarkdownTextInline(".")),
            .. Fillers("After", 30),
            new MarkdownFootnotesBlock([new MarkdownFootnote(1, [Paragraph("The footnote.")])])
        ]);

    private static IEnumerable<MarkdownBlock> Fillers(string prefix, int count)
        => Enumerable.Range(1, count).Select(index => Paragraph($"{prefix} filler paragraph {index}."));

    private static MarkdownParagraphBlock Paragraph(string text, params MarkdownInline[] rest)
        => new([new MarkdownTextInline(text), .. rest]);

    private static MarkdownDocumentView CreateView(RenderedMarkdownDocument document)
        => new()
        {
            ReadingPreferences = ReadingPreferences.Default,
            Document = document
        };

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
    /// Документ на странице, как в ViewerView: переходы прокручивают страницу —
    /// ближайший <see cref="ScrollViewer"/> над документом.
    /// </summary>
    private static (Window Window, ScrollViewer Page) ShowOnPage(MarkdownDocumentView view)
    {
        var page = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = view
        };

        var window = ThemedTestWindow.Create(ThemeVariant.Light, page);
        window.Width = 600;
        window.Height = 400;
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return (window, page);
    }

    /// <summary>Прокручивает страницу так, чтобы контрол был в середине окна.</summary>
    private static void ScrollTo(Window window, ScrollViewer page, Control control)
    {
        var top = control.TranslatePoint(default, page)!.Value.Y;
        page.Offset = new Vector(0, page.Offset.Y + top - page.Viewport.Height / 2);
        window.UpdateLayout();
    }

    /// <summary>
    /// Клик по метке сноски — в правую половину номера: на экране метка — один
    /// символ, и попадание не должно зависеть от того, в какую его половину пришёлся клик.
    /// </summary>
    private static void ClickReference(Window window, MarkdownSelectionTextFragment fragment, int labelStart)
    {
        Assert.True(fragment.TryGetHorizontalExtentForLocalRange(labelStart, labelStart + 3, out _, out var right));
        Click(window, fragment, right - 1, labelStart);
    }

    private static void ClickMarker(Window window, MarkdownSelectionTextFragment marker)
    {
        Assert.True(marker.TryGetHorizontalExtentForLocalRange(0, 1, out var left, out var right));
        Click(window, marker, (left + right) / 2, 0);
    }

    private static void Click(Window window, MarkdownSelectionTextFragment fragment, double x, int localOffset)
    {
        Assert.True(fragment.TryGetLineTopForLocalOffset(localOffset, out var lineTop));
        var point = fragment.TranslatePoint(new Point(x, lineTop + 8), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        window.UpdateLayout();
    }

    /// <summary>Верх строки с offset'ом в координатах окна страницы.</summary>
    private static double LineTop(MarkdownSelectionTextFragment fragment, int localOffset, ScrollViewer page)
    {
        Assert.True(fragment.TryGetLineTopForLocalOffset(localOffset, out var lineTop));
        return fragment.TranslatePoint(new Point(0, lineTop), page)!.Value.Y;
    }

    private static T Resource<T>(Window window, string key, ThemeVariant theme)
    {
        Assert.True(window.TryFindResource(key, theme, out var value), $"{key} is not defined in the theme");
        return Assert.IsAssignableFrom<T>(value);
    }

    private static StackPanel Root(MarkdownDocumentView view)
    {
        var viewport = Assert.IsType<Border>(view.Content);
        return Assert.IsType<StackPanel>(viewport.Child);
    }

    private static MarkdownSelectionTextFragment TextFragment(MarkdownDocumentView view, int blockIndex)
        => Assert.IsType<MarkdownSelectionTextFragment>(Root(view).Children[blockIndex]);

    private static MarkdownSelectionTextFragment FragmentContaining(MarkdownDocumentView view, string text)
        => Assert.Single(
            view.GetVisualDescendants().OfType<MarkdownSelectionTextFragment>(),
            fragment => fragment.StyledText.Text.Contains(text, StringComparison.Ordinal));

    private static StackPanel FootnotesBlock(MarkdownDocumentView view)
        => Assert.IsType<StackPanel>(Root(view).Children[^1]);

    private static Grid FootnotesList(MarkdownDocumentView view)
        => Assert.IsType<Grid>(FootnotesBlock(view).Children[1]);

    private static MarkdownSelectionTextFragment Marker(Grid list, int row)
    {
        var marker = Assert.IsType<MarkdownSelectionTextFragment>(Assert.Single(
            list.Children,
            child => Grid.GetRow(child) == row && Grid.GetColumn(child) == 0));
        Assert.Equal(HorizontalAlignment.Right, marker.HorizontalAlignment);
        return marker;
    }

    private static StackPanel Content(Grid list, int row)
        => Assert.IsType<StackPanel>(Assert.Single(
            list.Children,
            child => Grid.GetRow(child) == row && Grid.GetColumn(child) == 1));
}
