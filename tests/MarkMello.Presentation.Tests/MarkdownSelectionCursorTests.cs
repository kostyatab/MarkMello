using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using MarkMello.Domain;
using MarkMello.Presentation.Views;
using MarkMello.Presentation.Views.Markdown;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// На время нажатия документ захватывает указатель, и Avalonia показывает курсор
/// документа, а не фрагмента под мышью. Без собственного курсора документа
/// протягивание выделения шло со стрелкой вместо текстового курсора.
/// </summary>
[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class MarkdownSelectionCursorTests
{
    private readonly AvaloniaHeadlessFixture _fixture;

    public MarkdownSelectionCursorTests(AvaloniaHeadlessFixture fixture) => _fixture = fixture;

    [Fact]
    public Task DraggingSelectionShowsTextCursorUntilRelease()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var (window, view, fragment) = ShowParagraph();
            var start = PointOnCharacter(window, fragment, 1);
            var end = PointOnCharacter(window, fragment, 12);

            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(end);
            Assert.Equal(nameof(StandardCursorType.Ibeam), view.Cursor?.ToString());

            // Ниже абзаца — пустое поле документа, где своего курсора нет.
            window.MouseMove(new Point(end.X, end.Y + 100));
            Assert.Equal(nameof(StandardCursorType.Ibeam), view.Cursor?.ToString());

            window.MouseUp(end, MouseButton.Left);
            Assert.Null(view.Cursor);

            window.Close();
        }, CancellationToken.None);
    }

    /// <summary>
    /// Пересборка посреди протягивания сбрасывает нажатие — и вместе с ним
    /// отпускает захват, иначе до отпускания кнопки под мышью был бы только
    /// документ, а элементы внутри него не получали бы наведения.
    /// </summary>
    [Fact]
    public Task RebuildWhileDraggingReleasesThePointer()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var (window, view, fragment) = ShowParagraph();
            var start = PointOnCharacter(window, fragment, 1);
            var end = PointOnCharacter(window, fragment, 12);
            var viewport = Assert.IsType<Border>(view.Content);

            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(end);
            Assert.False(viewport.IsPointerOver);

            view.Document = ParagraphDocument();
            window.UpdateLayout();
            window.MouseMove(end);

            Assert.Null(view.Cursor);
            Assert.True(viewport.IsPointerOver);

            window.MouseUp(end, MouseButton.Left);
            window.Close();
        }, CancellationToken.None);
    }

    /// <summary>Середина символа <paramref name="character"/> в координатах окна.</summary>
    private static Point PointOnCharacter(Window window, MarkdownSelectionTextFragment fragment, int character)
    {
        Assert.True(fragment.TryGetHorizontalExtentForLocalRange(character, character + 1, out var left, out var right));
        Assert.True(fragment.TryGetLineTopForLocalOffset(character, out var lineTop));
        return fragment.TranslatePoint(new Point((left + right) / 2, lineTop + 8), window)!.Value;
    }

    private static (Window Window, MarkdownDocumentView View, MarkdownSelectionTextFragment Fragment) ShowParagraph()
    {
        var view = new MarkdownDocumentView
        {
            ReadingPreferences = ReadingPreferences.Default,
            Document = ParagraphDocument()
        };

        var window = new Window { Width = 600, Height = 400, Content = view };
        window.Show();
        window.UpdateLayout();

        var viewport = Assert.IsType<Border>(view.Content);
        var root = Assert.IsType<StackPanel>(viewport.Child);
        return (window, view, Assert.IsType<MarkdownSelectionTextFragment>(Assert.Single(root.Children)));
    }

    private static RenderedMarkdownDocument ParagraphDocument()
        => new(
        [
            new MarkdownParagraphBlock(
            [
                new MarkdownTextInline("See "),
                new MarkdownLinkInline([new MarkdownTextInline("docs")], "https://example.com/docs", null),
                new MarkdownTextInline(" now and then some more text")
            ])
        ]);
}
