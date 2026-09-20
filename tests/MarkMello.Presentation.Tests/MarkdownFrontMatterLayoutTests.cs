using Avalonia.Controls;
using Avalonia.Styling;
using MarkMello.Domain;
using MarkMello.Presentation.Views;
using MarkMello.Presentation.Views.Markdown;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// MM-48: front matter — отдельный тип блока, но рисуется он прежней табличной
/// раскладкой: строки без шапки, ключ жирным. Вид не должен отличаться от MM-46,
/// когда метаданные были <see cref="MarkdownTableBlock"/> с пустым заголовком.
/// </summary>
[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class MarkdownFrontMatterLayoutTests
{
    private readonly AvaloniaHeadlessFixture _fixture;

    public MarkdownFrontMatterLayoutTests(AvaloniaHeadlessFixture fixture) => _fixture = fixture;

    [Fact]
    public Task FrontMatterIsDrawnAsATableWithoutAHeaderRow()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(Document());
            var window = Show(view);
            var panel = Table(view);

            Assert.Equal(2, panel.ColumnCount);
            Assert.Equal(2, panel.Children.Count / panel.ColumnCount);

            // Строки заголовка нет: все ячейки — обычные.
            Assert.All(panel.Children, child =>
                Assert.Contains("mm-md-table-cell", Assert.IsType<Border>(child).Classes));

            Assert.Equal("id", Content(panel, 0, 0).StyledText.Text);
            Assert.Equal("MM-48", Content(panel, 0, 1).StyledText.Text);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task TheKeyIsBoldAndTheValueIsPlainText()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(Document());
            var window = Show(view);
            var panel = Table(view);

            var key = Content(panel, 0, 0).StyledText;
            var span = Assert.Single(key.Spans);
            Assert.True(span.Style.IsBold);
            Assert.Equal(new DocumentTextRange(0, key.Text.Length), span.Range);

            Assert.Empty(Content(panel, 0, 1).StyledText.Spans);

            window.Close();
        }, CancellationToken.None);
    }

    /// <summary>
    /// Компаратор блоков сравнивает front matter по содержимому: неизменившийся
    /// документ переиспользует готовый контрол, а не пересобирает его.
    /// </summary>
    [Fact]
    public Task AnUnchangedDocumentReusesTheFrontMatterControl()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = CreateView(Document());
            var window = Show(view);
            var before = Assert.Single(Root(view).Children);

            view.Document = Document();
            var afterSameDocument = Assert.Single(Root(view).Children);

            view.Document = new RenderedMarkdownDocument(
            [
                new MarkdownFrontMatterBlock([new MarkdownFrontMatterEntry("id", "MM-49")])
            ]);
            var afterEdit = Assert.Single(Root(view).Children);

            Assert.Same(before, afterSameDocument);
            Assert.NotSame(before, afterEdit);

            window.Close();
        }, CancellationToken.None);
    }

    private static RenderedMarkdownDocument Document()
        => new(
        [
            new MarkdownFrontMatterBlock(
            [
                new MarkdownFrontMatterEntry("id", "MM-48"),
                new MarkdownFrontMatterEntry("empty", string.Empty)
            ])
        ]);

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

    private static StackPanel Root(MarkdownDocumentView view)
    {
        var viewport = Assert.IsType<Border>(view.Content);
        return Assert.IsType<StackPanel>(viewport.Child);
    }

    private static MarkdownTablePanel Table(MarkdownDocumentView view)
    {
        var block = Assert.IsType<Border>(Assert.Single(Root(view).Children));
        Assert.Contains("mm-md-table", block.Classes);
        return Assert.IsType<MarkdownTableHost>(block.Child).Panel;
    }

    private static MarkdownSelectionTextFragment Content(MarkdownTablePanel panel, int row, int column)
        => Assert.IsType<MarkdownSelectionTextFragment>(
            Assert.IsType<Border>(panel.Children[(row * panel.ColumnCount) + column]).Child);
}
