using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using MarkMello.Domain;
using MarkMello.Presentation.Clipboard;
using MarkMello.Presentation.Localization;
using MarkMello.Presentation.Views;
using MarkMello.Presentation.Views.Markdown;
using AvaloniaApplication = Avalonia.Application;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// GitHub alert — плашка цвета вида без полосы: иконка Lucide в своей колонке
/// слева, справа заголовок того же цвета и тело обычным, не курсивным текстом.
/// Заголовок — часть текста документа: выделяется, ищется и копируется вместе с
/// alert (ADR-0001), на языке интерфейса. Обычная цитата остаётся цитатой.
/// </summary>
[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class MarkdownAlertLayoutTests
{
    private readonly AvaloniaHeadlessFixture _fixture;

    public MarkdownAlertLayoutTests(AvaloniaHeadlessFixture fixture) => _fixture = fixture;

    [Fact]
    public Task AlertShowsAHeaderWithIconAndTitleAboveAPlainBody()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(AlertDocument(MarkdownAlertKind.Note, "Useful information."));

            var alert = TopLevelQuote(view);
            Assert.Contains("mm-md-alert", alert.Classes);
            Assert.Contains("mm-md-alert-note", alert.Classes);

            var (icon, title) = Header(alert);
            var fontSize = ReadingPreferences.Default.FontSize;
            var iconSize = Math.Round(fontSize * 1.15);
            Assert.Equal(iconSize, icon.Width);
            Assert.Equal(iconSize, icon.Height);
            Assert.Contains("mm-md-alert-note", icon.Classes);

            // Иконка — в своей колонке слева и чуть ниже верха заголовка, текст — справа от неё.
            Assert.Equal(0, Grid.GetColumn(icon));
            Assert.Equal(1, Grid.GetColumn(Stack(alert)));
            Assert.Equal(fontSize * 0.2, icon.Margin.Top, 3);
            Assert.Equal(fontSize * 0.6, Assert.IsType<Grid>(alert.Child).ColumnSpacing, 3);
            Assert.Equal(new Thickness(fontSize * 0.8, fontSize * 0.85, fontSize, fontSize * 0.85), alert.Padding);
            Assert.Equal(new CornerRadius(fontSize * 4 / 14), alert.CornerRadius);
            Assert.Equal("Note", title.StyledText.Text);
            Assert.Equal(FontWeight.SemiBold, title.BaseFontWeight);
            Assert.Equal("MmAlertNoteBrush", title.BaseForegroundResourceKey);

            var body = Assert.IsType<MarkdownSelectionTextFragment>(Body(alert)[0]);
            Assert.Equal("Useful information.", body.StyledText.Text);
            Assert.Equal(fontSize * 0.15, body.Margin.Top, 3);
            Assert.Equal(FontStyle.Normal, body.BaseFontStyle);
            Assert.Null(body.BaseForeground);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("paragraph")]
    [InlineData("list")]
    [InlineData("quote")]
    public Task SpaceBelowTheAlertTextIsTheSameAsAboveTheHeader(string lastBlock)
    {
        return _fixture.Session.Dispatch(() =>
        {
            MarkdownBlock last = lastBlock switch
            {
                "list" => new MarkdownListBlock(false, [new MarkdownListItem([Paragraph("One")]), new MarkdownListItem([Paragraph("Two")])]),
                "quote" => new MarkdownQuoteBlock([Paragraph("Nested one."), Paragraph("Nested two.")]),
                _ => Paragraph("Second paragraph.")
            };
            var view = CreateView(new RenderedMarkdownDocument(
            [
                new MarkdownQuoteBlock([Paragraph("First paragraph."), last], MarkdownAlertKind.Warning),
                Paragraph("After.")
            ]));
            var window = Show(view);

            var alert = Assert.IsType<Border>(Root(view).Children[0]);
            var header = Header(alert).Title;
            var lastText = alert.GetVisualDescendants().OfType<MarkdownSelectionTextFragment>().Last();
            var above = header.TranslatePoint(default, alert)!.Value.Y;
            var below = alert.Bounds.Height - lastText.TranslatePoint(new Point(0, lastText.Bounds.Height), alert)!.Value.Y;

            // У цитаты внутри alert под текстом остаётся только её собственное
            // поле — такое же, как у неё сверху.
            var nestedQuotePadding = Body(alert)[^1] is Border nested ? Stack(nested).Margin.Bottom : 0;
            Assert.Equal(above + nestedQuotePadding, below, 0.5);

            // Между блоками внутри alert просвет остаётся.
            Assert.True(Body(alert)[1].Margin.Top > 0);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task ListInsideAnAlertIsPlainTextToo()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(new RenderedMarkdownDocument(
            [
                new MarkdownQuoteBlock(
                [
                    new MarkdownListBlock(false, [new MarkdownListItem([Paragraph("Item")])])
                ],
                MarkdownAlertKind.Tip)
            ]));

            var list = Assert.IsType<Grid>(Body(TopLevelQuote(view))[0]);
            var item = list.GetVisualDescendants().OfType<MarkdownSelectionTextFragment>()
                .Single(fragment => fragment.StyledText.Text == "Item");
            Assert.Equal(FontStyle.Normal, item.BaseFontStyle);
        }, CancellationToken.None);
    }

    [Fact]
    public Task PlainQuoteIsUprightTextWithoutHeader()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(new RenderedMarkdownDocument(
            [
                new MarkdownQuoteBlock([Paragraph("Just a quote.")])
            ]));

            var quote = TopLevelQuote(view);
            Assert.Equal(["mm-md-quote"], quote.Classes);
            Assert.Contains("mm-md-quote-mark", Assert.Single(quote.GetVisualDescendants().OfType<LucideIcon>()).Classes);

            var paragraph = Assert.IsType<MarkdownSelectionTextFragment>(Assert.Single(Stack(quote).Children));
            Assert.Equal("Just a quote.", paragraph.StyledText.Text);
            // Несколько строк курсивом читать тяжело: цитата — прямым шрифтом цветом текста.
            Assert.Equal(FontStyle.Normal, paragraph.BaseFontStyle);
            Assert.Null(paragraph.BaseForeground);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(MarkdownAlertKind.Note, "Light", "LucideInfoGeometry", "MmAlertNoteBrush")]
    [InlineData(MarkdownAlertKind.Tip, "Light", "LucideLightbulbGeometry", "MmAlertTipBrush")]
    [InlineData(MarkdownAlertKind.Important, "Light", "LucideMessageSquareWarningGeometry", "MmAlertImportantBrush")]
    [InlineData(MarkdownAlertKind.Warning, "Light", "LucideTriangleAlertGeometry", "MmAlertWarningBrush")]
    [InlineData(MarkdownAlertKind.Caution, "Light", "LucideOctagonAlertGeometry", "MmAlertCautionBrush")]
    [InlineData(MarkdownAlertKind.Note, "Dark", "LucideInfoGeometry", "MmAlertNoteBrush")]
    [InlineData(MarkdownAlertKind.Tip, "Dark", "LucideLightbulbGeometry", "MmAlertTipBrush")]
    [InlineData(MarkdownAlertKind.Important, "Dark", "LucideMessageSquareWarningGeometry", "MmAlertImportantBrush")]
    [InlineData(MarkdownAlertKind.Warning, "Dark", "LucideTriangleAlertGeometry", "MmAlertWarningBrush")]
    [InlineData(MarkdownAlertKind.Caution, "Dark", "LucideOctagonAlertGeometry", "MmAlertCautionBrush")]
    public Task EveryKindHasItsIconAndColourInBothThemes(
        MarkdownAlertKind kind,
        string themeName,
        string geometryKey,
        string brushKey)
    {
        return _fixture.Session.Dispatch(() =>
        {
            var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
            var view = CreateView(AlertDocument(kind, "Body."));
            var window = ThemedTestWindow.Create(theme, view);
            window.Show();
            window.UpdateLayout();

            var alert = TopLevelQuote(view);
            var (icon, title) = Header(alert);
            var brush = Resource<IBrush>(window, brushKey, theme);
            var background = Resource<ISolidColorBrush>(window, brushKey.Replace("Brush", "BackgroundBrush", StringComparison.Ordinal), theme);

            Assert.Same(Resource<Geometry>(window, geometryKey, theme), icon.Data);
            Assert.Same(brush, icon.Foreground);
            Assert.Equal(brushKey, title.BaseForegroundResourceKey);

            // Плашка — цвет вида с прозрачностью 8 % в светлой теме и 10 % в тёмной, без полосы.
            Assert.Same(background, alert.Background);
            var colour = Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
            Assert.Equal(Color.FromArgb(themeName == "Light" ? (byte)0x14 : (byte)0x1A, colour.R, colour.G, colour.B), background.Color);
            Assert.Equal(default, alert.BorderThickness);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task TitleColourFollowsTheThemeWithoutARebuild()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(AlertDocument(MarkdownAlertKind.Warning, "Body."));
            var window = ThemedTestWindow.Create(ThemeVariant.Light, view);
            window.Show();
            window.UpdateLayout();

            var alert = TopLevelQuote(view);
            var (icon, title) = Header(alert);
            Assert.Same(Resource<IBrush>(window, "MmAlertWarningBrush", ThemeVariant.Light), title.ResolveBaseTextBrush());

            window.RequestedThemeVariant = ThemeVariant.Dark;
            window.UpdateLayout();

            var dark = Resource<IBrush>(window, "MmAlertWarningBrush", ThemeVariant.Dark);
            Assert.Same(alert, TopLevelQuote(view));
            Assert.Same(Resource<IBrush>(window, "MmAlertWarningBackgroundBrush", ThemeVariant.Dark), alert.Background);
            Assert.Same(dark, icon.Foreground);
            Assert.Same(dark, title.ResolveBaseTextBrush());

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task SelectAllCopiesTheTitleWithTheAlertText()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(new RenderedMarkdownDocument(
            [
                Paragraph("Before."),
                new MarkdownQuoteBlock([Paragraph("Key information.")], MarkdownAlertKind.Important),
                Paragraph("After.")
            ]));

            view.SelectAll();

            Assert.Equal("Before.\n\nImportant\nKey information.\n\nAfter.", view.SelectedText?.TrimEnd('\n'));
            var (_, title) = Header(Assert.IsType<Border>(Root(view).Children[1]));
            Assert.False(title.SelectionRange.Intersection(title.DocumentRange).IsEmpty);
        }, CancellationToken.None);
    }

    [Fact]
    public Task DraggingFromTheTitleSelectsItWithTheBody()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(AlertDocument(MarkdownAlertKind.Caution, "Risky."));
            var window = Show(view);

            var alert = TopLevelQuote(view);
            var (_, title) = Header(alert);
            var body = Body(alert)[0];
            var start = title.TranslatePoint(new Point(1, title.Bounds.Height / 2), window)!.Value;
            var end = body.TranslatePoint(new Point(body.Bounds.Width - 1, body.Bounds.Height / 2), window)!.Value;

            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(end);
            window.MouseUp(end, MouseButton.Left);

            Assert.Equal("Caution\nRisky.", view.SelectedText?.TrimEnd('\n'));

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task SearchFindsTheTitleAndTheBody()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(AlertDocument(MarkdownAlertKind.Tip, "A tip about tips."));

            view.ApplySearchQuery("tip");

            Assert.Equal(3, view.MatchCount);
            var alert = TopLevelQuote(view);
            Assert.Single(Header(alert).Title.SearchHighlightRanges);
            Assert.Equal(2, Assert.IsType<MarkdownSelectionTextFragment>(Body(alert)[0]).SearchHighlightRanges.Count);
        }, CancellationToken.None);
    }

    [Fact]
    public Task AlertIsRebuiltWhenItsKindChangesInTheSource()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(AlertDocument(MarkdownAlertKind.Note, "Same text."));
            var before = TopLevelQuote(view);

            view.Document = new RenderedMarkdownDocument([new MarkdownQuoteBlock([Paragraph("Same text.")])]);
            var plain = TopLevelQuote(view);

            view.Document = AlertDocument(MarkdownAlertKind.Warning, "Same text.");
            var warning = TopLevelQuote(view);

            Assert.NotSame(before, plain);
            Assert.DoesNotContain("mm-md-alert", plain.Classes);
            Assert.NotSame(plain, warning);
            Assert.Contains("mm-md-alert-warning", warning.Classes);
        }, CancellationToken.None);
    }

    [Fact]
    public Task TitleIsInTheInterfaceLanguageAndFollowsItsChange()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var resources = AvaloniaApplication.Current!.Resources;
            var localization = new LocalizationService(AppLanguage.Russian);
            resources["Localization"] = localization;
            try
            {
                var document = AlertDocument(MarkdownAlertKind.Warning, "Текст.");
                var view = CreateView(document);
                var window = Show(view);

                Assert.Equal("Предупреждение", Header(TopLevelQuote(view)).Title.StyledText.Text);
                view.SelectAll();
                Assert.Equal("Предупреждение\nТекст.", view.SelectedText?.TrimEnd('\n'));

                localization.SetLanguage(AppLanguage.English);

                Assert.Equal("Warning", Header(TopLevelQuote(view)).Title.StyledText.Text);
                view.SelectAll();
                Assert.Equal("Warning\nТекст.", view.SelectedText?.TrimEnd('\n'));

                // Копирование в Telegram-форматы видит те же заголовки, что и view.
                var range = new DocumentTextRange(0, "Warning\nТекст.".Length);
                Assert.Equal("> *Warning*\n> Текст\\.", TelegramMarkdownFormatter.FormatSelection(
                    document,
                    range,
                    MarkdownAlertTitles.Create((key, fallback) => localization[key]).Get));

                window.Close();
            }
            finally
            {
                resources.Remove("Localization");
            }
        }, CancellationToken.None);
    }

    [Fact]
    public Task LanguageChangedWhileDetachedIsPickedUpWhenTheViewIsShownAgain()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var resources = AvaloniaApplication.Current!.Resources;
            var localization = new LocalizationService(AppLanguage.English);
            resources["Localization"] = localization;
            try
            {
                var view = CreateView(AlertDocument(MarkdownAlertKind.Note, "Text."));
                var window = Show(view);
                Assert.Equal("Note", Header(TopLevelQuote(view)).Title.StyledText.Text);

                window.Content = null;
                localization.SetLanguage(AppLanguage.Russian);
                window.Content = view;
                window.UpdateLayout();

                Assert.Equal("Примечание", Header(TopLevelQuote(view)).Title.StyledText.Text);
                view.SelectAll();
                Assert.Equal("Примечание\nText.", view.SelectedText?.TrimEnd('\n'));

                window.Close();
            }
            finally
            {
                resources.Remove("Localization");
            }
        }, CancellationToken.None);
    }

    [Fact]
    public Task LanguageChangeLeavesADocumentWithoutAlertsAlone()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var resources = AvaloniaApplication.Current!.Resources;
            var localization = new LocalizationService(AppLanguage.English);
            resources["Localization"] = localization;
            try
            {
                var view = CreateView(new RenderedMarkdownDocument([Paragraph("Text.")]));
                var window = Show(view);
                var before = Assert.Single(Root(view).Children);

                localization.SetLanguage(AppLanguage.Russian);
                var afterLanguageChange = Assert.Single(Root(view).Children);

                // И следующая правка переиспользует неизменившийся блок, как обычно.
                view.Document = new RenderedMarkdownDocument([Paragraph("Text.")]);
                var afterEdit = Assert.Single(Root(view).Children);

                Assert.Same(before, afterLanguageChange);
                Assert.Same(before, afterEdit);

                window.Close();
            }
            finally
            {
                resources.Remove("Localization");
            }
        }, CancellationToken.None);
    }

    private static T Resource<T>(Window window, string key, ThemeVariant theme)
    {
        Assert.True(window.TryFindResource(key, theme, out var value), $"{key} is not defined in the theme");
        return Assert.IsAssignableFrom<T>(value);
    }

    private static RenderedMarkdownDocument AlertDocument(MarkdownAlertKind kind, string body)
        => new([new MarkdownQuoteBlock([Paragraph(body)], kind)]);

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

    private static StackPanel Root(MarkdownDocumentView view)
    {
        var viewport = Assert.IsType<Border>(view.Content);
        return Assert.IsType<StackPanel>(viewport.Child);
    }

    private static Border TopLevelQuote(MarkdownDocumentView view)
        => Assert.IsType<Border>(Assert.Single(Root(view).Children));

    /// <summary>Блоки цитаты или alert: у цитаты рядом значок, у alert — иконка.</summary>
    private static StackPanel Stack(Border quote) => quote.Child switch
    {
        StackPanel stack => stack,
        Grid grid => Assert.Single(grid.Children.OfType<StackPanel>()),
        var child => throw new InvalidOperationException($"Unexpected quote content {child}")
    };

    private static (LucideIcon Icon, MarkdownSelectionTextFragment Title) Header(Border alert)
    {
        var grid = Assert.IsType<Grid>(alert.Child);
        return (
            Assert.Single(grid.Children.OfType<LucideIcon>()),
            Assert.IsType<MarkdownSelectionTextFragment>(Stack(alert).Children[0]));
    }

    private static Control[] Body(Border alert) => Stack(alert).Children.Skip(1).ToArray();
}
