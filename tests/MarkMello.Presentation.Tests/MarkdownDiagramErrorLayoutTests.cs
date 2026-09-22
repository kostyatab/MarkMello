using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using MarkMello.Domain;
using MarkMello.Presentation.Localization;
using MarkMello.Presentation.Views;
using MarkMello.Presentation.Views.Markdown;
using AvaloniaApplication = Avalonia.Application;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Диаграмма, которую не удалось показать, выглядит как битая картинка: пунктирная
/// рамка, иконка, заголовок и суть сообщения по центру, ниже — исходник в том же
/// блоке кода, что и в документе. Регрессия: заголовок, сообщение и исходник
/// стояли прямо в тексте без рамки, исходник — зашитым шрифтом Cascadia.
/// </summary>
[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class MarkdownDiagramErrorLayoutTests
{
    private const double Tolerance = 0.5;

    private const string Source = "flowchart LR\n    A[Open file] --> B{Parse";

    // Сообщение Naiad о синтаксической ошибке — как его отдаёт MermaidParseException.
    private const string NaiadMessage = "Failed to parse flowchart: Parse error.\n    unexpected A\n    expected \"%%\", \"\n\", or \"\n\"\n    at line 2, col 5";

    private readonly AvaloniaHeadlessFixture _fixture;

    public MarkdownDiagramErrorLayoutTests(AvaloniaHeadlessFixture fixture) => _fixture = fixture;

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public Task FailedDiagramIsADashedFrameWithTheMessageAndTheSourceCode(string themeName)
    {
        return _fixture.Session.Dispatch(() =>
        {
            var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
            var (window, view) = Show(Failed(NaiadMessage), theme);
            var frame = ErrorFrame(view, "mm-md-diagram-error");
            var em = ReadingPreferences.Default.FontSize;

            var dash = Assert.Single(frame.Children.OfType<Rectangle>());
            Assert.Same(Resource(window, "MmKeyboardBorderBrush", theme), dash.Stroke);
            Assert.NotEmpty(dash.StrokeDashArray!);
            Assert.Equal(em * 0.3, dash.RadiusX, 3);

            var icon = Assert.Single(frame.GetVisualDescendants().OfType<LucideIcon>(), static icon => icon.Classes.Contains("mm-md-missing-icon"));
            Assert.Same(Resource(window, "LucideCircleAlertGeometry", theme), icon.Data);
            Assert.Same(Resource(window, "MmTextFaintBrush", theme), icon.Foreground);
            Assert.Equal(em * 1.7, icon.Width, 3);

            var title = Text(frame, "mm-md-diagram-error-title");
            Assert.Equal("Mermaid diagram could not be rendered", title.Text);
            Assert.Equal(em, title.FontSize);
            Assert.Same(Resource(window, "MmTextSoftBrush", theme), title.Foreground);

            // Из сообщения Naiad — первая строка и где парсер остановился, без списка ожидаемого.
            var message = Text(frame, "mm-md-diagram-error-message");
            Assert.Equal("Failed to parse flowchart: Parse error.\nunexpected A at line 2, col 5", message.Text);
            Assert.Equal(em * 0.857, message.FontSize, 3);
            Assert.Same(Resource(window, "MmTextFaintBrush", theme), message.Foreground);

            // Содержимое по центру, исходник — блоком кода во всю ширину рамки.
            var framePadding = Assert.IsType<Border>(frame.Children[1]).Padding;
            // Поле — от внутреннего края пунктира толщиной 1.
            Assert.Equal(new Thickness(em * 1.15 + 1, em * 1.5 + 1, em * 1.15 + 1, em * 1.15 + 1), framePadding);
            var code = CodeBlock(frame);
            Assert.Equal(frame.Bounds.Width - em * 2.3 - 2, code.Bounds.Width, Tolerance);
            Assert.Equal(em * 1.15, code.Margin.Top, 3);
            Assert.Equal("mermaid", Assert.Single(Assert.IsType<Grid>(code.Child).Children.OfType<TextBlock>()).Text);
            Assert.Single(Assert.IsType<Grid>(code.Child).Children.OfType<Button>());
            Assert.Contains(frame.GetVisualDescendants().OfType<TextBlock>(), static text => text.Text == Source);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task UnsupportedSvgUsesTheSameFrameWithItsOwnTitle()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var diagram = new MarkdownDiagramBlock(MarkdownDiagramKind.Mermaid, Source)
            {
                RenderResult = new DiagramRenderResult.Success("<html />"),
            };
            var (window, view) = Show(diagram, ThemeVariant.Light);
            var frame = ErrorFrame(view, "mm-md-diagram-svg-unsupported");

            Assert.Contains("SVG", Text(frame, "mm-md-diagram-error-title").Text, StringComparison.Ordinal);
            Assert.DoesNotContain(frame.GetVisualDescendants().OfType<TextBlock>(), static text => text.Classes.Contains("mm-md-diagram-error-message"));
            Assert.Single(frame.Children.OfType<Rectangle>());
            CodeBlock(frame);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task TitlesAreInTheInterfaceLanguageAndFollowItsChange()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var resources = AvaloniaApplication.Current!.Resources;
            var localization = new LocalizationService(AppLanguage.Russian);
            resources["Localization"] = localization;
            try
            {
                var (window, view) = Show(Failed("broken"), ThemeVariant.Light);
                Assert.Equal("Не удалось отрисовать диаграмму Mermaid", Text(ErrorFrame(view, "mm-md-diagram-error"), "mm-md-diagram-error-title").Text);

                localization.SetLanguage(AppLanguage.English);

                Assert.Equal("Mermaid diagram could not be rendered", Text(ErrorFrame(view, "mm-md-diagram-error"), "mm-md-diagram-error-title").Text);

                window.Close();
            }
            finally
            {
                resources.Remove("Localization");
            }
        }, CancellationToken.None);
    }

    [Fact]
    public Task EmptyDiagramIsExplainedInTheInterfaceLanguage()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var resources = AvaloniaApplication.Current!.Resources;
            resources["Localization"] = new LocalizationService(AppLanguage.Russian);
            try
            {
                var diagram = new MarkdownDiagramBlock(MarkdownDiagramKind.Mermaid, Source)
                {
                    RenderResult = new DiagramRenderResult.Failure("Mermaid produced an empty diagram.", Source)
                    {
                        Reason = DiagramFailureReason.EmptyDiagram,
                    },
                };
                var (window, view) = Show(diagram, ThemeVariant.Light);
                var frame = ErrorFrame(view, "mm-md-diagram-error");

                // Вместо английской диагностики рендерера — фраза на языке интерфейса.
                Assert.Equal("Диаграмма получилась пустой. Проверьте синтаксис.", Text(frame, "mm-md-diagram-error-message").Text);
                CodeBlock(frame);

                window.Close();
            }
            finally
            {
                resources.Remove("Localization");
            }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("syntax error", "syntax error")]
    [InlineData("  first line  \n\n  second line ", "first line\nsecond line")]
    [InlineData("Failed to parse: Parse error.\n    unexpected B\n    expected \"x\"", "Failed to parse: Parse error.\nunexpected B")]
    [InlineData("Failed to parse: Parse error.\n    expected \"x\"\n    at line 3, col 1", "Failed to parse: Parse error.\nat line 3, col 1")]
    [InlineData(NaiadMessage, "Failed to parse flowchart: Parse error.\nunexpected A at line 2, col 5")]
    [InlineData("", "")]
    public void SummaryKeepsTheFirstLineAndThePosition(string message, string expected)
        => Assert.Equal(expected, MarkdownDiagramBlockView.SummarizeMessage(message));

    private static MarkdownDiagramBlock Failed(string message)
        => new(MarkdownDiagramKind.Mermaid, Source)
        {
            RenderResult = new DiagramRenderResult.Failure(message, Source),
        };

    private static (Window Window, MarkdownDocumentView View) Show(MarkdownDiagramBlock diagram, ThemeVariant theme)
    {
        var view = new MarkdownDocumentView
        {
            ReadingPreferences = ReadingPreferences.Default,
            Document = new RenderedMarkdownDocument([diagram])
        };
        var window = ThemedTestWindow.Create(theme, view);
        window.Width = 700;
        window.Height = 500;
        window.Show();
        window.UpdateLayout();
        return (window, view);
    }

    private static Grid ErrorFrame(MarkdownDocumentView view, string stateClass)
    {
        var diagram = Assert.Single(view.GetVisualDescendants().OfType<MarkdownDiagramBlockView>());
        var frame = Assert.IsType<Grid>(diagram.Content);
        Assert.Contains("mm-md-missing", frame.Classes);
        Assert.Contains(stateClass, frame.Classes);
        return frame;
    }

    private static TextBlock Text(Grid frame, string className)
        => Assert.Single(frame.GetVisualDescendants().OfType<TextBlock>(), text => text.Classes.Contains(className));

    private static Border CodeBlock(Grid frame)
        => Assert.Single(frame.GetVisualDescendants().OfType<Border>(), static border => border.Classes.Contains("mm-md-codeblock"));

    private static object Resource(Window window, string key, ThemeVariant theme)
    {
        Assert.True(window.TryFindResource(key, theme, out var value), $"{key} is not defined in the theme");
        return value!;
    }
}
