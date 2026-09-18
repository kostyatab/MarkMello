using MarkMello.Application.Abstractions;
using MarkMello.Application.UseCases;
using MarkMello.Domain;
using MarkMello.Infrastructure.Markdown;

namespace MarkMello.Presentation.Tests;

public sealed class RenderMarkdownDocumentUseCaseTests
{
    [Fact]
    public void ExecuteMaterializesRenderResultForTopLevelDiagramBlock()
    {
        const string markdown = """
            # Heading

            ```mermaid
            flowchart LR
                A --> B
            ```
            """;

        var diagramService = new FakeDiagramRenderService();
        var useCase = new RenderMarkdownDocumentUseCase(
            new MarkdigMarkdownDocumentRenderer(),
            diagramService);

        var document = useCase.Execute(markdown);

        Assert.Collection(
            document.Blocks,
            block => Assert.IsType<MarkdownHeadingBlock>(block),
            block =>
            {
                var diagram = Assert.IsType<MarkdownDiagramBlock>(block);
                Assert.Equal(MarkdownDiagramKind.Mermaid, diagram.Kind);
                var success = Assert.IsType<DiagramRenderResult.Success>(diagram.RenderResult);
                Assert.Contains("flowchart LR", success.Svg, StringComparison.Ordinal);
            });
    }

    [Fact]
    public void ExecutePropagatesDiagramFailureIntoRenderResult()
    {
        const string markdown = """
            ```mermaid
            broken
            ```
            """;

        var diagramService = new FakeDiagramRenderService
        {
            Handler = (kind, source) => new DiagramRenderResult.Failure($"boom for {kind}", source),
        };
        var useCase = new RenderMarkdownDocumentUseCase(
            new MarkdigMarkdownDocumentRenderer(),
            diagramService);

        var document = useCase.Execute(markdown);

        var diagram = Assert.IsType<MarkdownDiagramBlock>(Assert.Single(document.Blocks));
        var failure = Assert.IsType<DiagramRenderResult.Failure>(diagram.RenderResult);
        Assert.Equal("boom for Mermaid", failure.Message);
        Assert.Equal("broken", failure.Source);
    }

    [Fact]
    public void ExecuteDoesNotInvokeServiceForDocumentsWithoutDiagrams()
    {
        const string markdown = """
            # Heading

            Paragraph text.

            ```csharp
            var x = 1;
            ```
            """;

        var serviceCalls = 0;
        var diagramService = new FakeDiagramRenderService
        {
            Handler = (kind, source) =>
            {
                serviceCalls++;
                return new DiagramRenderResult.Success("<svg/>");
            },
        };
        var useCase = new RenderMarkdownDocumentUseCase(
            new MarkdigMarkdownDocumentRenderer(),
            diagramService);

        var document = useCase.Execute(markdown);

        Assert.Equal(0, serviceCalls);
        Assert.Equal(3, document.Blocks.Count);
    }

    [Fact]
    public void ExecuteMaterializesNestedDiagramInsideBlockQuote()
    {
        const string markdown = """
            > ```mermaid
            > flowchart LR
            >     A --> B
            > ```
            """;

        var diagramService = new FakeDiagramRenderService();
        var useCase = new RenderMarkdownDocumentUseCase(
            new MarkdigMarkdownDocumentRenderer(),
            diagramService);

        var document = useCase.Execute(markdown);

        var quote = Assert.IsType<MarkdownQuoteBlock>(Assert.Single(document.Blocks));
        var diagram = Assert.IsType<MarkdownDiagramBlock>(Assert.Single(quote.Blocks));
        Assert.IsType<DiagramRenderResult.Success>(diagram.RenderResult);
    }

    [Fact]
    public void ExecuteKeepsTaskListStateWhenMaterializingDiagramsInsideAList()
    {
        const string markdown = """
            - [x] done

              ```mermaid
              flowchart LR
                  A --> B
              ```
            - [ ] open
            - regular
            """;

        var useCase = new RenderMarkdownDocumentUseCase(
            new MarkdigMarkdownDocumentRenderer(),
            new FakeDiagramRenderService());

        var document = useCase.Execute(markdown);

        var list = Assert.IsType<MarkdownListBlock>(Assert.Single(document.Blocks));
        Assert.Equal([true, false, null], list.Items.Select(item => item.IsChecked));
        var diagram = Assert.IsType<MarkdownDiagramBlock>(list.Items[0].Blocks[1]);
        Assert.IsType<DiagramRenderResult.Success>(diagram.RenderResult);
    }

    [Fact]
    public void ExecuteRoutesEachDiagramToServiceWithCorrectSource()
    {
        const string markdown = """
            ```mermaid
            flowchart LR
                A --> B
            ```

            ```mermaid
            sequenceDiagram
                Alice->>Bob: Hi
            ```
            """;

        var received = new List<string>();
        var diagramService = new FakeDiagramRenderService
        {
            Handler = (kind, source) =>
            {
                received.Add(source);
                return new DiagramRenderResult.Success("<svg/>");
            },
        };
        var useCase = new RenderMarkdownDocumentUseCase(
            new MarkdigMarkdownDocumentRenderer(),
            diagramService);

        useCase.Execute(markdown);

        Assert.Equal(2, received.Count);
        Assert.Contains("flowchart LR", received[0], StringComparison.Ordinal);
        Assert.Contains("sequenceDiagram", received[1], StringComparison.Ordinal);
    }
}
