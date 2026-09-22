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

    /// <summary>
    /// Книжный блок сносок: просвет как перед H2, короткая линия 4em кегля сноски
    /// у левого края, от неё до первой сноски 1em; номер справа в колонке 1.6em,
    /// до текста .5em; текст .875em мягким цветом, межстрочный 1.5; между
    /// сносками и абзацами внутри — .45em.
    /// </summary>
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

            const double fontSize = 14;
            const double footnoteSize = fontSize * 0.875;
            var footnotes = FootnotesBlock(view);
            Assert.Contains("mm-md-footnotes", footnotes.Classes);
            Assert.Equal(fontSize * 2.29, footnotes.Bounds.Top - TextFragment(view, 0).Bounds.Bottom, Tolerance);
            Assert.Equal(0, footnotes.TranslatePoint(default, view)!.Value.X - TextFragment(view, 0).TranslatePoint(default, view)!.Value.X, Tolerance);

            var rule = Assert.IsType<Border>(footnotes.Children[0]);
            Assert.Contains("mm-md-hr", rule.Classes);
            Assert.Equal(1, rule.Bounds.Height, Tolerance);
            Assert.Equal(0, rule.Bounds.X, Tolerance);
            Assert.Equal(footnoteSize * 4, rule.Bounds.Width, Tolerance);

            var list = Assert.IsType<Grid>(footnotes.Children[1]);
            Assert.Equal(footnoteSize, list.Bounds.Top - rule.Bounds.Top, Tolerance);

            // Номер без точки, акцентом, полужирный, цифрами одной ширины, прижат
            // цифрой к краю колонки: сноска в .15em от края, колонка 1.6em вместе
            // с отступом .5em до текста. Пробел после номера только для копии.
            var marker = Marker(list, 0);
            Assert.Equal("1 ", marker.StyledText.Text);
            Assert.Equal("2 ", Marker(list, 1).StyledText.Text);
            Assert.Equal(footnoteSize, marker.BaseFontSize, Tolerance);
            Assert.Equal(FontWeight.SemiBold, marker.BaseFontWeight);
            Assert.Same(MarkdownTextRunPropertiesFactory.TabularNumberFontFeatures, marker.BaseFontFeatures);
            Assert.Equal(footnoteSize * (0.15 + 1.6 - 0.5), marker.TranslatePoint(new Point(marker.Bounds.Width, 0), footnotes)!.Value.X, Tolerance);
            Assert.True(marker.TryGetHorizontalExtentForLocalRange(0, 1, out _, out var digitRight));
            Assert.Equal(marker.Bounds.Width, digitRight, Tolerance);

            // Текст всех сносок начинается с одной вертикали, в .5em от номеров.
            Assert.Single(Content(list, 0).Children);
            Assert.Equal(2, Content(list, 1).Children.Count);
            Assert.Equal(footnoteSize * (0.15 + 1.6), Content(list, 0).TranslatePoint(default, footnotes)!.Value.X, Tolerance);
            Assert.Equal(Content(list, 0).Bounds.X, Content(list, 1).Bounds.X, Tolerance);

            var text = Assert.IsType<MarkdownSelectionTextFragment>(Content(list, 0).Children[0]);
            Assert.Equal(footnoteSize, text.BaseFontSize, Tolerance);
            Assert.Equal(footnoteSize * 1.5, text.BaseLineHeight, Tolerance);
            Assert.Equal("MmTextSoftBrush", text.BaseForegroundResourceKey);

            // Между сносками и между абзацами одной сноски — .45em кегля сноски.
            Assert.Equal(footnoteSize * 0.45, Content(list, 1).Bounds.Top - Content(list, 0).Bounds.Bottom, Tolerance);
            var second = Content(list, 1);
            Assert.Equal(footnoteSize * 0.45, second.Children[1].Bounds.Top - second.Children[0].Bounds.Bottom, Tolerance);

            window.Close();
        }, CancellationToken.None);
    }

    /// <summary>Код и список в сноске — обычного размера, как в тексте; список — мягким цветом сноски.</summary>
    [Fact]
    public Task CodeAndListsInsideAFootnoteKeepTheirUsualSize()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(new RenderedMarkdownDocument(
            [
                Paragraph("Text", new MarkdownFootnoteReferenceInline(1)),
                new MarkdownFootnotesBlock(
                [
                    new MarkdownFootnote(1,
                    [
                        Paragraph("Starts with a paragraph:"),
                        new MarkdownListBlock(false, [new MarkdownListItem([Paragraph("List item")])]),
                        new MarkdownCodeBlock("bash", "dotnet test")
                    ])
                ])
            ]));
            var window = Show(view);

            var item = FragmentContaining(view, "List item");
            Assert.Equal(14, item.BaseFontSize, Tolerance);
            Assert.Equal(14 * 1.6, item.BaseLineHeight, Tolerance);
            Assert.Equal("MmTextSoftBrush", item.BaseForegroundResourceKey);
            Assert.Equal("MmTextSoftBrush", FragmentContaining(view, "• ").BaseForegroundResourceKey);

            var code = FragmentContaining(view, "dotnet test");
            Assert.Equal(14 * 0.85, code.BaseFontSize, Tolerance);

            // Список — через .45em кегля сноски, код — через .6em текста.
            var content = Content(FootnotesList(view), 0);
            Assert.Equal(14 * 0.875 * 0.45, content.Children[1].Bounds.Top - content.Children[0].Bounds.Bottom, Tolerance);
            Assert.Equal(14 * 0.6, content.Children[2].Bounds.Top - content.Children[1].Bounds.Bottom, Tolerance);

            window.Close();
        }, CancellationToken.None);
    }

    /// <summary>
    /// В конце последнего абзаца сноски — иконка возврата к метке; после списка
    /// или кода в конце сноски её нет. В текст документа иконка не попадает.
    /// </summary>
    [Fact]
    public Task OnlyAFootnoteEndingWithAParagraphGetsTheBackIcon()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(new RenderedMarkdownDocument(
            [
                Paragraph("Text", new MarkdownFootnoteReferenceInline(1), new MarkdownFootnoteReferenceInline(2), new MarkdownFootnoteReferenceInline(3)),
                new MarkdownFootnotesBlock(
                [
                    new MarkdownFootnote(1, [Paragraph("First paragraph."), Paragraph("Last paragraph.")]),
                    new MarkdownFootnote(2, [Paragraph("Then a list:"), new MarkdownListBlock(false, [new MarkdownListItem([Paragraph("Item")])])]),
                    new MarkdownFootnote(3, [Paragraph("Then code:"), new MarkdownCodeBlock(null, "code")])
                ])
            ]));
            var window = Show(view);

            Assert.Null(FragmentContaining(view, "First paragraph.").StyledText.BackReferenceNumber);
            var last = FragmentContaining(view, "Last paragraph.");
            Assert.Equal(1, last.StyledText.BackReferenceNumber);
            Assert.Equal("Last paragraph.", last.StyledText.Text);
            Assert.Null(FragmentContaining(view, "Then a list:").StyledText.BackReferenceNumber);
            Assert.Null(FragmentContaining(view, "Item").StyledText.BackReferenceNumber);
            Assert.Null(FragmentContaining(view, "Then code:").StyledText.BackReferenceNumber);

            view.SelectAll();
            Assert.Contains("1 First paragraph.\nLast paragraph.\n2 Then a list:", view.SelectedText, StringComparison.Ordinal);

            window.Close();
        }, CancellationToken.None);
    }

    /// <summary>
    /// Сноска, которая кончается абзацем из одной картинки, тоже получает иконку
    /// возврата: такой абзац строится текстовым фрагментом, а не потоком картинок.
    /// </summary>
    [Fact]
    public Task FootnoteEndingWithAnImageOnlyParagraphKeepsTheBackIcon()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(new RenderedMarkdownDocument(
            [
                Paragraph("Text", new MarkdownFootnoteReferenceInline(1)),
                new MarkdownFootnotesBlock(
                [
                    new MarkdownFootnote(1, [new MarkdownParagraphBlock([new MarkdownImageInline("missing.png", "Chart", null)])])
                ])
            ]));
            var window = Show(view);

            var content = Content(FootnotesList(view), 0);
            var image = Assert.IsType<MarkdownSelectionTextFragment>(Assert.Single(content.Children));
            Assert.Equal(1, image.StyledText.BackReferenceNumber);

            window.Close();
        }, CancellationToken.None);
    }

    /// <summary>
    /// Иконка — 1em текста сноски в .35em от последнего слова, приглушённым цветом;
    /// клик по ней ведёт к метке, как клик по номеру.
    /// </summary>
    [Fact]
    public Task BackIconFollowsTheLastWordAndLeadsToTheReference()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(new RenderedMarkdownDocument(
            [
                .. Fillers("Before", 10),
                Paragraph("The mention", new MarkdownFootnoteReferenceInline(1), new MarkdownTextInline(".")),
                .. Fillers("After", 40),
                new MarkdownFootnotesBlock([new MarkdownFootnote(1, [Paragraph("The footnote.")])])
            ]));
            var (window, page) = ShowOnPage(view);
            var note = FragmentContaining(view, "The footnote.");
            ScrollTo(window, page, note);

            var footnoteSize = 14 * 0.875;
            Assert.True(note.TryGetHorizontalExtentForLocalRange(0, note.StyledText.Text.Length, out _, out var textRight));
            var iconCenter = textRight + footnoteSize * (0.35 + 0.5);
            Assert.True(note.TryGetLinkAt(new Point(iconCenter, footnoteSize * 0.75), out var link));
            Assert.Equal(new MarkdownFootnoteLinkTarget(1, IsBackReference: true), link.Footnote);
            Assert.False(note.TryGetLinkAt(new Point(textRight + footnoteSize * 1.5, footnoteSize * 0.75), out _));

            Click(window, note, iconCenter, 0);

            var mention = FragmentContaining(view, "The mention[1]");
            Assert.Equal(ScrollTopInset, LineTop(mention, "The mention".Length, page), Tolerance);

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
                "Markdig[1] is fast.\n\n1 First paragraph.\nSecond paragraph.",
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
