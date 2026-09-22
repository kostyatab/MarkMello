using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using MarkMello.Domain;
using MarkMello.Infrastructure.Markdown;
using MarkMello.Presentation.Views;
using MarkMello.Presentation.Views.Markdown;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Основа стиля документа (MM-60): типографика Notion на Inter 14 / 1.6 и ровный
/// ритм блоков. Все размеры — в долях размера текста, поэтому на 18 px пропорции
/// те же, что на 14.
/// </summary>
[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class MarkdownDocumentStyleTests
{
    private const double Tolerance = 0.01;

    // База макетов и крупный текст: пропорции должны совпадать.
    private static readonly int[] TextSizes = [14, 18];

    private readonly AvaloniaHeadlessFixture _fixture;

    public MarkdownDocumentStyleTests(AvaloniaHeadlessFixture fixture) => _fixture = fixture;

    [Theory]
    [InlineData(1, 1.875, 2.5)]
    [InlineData(2, 1.5, 2.29)]
    [InlineData(3, 1.25, 2)]
    [InlineData(4, 1.125, 1.75)]
    [InlineData(5, 1, 1.5)]
    [InlineData(6, 1, 1.5)]
    public Task HeadingSizeAndGapAboveComeFromTheTextSize(int level, double sizeRatio, double gapRatio)
    {
        return _fixture.Session.Dispatch(() =>
        {
            foreach (var fontSize in TextSizes)
            {
                var (window, view) = Show(
                    [Paragraph("Before."), new MarkdownHeadingBlock(level, [new MarkdownTextInline("Heading")]), Paragraph("After.")],
                    fontSize);

                var heading = Text(view, "Heading");
                Assert.Equal(fontSize * sizeRatio, heading.BaseFontSize, Tolerance);
                Assert.Equal(fontSize * sizeRatio * 1.3, heading.BaseLineHeight, Tolerance);
                Assert.Equal(FontWeight.SemiBold, heading.BaseFontWeight);
                Assert.Equal(fontSize * gapRatio, heading.Margin.Top, Tolerance);

                // От заголовка до текста — 1 размер текста.
                Assert.Equal(fontSize, Text(view, "After.").Margin.Top, Tolerance);

                window.Close();
            }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(5, false)]
    [InlineData(6, true)]
    public Task OnlyH6IsSoft(int level, bool soft)
    {
        return _fixture.Session.Dispatch(() =>
        {
            var (window, view) = Show([new MarkdownHeadingBlock(level, [new MarkdownTextInline("Heading")])]);

            var heading = Text(view, "Heading");
            var brush = Assert.IsAssignableFrom<ISolidColorBrush>(heading.ResolveBaseTextBrush());
            Assert.Equal(Color.Parse(soft ? "#5A544F" : "#1F1915"), brush.Color);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task ParagraphsUseTheLineHeightFromSettingsAndOneTextSizeBetween()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var (window, view) = Show([Paragraph("First."), Paragraph("Second.")]);

            var first = Text(view, "First.");
            var second = Text(view, "Second.");
            Assert.Equal(14 * 1.6, first.BaseLineHeight, Tolerance);

            // Первый блок в контейнере — без просвета; нижних отступов у блоков нет.
            Assert.Equal(new Thickness(0), first.Margin);
            Assert.Equal(new Thickness(0, 14, 0, 0), second.Margin);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task RuleSpansTheColumnWithOneTextSizeAroundIt()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var (window, view) = Show([Paragraph("Above."), new MarkdownHorizontalRuleBlock(), Paragraph("Below.")]);

            var root = Root(view);
            var rule = Assert.IsType<Border>(root.Children[1]);
            Assert.Contains("mm-md-hr", rule.Classes);
            Assert.Equal(root.Bounds.Width, rule.Bounds.Width, Tolerance);
            Assert.Equal(1, rule.Bounds.Height, Tolerance);
            Assert.Equal(14, rule.Margin.Top, Tolerance);
            Assert.Equal(14, Text(view, "Below.").Margin.Top, Tolerance);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task GapsDoNotAddUpTheLargerOneWins()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var (window, view) = Show(
            [
                Paragraph("Above."),
                new MarkdownHorizontalRuleBlock(),
                new MarkdownHeadingBlock(2, [new MarkdownTextInline("Heading")])
            ]);

            // Под линией 1 размер текста, над H2 — 2.29: действует просвет заголовка.
            Assert.Equal(14 * 2.29, Text(view, "Heading").Margin.Top, Tolerance);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task ReusedBlockGetsTheGapOfItsNewNeighbour()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var table = new MarkdownTableBlock(
                [new MarkdownTableCell([new MarkdownTextInline("Key")])],
                [[new MarkdownTableCell([new MarkdownTextInline("Value")])]]);
            var (window, view) = Show([table, Paragraph("Kept.")]);
            var kept = Text(view, "Kept.");
            Assert.Equal(14 * 1.4, kept.Margin.Top, Tolerance);

            view.Document = new RenderedMarkdownDocument([Paragraph("New."), Paragraph("Kept.")]);
            window.UpdateLayout();

            Assert.Same(kept, Text(view, "Kept."));
            Assert.Equal(14, kept.Margin.Top, Tolerance);

            window.Close();
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("---")]
    [InlineData("***")]
    [InlineData("___")]
    public void EveryRuleSyntaxIsTheSameBlock(string rule)
    {
        var document = new MarkdigMarkdownDocumentRenderer().Render($"Above.\n\n{rule}\n\nBelow.");

        Assert.IsType<MarkdownHorizontalRuleBlock>(document.Blocks[1]);
    }

    [Theory]
    [InlineData("Light", "#B35025")]
    [InlineData("Dark", "#E38A67")]
    public Task LinkIsUnderlinedWithHalfTransparentAccentInEm(string themeName, string accent)
    {
        return _fixture.Session.Dispatch(() =>
        {
            var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
            var (window, view) = Show([Paragraph("Link.")], theme: theme);

            var decoration = Assert.Single(Text(view, "Link.").BuildLinkTextDecorations());
            Assert.Equal(TextDecorationLocation.Baseline, decoration.Location);
            // Пиксели от кегля текста ссылки, а не от кегля прогона: под кодом и
            // клавишей внутри ссылки линия не становится тоньше и выше.
            Assert.Equal(TextDecorationUnit.Pixel, decoration.StrokeThicknessUnit);
            Assert.Equal(14 * 0.07, decoration.StrokeThickness, Tolerance);
            Assert.Equal(TextDecorationUnit.Pixel, decoration.StrokeOffsetUnit);

            // Верх линии в .2em под базовой линией: центр линии ниже на полтолщины.
            Assert.Equal(14 * (0.2 + 0.07 / 2), decoration.StrokeOffset, Tolerance);

            var stroke = Assert.IsAssignableFrom<ISolidColorBrush>(decoration.Stroke);
            Assert.Equal(Color.Parse(accent), stroke.Color);
            Assert.Equal(0.5, stroke.Opacity, Tolerance);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task LinkTextKeepsTheTextColourAndWeight()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var factory = CreateFactory();

            var link = factory.Get(MarkdownInlineStyleState.Default with { IsLink = true });
            var boldLink = factory.Get(MarkdownInlineStyleState.Default with { IsLink = true, IsBold = true });

            Assert.Same(Brushes.Black, link.ForegroundBrush);
            Assert.Equal(FontWeight.Normal, link.Typeface.Weight);
            Assert.Equal(FontWeight.Bold, boldLink.Typeface.Weight);
        }, CancellationToken.None);
    }

    [Fact]
    public Task LongUrlWrapsAnywhereInsideTheColumn()
    {
        return _fixture.Session.Dispatch(() =>
        {
            const string url = "https://example.com/a/really/long/path/that/does/not/fit/into/the/column/at/all?query=string&and=more";
            var styled = MarkdownStyledText.FromInlines([new MarkdownLinkInline([new MarkdownTextInline(url)], url, null)]);
            using var layout = CreateLayout(styled, maxWidth: 160);

            var lines = layout.GetLineMetrics();
            Assert.True(lines.Count > 1);
            Assert.All(lines, line => Assert.True(line.Bounds.Right <= 160.5, $"a line ends at {line.Bounds.Right}"));
        }, CancellationToken.None);
    }

    [Fact]
    public Task InlineCodeIsSmallerMonoInTheAccentColour()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var factory = CreateFactory();

            var code = factory.Get(MarkdownInlineStyleState.Default with { IsCode = true });

            Assert.Equal(Mono, code.Typeface.FontFamily);
            Assert.Equal(14 * 0.85, code.FontRenderingEmSize, Tolerance);
            Assert.Same(Brushes.OrangeRed, code.ForegroundBrush);
        }, CancellationToken.None);
    }

    [Fact]
    public Task KeyIsAsTallAsInlineCodeAndLevelWithIt()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var styled = MarkdownStyledText.FromInlines(
            [
                new MarkdownCodeInline("code"),
                new MarkdownTextInline(" "),
                new MarkdownKeyboardInline("O"),
                new MarkdownTextInline(" "),
                new MarkdownKeyboardInline("Shift")
            ]);
            using var layout = CreateLayout(styled);

            var code = Assert.Single(layout.GetCodeBoxRects(layout.CodeBoxes[0]));
            var squareKey = Assert.Single(layout.GetCodeBoxRects(layout.CodeBoxes[1]));
            var wideKey = Assert.Single(layout.GetCodeBoxRects(layout.CodeBoxes[2]));

            Assert.Equal(code.Height, squareKey.Height, Tolerance);
            Assert.Equal(code.Top, squareKey.Top, Tolerance);

            // «O» — квадрат, длинная подпись — шире: поля .36em по бокам внутри
            // рамки 1 px, как padding и border в CSS.
            Assert.Equal(squareKey.Height, squareKey.Width, 0.5);
            using var label = new Avalonia.Media.TextFormatting.TextLayout("Shift", new Typeface(Sans), 14 * 0.8, Brushes.Black);
            Assert.Equal(label.WidthIncludingTrailingWhitespace + 2 * (14 * 0.36 + 1), wideKey.Width, 0.5);

            // Строку плашки не раздвигают.
            Assert.Equal(14 * 1.6, Assert.Single(layout.GetLineMetrics()).Bounds.Height, 0.5);
        }, CancellationToken.None);
    }

    [Fact]
    public Task KeysNextToEachOtherAreSeparatedByAGap()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var styled = MarkdownStyledText.FromInlines([new MarkdownKeyboardInline("Ctrl"), new MarkdownKeyboardInline("C")]);
            using var layout = CreateLayout(styled);

            var first = Assert.Single(layout.GetCodeBoxRects(layout.CodeBoxes[0]));
            var second = Assert.Single(layout.GetCodeBoxRects(layout.CodeBoxes[1]));

            Assert.Equal(14 * 0.15, second.Left - first.Right, 0.1);
        }, CancellationToken.None);
    }

    private static readonly FontFamily Mono = new("avares://MarkMello.Presentation/Assets/Fonts/JetBrainsMono#JetBrains Mono");
    private static readonly FontFamily Sans = new("avares://MarkMello.Presentation/Assets/Fonts/Inter#Inter");

    private static MarkdownTextRunPropertiesFactory CreateFactory()
        => new(
            Sans,
            Mono,
            fontSize: 14,
            FontWeight.Normal,
            FontStyle.Normal,
            Brushes.Black,
            linkDecorations: null,
            inlineCodeForeground: Brushes.OrangeRed);

    private static MarkdownFormattedTextLayout CreateLayout(MarkdownStyledText styled, double maxWidth = 100_000)
        => new(
            styled,
            inlineImages: null,
            baseFontFamily: Sans,
            inlineCodeFontFamily: Mono,
            baseFontSize: 14,
            baseFontWeight: FontWeight.Normal,
            baseFontStyle: FontStyle.Normal,
            lineHeight: 14 * 1.6,
            letterSpacing: 0,
            textWrapping: TextWrapping.Wrap,
            textAlignment: TextAlignment.Left,
            maxWidth: maxWidth,
            foreground: Brushes.Black,
            linkDecorations: null,
            imagePlaceholderBrushes: new(Brushes.Transparent, Brushes.Black, Brushes.Black));

    private static MarkdownParagraphBlock Paragraph(string text) => new([new MarkdownTextInline(text)]);

    private static (Window Window, MarkdownDocumentView View) Show(
        IReadOnlyList<MarkdownBlock> blocks,
        int fontSize = 14,
        ThemeVariant? theme = null)
    {
        var view = new MarkdownDocumentView
        {
            ReadingPreferences = ReadingPreferences.Default with { FontSize = fontSize },
            Document = new RenderedMarkdownDocument(blocks)
        };

        var window = ThemedTestWindow.Create(theme ?? ThemeVariant.Light, view);
        window.Show();
        window.UpdateLayout();
        return (window, view);
    }

    private static StackPanel Root(MarkdownDocumentView view)
        => Assert.IsType<StackPanel>(Assert.IsType<Border>(view.Content).Child);

    private static MarkdownSelectionTextFragment Text(MarkdownDocumentView view, string text)
        => view.GetVisualDescendants().OfType<MarkdownSelectionTextFragment>()
            .Single(fragment => fragment.StyledText.Text == text);
}
