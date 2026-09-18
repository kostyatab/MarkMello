using Avalonia;
using Avalonia.Controls;
using MarkMello.Domain;
using MarkMello.Presentation.Views;
using MarkMello.Presentation.Views.Markdown;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Ссылка под курсором определяется по символу под точкой, а не по ближайшей
/// каретке: клик по любой половине символа ссылки попадает в ссылку, клик по
/// соседнему символу — нет. По каретке правая половина последнего символа ссылки
/// уходила за неё, а правая половина символа перед ссылкой — в неё.
/// </summary>
[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class MarkdownLinkHitTestTests
{
    // «See docs now»: ссылка — «docs», символы 4..7.
    private const int LinkStart = 4;
    private const int LinkEnd = 8;

    private readonly AvaloniaHeadlessFixture _fixture;

    public MarkdownLinkHitTestTests(AvaloniaHeadlessFixture fixture) => _fixture = fixture;

    [Fact]
    public Task EitherHalfOfTheFirstAndLastLinkCharactersHitsTheLink()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var (window, fragment) = ShowLinkParagraph();

            foreach (var (character, half) in new[] { (LinkStart, 0.25), (LinkStart, 0.75), (LinkEnd - 1, 0.25), (LinkEnd - 1, 0.75) })
            {
                Assert.True(
                    fragment.TryGetLinkAt(PointIn(fragment, character, half), out var link),
                    $"Character {character} at {half:P0} of its width should hit the link.");
                Assert.Equal("https://example.com/docs", link.Url);
            }

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task EitherHalfOfTheCharactersAroundTheLinkMissesIt()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var (window, fragment) = ShowLinkParagraph();

            foreach (var (character, half) in new[] { (LinkStart - 1, 0.25), (LinkStart - 1, 0.75), (LinkEnd, 0.25), (LinkEnd, 0.75) })
            {
                Assert.False(
                    fragment.TryGetLinkAt(PointIn(fragment, character, half), out _),
                    $"Character {character} at {half:P0} of its width should miss the link.");
            }

            window.Close();
        }, CancellationToken.None);
    }

    private static (Window Window, MarkdownSelectionTextFragment Fragment) ShowLinkParagraph()
    {
        var view = new MarkdownDocumentView
        {
            ReadingPreferences = ReadingPreferences.Default,
            Document = new RenderedMarkdownDocument(
            [
                new MarkdownParagraphBlock(
                [
                    new MarkdownTextInline("See "),
                    new MarkdownLinkInline([new MarkdownTextInline("docs")], "https://example.com/docs", null),
                    new MarkdownTextInline(" now")
                ])
            ])
        };

        var window = new Window { Width = 600, Height = 400, Content = view };
        window.Show();
        window.UpdateLayout();

        var viewport = Assert.IsType<Border>(view.Content);
        var root = Assert.IsType<StackPanel>(viewport.Child);
        return (window, Assert.IsType<MarkdownSelectionTextFragment>(Assert.Single(root.Children)));
    }

    /// <summary>Точка на доле <paramref name="half"/> ширины символа, посередине строки.</summary>
    private static Point PointIn(MarkdownSelectionTextFragment fragment, int character, double half)
    {
        Assert.True(fragment.TryGetHorizontalExtentForLocalRange(character, character + 1, out var left, out var right));
        Assert.True(fragment.TryGetLineTopForLocalOffset(character, out var lineTop));
        return new Point(left + (right - left) * half, lineTop + 8);
    }
}
