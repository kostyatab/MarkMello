using Avalonia.Controls;
using MarkMello.Domain;
using MarkMello.Presentation.Views;
using MarkMello.Presentation.Views.Markdown;

namespace MarkMello.Presentation.Tests;

[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class MarkdownDiagramBlockViewTests
{
    private const string MinimalSvg =
        """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><rect width="10" height="10" fill="red"/></svg>""";

    private readonly AvaloniaHeadlessFixture _fixture;

    public MarkdownDiagramBlockViewTests(AvaloniaHeadlessFixture fixture) => _fixture = fixture;

    [Fact]
    public Task DocumentWithDiagramSuccessProducesDiagramBlockView()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var diagram = new MarkdownDiagramBlock(
                MarkdownDiagramKind.Mermaid,
                "flowchart LR\nA --> B")
            {
                RenderResult = new DiagramRenderResult.Success(MinimalSvg),
            };

            var rendered = RenderToVisualTree(diagram);

            Assert.IsType<MarkdownDiagramBlockView>(rendered);
        }, CancellationToken.None);
    }

    [Fact]
    public Task DocumentWithDiagramFailureStillProducesDiagramBlockView()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var diagram = new MarkdownDiagramBlock(
                MarkdownDiagramKind.Mermaid,
                "broken")
            {
                RenderResult = new DiagramRenderResult.Failure("invalid syntax", "broken"),
            };

            var rendered = RenderToVisualTree(diagram);

            // The view is responsible for both success and failure visuals — the
            // failure path must not fall through to the document's generic
            // BuildFallback (which would surface the diagram as a plain
            // TextBlock).
            Assert.IsType<MarkdownDiagramBlockView>(rendered);
        }, CancellationToken.None);
    }

    [Fact]
    public Task DocumentWithUnmaterializedDiagramStillProducesDiagramBlockView()
    {
        return _fixture.Session.Dispatch(() =>
        {
            // Defensive: if the use case did not materialize a result, the view
            // must NOT silently render the diagram as a code block. The view
            // produces an error visual so the composition gap is visible.
            var diagram = new MarkdownDiagramBlock(
                MarkdownDiagramKind.Mermaid,
                "flowchart LR\nA --> B");

            var rendered = RenderToVisualTree(diagram);

            Assert.IsType<MarkdownDiagramBlockView>(rendered);
        }, CancellationToken.None);
    }

    [Fact]
    public void DiagramBlockDoesNotContributeToDocumentText()
    {
        var diagram = new MarkdownDiagramBlock(
            MarkdownDiagramKind.Mermaid,
            "flowchart LR\nA --> B")
        {
            RenderResult = new DiagramRenderResult.Success(MinimalSvg),
        };
        var paragraph = new MarkdownParagraphBlock([new MarkdownTextInline("after")]);

        var document = new RenderedMarkdownDocument([diagram, paragraph]);
        var textMap = MarkdownDocumentTextMap.Create(document);

        // The diagram source must not appear in the document's selectable text
        // (ADR-0005 §8); only the surrounding paragraph contributes content.
        Assert.DoesNotContain("flowchart", textMap.Text, StringComparison.Ordinal);
        Assert.Contains("after", textMap.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void FailedDiagramSourceAlsoStaysOutOfDocumentText()
    {
        // Mirror of the success case: even when the renderer surfaces a
        // failure (and the source is shown inside the error block as
        // readable code-style text), the source must not be folded back
        // into the document-wide selection stream. M6 keeps diagrams
        // visually present but never lets their source pollute Select All.
        var diagram = new MarkdownDiagramBlock(
            MarkdownDiagramKind.Mermaid,
            "flowchart LR\nbroken")
        {
            RenderResult = new DiagramRenderResult.Failure("syntax error", "flowchart LR\nbroken"),
        };
        var paragraph = new MarkdownParagraphBlock([new MarkdownTextInline("between")]);

        var document = new RenderedMarkdownDocument([diagram, paragraph]);
        var textMap = MarkdownDocumentTextMap.Create(document);

        Assert.DoesNotContain("flowchart", textMap.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("broken", textMap.Text, StringComparison.Ordinal);
        Assert.Contains("between", textMap.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void UnmaterializedDiagramSourceAlsoStaysOutOfDocumentText()
    {
        // Defensive case: even when the application composition is broken
        // and the use case never materialized a RenderResult, the text map
        // must continue to exclude the diagram source. The presentation
        // surfaces this as an error visual (see
        // <see cref="DocumentWithUnmaterializedDiagramStillProducesDiagramBlockView"/>),
        // but the document text stream is unaffected.
        var diagram = new MarkdownDiagramBlock(
            MarkdownDiagramKind.Mermaid,
            "flowchart LR\nA --> B");

        var paragraph = new MarkdownParagraphBlock([new MarkdownTextInline("end-of-doc")]);
        var document = new RenderedMarkdownDocument([diagram, paragraph]);
        var textMap = MarkdownDocumentTextMap.Create(document);

        Assert.DoesNotContain("flowchart", textMap.Text, StringComparison.Ordinal);
        Assert.Contains("end-of-doc", textMap.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void NestedDiagramInsideQuoteDoesNotBreakTextMapNorAdoptSource()
    {
        // M6 acceptance: existing text map tests must continue to pass
        // when a diagram is nested inside other container blocks. The
        // diagram skips its own text contribution but still separates
        // surrounding text so adjacent paragraphs remain readable.
        var quote = new MarkdownQuoteBlock(
        [
            new MarkdownParagraphBlock([new MarkdownTextInline("note above")]),
            new MarkdownDiagramBlock(MarkdownDiagramKind.Mermaid, "flowchart LR\nA --> B")
            {
                RenderResult = new DiagramRenderResult.Success(MinimalSvg),
            },
            new MarkdownParagraphBlock([new MarkdownTextInline("note below")]),
        ]);
        var document = new RenderedMarkdownDocument([quote]);

        var textMap = MarkdownDocumentTextMap.Create(document);

        Assert.DoesNotContain("flowchart", textMap.Text, StringComparison.Ordinal);
        Assert.Contains("note above", textMap.Text, StringComparison.Ordinal);
        Assert.Contains("note below", textMap.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void NestedDiagramInsideListDoesNotBreakTextMap()
    {
        var list = new MarkdownListBlock(IsOrdered: false,
        [
            new MarkdownListItem(
            [
                new MarkdownParagraphBlock([new MarkdownTextInline("first item")]),
                new MarkdownDiagramBlock(MarkdownDiagramKind.Mermaid, "flowchart LR\nC --> D")
                {
                    RenderResult = new DiagramRenderResult.Success(MinimalSvg),
                },
            ]),
            new MarkdownListItem(
            [
                new MarkdownParagraphBlock([new MarkdownTextInline("second item")]),
            ]),
        ]);
        var document = new RenderedMarkdownDocument([list]);

        var textMap = MarkdownDocumentTextMap.Create(document);

        Assert.DoesNotContain("flowchart", textMap.Text, StringComparison.Ordinal);
        Assert.Contains("first item", textMap.Text, StringComparison.Ordinal);
        Assert.Contains("second item", textMap.Text, StringComparison.Ordinal);
    }

    [Fact]
    public Task DiagramBlockWithSourceSpanRegistersScrollSyncAnchor()
    {
        return _fixture.Session.Dispatch(() =>
        {
            // M6 acceptance: the diagram's source span has to remain available
            // to edit-mode scroll synchronization, alongside paragraphs and
            // code blocks. Without a registered anchor, jumping from the
            // editor cursor to the preview would skip diagrams entirely.
            var diagram = new MarkdownDiagramBlock(
                MarkdownDiagramKind.Mermaid,
                "flowchart LR\nA --> B")
            {
                SourceSpan = new MarkdownSourceSpan(5, 7),
                RenderResult = new DiagramRenderResult.Success(MinimalSvg),
            };
            var document = new RenderedMarkdownDocument([diagram]);

            var view = new MarkdownDocumentView
            {
                Document = document,
                ReadingPreferences = ReadingPreferences.Default,
            };

            var spans = view.EnumerateRegisteredSourceSpans();
            Assert.Contains(spans, span => span.StartLine == 5 && span.EndLine == 7);
        }, CancellationToken.None);
    }

    [Fact]
    public Task FailedDiagramBlockStillRegistersScrollSyncAnchor()
    {
        return _fixture.Session.Dispatch(() =>
        {
            // Even when the renderer surfaces a failure visual, the block
            // still occupies a source-span slot so scroll sync resolves to
            // the error view rather than silently snapping to the next block.
            var diagram = new MarkdownDiagramBlock(
                MarkdownDiagramKind.Mermaid,
                "flowchart LR\nbroken")
            {
                SourceSpan = new MarkdownSourceSpan(12, 15),
                RenderResult = new DiagramRenderResult.Failure("syntax error", "flowchart LR\nbroken"),
            };
            var document = new RenderedMarkdownDocument([diagram]);

            var view = new MarkdownDocumentView
            {
                Document = document,
                ReadingPreferences = ReadingPreferences.Default,
            };

            var spans = view.EnumerateRegisteredSourceSpans();
            Assert.Contains(spans, span => span.StartLine == 12 && span.EndLine == 15);
        }, CancellationToken.None);
    }

    private static Control RenderToVisualTree(MarkdownDiagramBlock diagram)
    {
        var document = new RenderedMarkdownDocument([diagram]);
        var view = new MarkdownDocumentView
        {
            Document = document,
            ReadingPreferences = ReadingPreferences.Default,
        };

        var viewport = Assert.IsType<Border>(view.Content);
        var root = Assert.IsType<StackPanel>(viewport.Child);
        return Assert.Single(root.Children);
    }
}
