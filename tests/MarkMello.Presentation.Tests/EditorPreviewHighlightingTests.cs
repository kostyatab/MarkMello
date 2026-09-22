using MarkMello.Application.Abstractions;
using MarkMello.Application.UseCases;
using MarkMello.Domain;
using MarkMello.Infrastructure.Markdown;
using MarkMello.Presentation.ViewModels;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Подсветка кода в превью режима правки (ADR-0010 §4): разбор — в фоновом
/// рендере превью, неизменившиеся блоки берут цвета из кэша сразу.
/// </summary>
public sealed class EditorPreviewHighlightingTests
{
    private const string Markdown = "# Title\n\n```cs\nvar x = 1;\n```";

    [Fact]
    public void PreviewIsHighlightedByTheBackgroundRender()
    {
        var engine = new CountingHighlighter();
        var scheduler = new ManualEditorPreviewScheduler();

        var session = CreateSession(Markdown, scheduler, new HighlightCodeBlocksUseCase(engine));

        Assert.Null(CodeBlock(session.RenderedPreview).Tokens);
        Assert.Equal(0, engine.Calls);
        Assert.True(scheduler.HasPendingRender);

        scheduler.Flush();

        Assert.NotNull(CodeBlock(session.RenderedPreview).Tokens);
        Assert.Equal(1, engine.Calls);
    }

    [Fact]
    public void EditingOutsideTheCodeKeepsItsColorsWithoutParsingAgain()
    {
        var engine = new CountingHighlighter();
        var scheduler = new ManualEditorPreviewScheduler();
        var session = CreateSession(Markdown, scheduler, new HighlightCodeBlocksUseCase(engine));
        scheduler.Flush();

        session.SourceText = Markdown.Replace("# Title", "# Other title", StringComparison.Ordinal);
        scheduler.Flush();

        Assert.NotNull(CodeBlock(session.RenderedPreview).Tokens);
        Assert.Equal(1, engine.Calls);
    }

    [Fact]
    public void SynchronousRefreshTakesColorsFromTheCache()
    {
        var engine = new CountingHighlighter();
        var scheduler = new ManualEditorPreviewScheduler();
        var session = CreateSession(Markdown, scheduler, new HighlightCodeBlocksUseCase(engine));
        scheduler.Flush();

        var path = Path.Combine(Path.GetTempPath(), "MarkMello.Tests", "one.md");
        session.ApplySavedDocument(new MarkdownSource(path, "one.md", Markdown));

        Assert.NotNull(CodeBlock(session.RenderedPreview).Tokens);
        Assert.False(scheduler.HasPendingRender);
    }

    private static EditorSessionViewModel CreateSession(
        string markdown,
        ManualEditorPreviewScheduler scheduler,
        HighlightCodeBlocksUseCase highlightCodeBlocks)
        => new(
            "one.md",
            markdown,
            ReadingPreferences.Default,
            new RenderMarkdownDocumentUseCase(new MarkdigMarkdownDocumentRenderer(), new FakeDiagramRenderService()),
            imageSourceResolver: null,
            previewScheduler: scheduler,
            highlightCodeBlocks: highlightCodeBlocks);

    private static MarkdownCodeBlock CodeBlock(RenderedMarkdownDocument document)
        => document.Blocks.OfType<MarkdownCodeBlock>().Single();

    private sealed class CountingHighlighter : ICodeHighlighter
    {
        public int Calls { get; private set; }

        public IReadOnlyList<MarkdownCodeToken>? Highlight(
            string language,
            string code,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            Calls++;
            return [new MarkdownCodeToken(0, 3, MarkdownCodeTokenKind.Keyword)];
        }
    }
}
