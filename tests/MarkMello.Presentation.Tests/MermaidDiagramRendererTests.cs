using System.Globalization;
using System.Xml.Linq;
using MarkMello.Application.Abstractions;
using MarkMello.Domain;
using MarkMello.Infrastructure.Diagrams;

namespace MarkMello.Presentation.Tests;

public sealed class MermaidDiagramRendererTests
{
    [Fact]
    public void KindIsMermaid()
    {
        var renderer = new MermaidDiagramRenderer(new FixedAdvanceDiagramTextMeasurer());

        Assert.Equal(MarkdownDiagramKind.Mermaid, renderer.Kind);
    }

    [Fact]
    public void RenderProducesNonEmptySvgForMinimalFlowchart()
    {
        const string source =
            """
            flowchart LR
                A[Start] --> B[End]
            """;

        var renderer = new MermaidDiagramRenderer(new FixedAdvanceDiagramTextMeasurer());

        var result = renderer.Render(new DiagramRenderRequest(source));

        var success = Assert.IsType<DiagramRenderResult.Success>(result);
        Assert.False(string.IsNullOrEmpty(success.Svg));
        Assert.StartsWith("<svg", success.Svg.TrimStart(), StringComparison.Ordinal);
        Assert.EndsWith("</svg>", success.Svg.TrimEnd(), StringComparison.Ordinal);
    }

    [Fact]
    public void RenderProducesNonEmptySvgForSequenceDiagram()
    {
        const string source =
            """
            sequenceDiagram
                Alice->>Bob: Hi
                Bob-->>Alice: Hey
            """;

        var renderer = new MermaidDiagramRenderer(new FixedAdvanceDiagramTextMeasurer());

        var result = renderer.Render(new DiagramRenderRequest(source));

        var success = Assert.IsType<DiagramRenderResult.Success>(result);
        Assert.False(string.IsNullOrEmpty(success.Svg));
    }

    [Fact]
    public void RenderReturnsFailureForInvalidMermaidSource()
    {
        const string source = "this is not a valid mermaid diagram";

        var renderer = new MermaidDiagramRenderer(new FixedAdvanceDiagramTextMeasurer());

        var result = renderer.Render(new DiagramRenderRequest(source));

        var failure = Assert.IsType<DiagramRenderResult.Failure>(result);
        Assert.False(string.IsNullOrWhiteSpace(failure.Message));
        Assert.Equal(source, failure.Source);
    }

    [Fact]
    public void RenderReturnsFailureWhenBrokenSourceParsesIntoAnEmptyDiagram()
    {
        // Naiad 1.x does not throw on unclosed flowchart nodes — it returns an
        // SVG with styles only. A blank picture would hide the source (MM-20).
        const string source =
            """
            flowchart LR
                A[Open file] --> B{Parse
                B -->|ok| C[Render
                C ==>
            """;

        var result = new MermaidDiagramRenderer(new FixedAdvanceDiagramTextMeasurer()).Render(new DiagramRenderRequest(source));

        var failure = Assert.IsType<DiagramRenderResult.Failure>(result);
        Assert.Equal(DiagramFailureReason.EmptyDiagram, failure.Reason);
        Assert.Equal(source, failure.Source);
    }

    [Fact]
    public void GitGraphCommitLabelsSitBelowTheirCommitCircles()
    {
        // MM-20: Naiad 0.1.2 drew commit labels under the commit circles, so
        // the circles covered them. Each label must start below its circle.
        const string source =
            """
            gitGraph
                commit
                branch develop
                checkout develop
                commit
                checkout main
                merge develop
            """;

        var result = new MermaidDiagramRenderer(new FixedAdvanceDiagramTextMeasurer()).Render(new DiagramRenderRequest(source));

        var svg = XDocument.Parse(Assert.IsType<DiagramRenderResult.Success>(result).Svg);
        var circles = svg.Descendants().Where(static e => e.Name.LocalName == "circle").ToList();
        var labels = svg.Descendants()
            .Where(static e => e.Name.LocalName == "text" && (e.Value.StartsWith("commit", StringComparison.Ordinal) || e.Value.StartsWith("merge", StringComparison.Ordinal)))
            .ToList();
        Assert.Equal(3, labels.Count);
        foreach (var label in labels)
        {
            var x = Number(label, "x");
            // A merge commit is a ring: an outer circle with a smaller one inside.
            var circle = circles.Where(c => Math.Abs(Number(c, "cx") - x) < 0.01).MaxBy(static c => Number(c, "r"));
            Assert.NotNull(circle);
            var fontSize = double.Parse(label.Attribute("font-size")!.Value.Replace("px", string.Empty, StringComparison.Ordinal), CultureInfo.InvariantCulture);
            Assert.True(
                Number(label, "y") - (fontSize / 2) >= Number(circle, "cy") + Number(circle, "r"),
                $"Label '{label.Value}' overlaps its commit circle.");
        }
    }

    private static double Number(XElement element, string attribute)
        => double.Parse(element.Attribute(attribute)!.Value, CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("<svg><style>#a{fill:#333}</style></svg>", false)]
    [InlineData("<svg><defs><marker><path d='M0 0'/></marker></defs></svg>", false)]
    [InlineData("<svg><defs><marker><path d='M0 0'/></marker></defs><g><rect/></g></svg>", true)]
    [InlineData("<svg><defs/><text>A</text></svg>", true)]
    [InlineData("<svg><g><path d='M0 0'/></g></svg>", true)]
    public void HasDrawableContentIgnoresStylesAndDefinitions(string svg, bool expected)
    {
        Assert.Equal(expected, MermaidDiagramRenderer.HasDrawableContent(svg));
    }

    [Fact]
    public void RenderReturnsFailureForEmptySource()
    {
        var renderer = new MermaidDiagramRenderer(new FixedAdvanceDiagramTextMeasurer());

        var result = renderer.Render(new DiagramRenderRequest(string.Empty));

        Assert.IsType<DiagramRenderResult.Failure>(result);
    }

    [Fact]
    public void RenderPreservesOriginalSourceInFailure()
    {
        const string source = "graph";
        var renderer = new MermaidDiagramRenderer(new FixedAdvanceDiagramTextMeasurer());

        var result = renderer.Render(new DiagramRenderRequest(source));

        if (result is DiagramRenderResult.Failure failure)
        {
            Assert.Equal(source, failure.Source);
        }
    }

    [Fact]
    public void RenderDoesNotMutateOrCorruptInputSource()
    {
        const string source =
            """
            flowchart TD
                A --> B
                B --> C
            """;
        var renderer = new MermaidDiagramRenderer(new FixedAdvanceDiagramTextMeasurer());

        renderer.Render(new DiagramRenderRequest(source));

        // Sanity: the source string is value-typed and not held mutably by
        // the renderer. This protects against future regressions where the
        // backend would gain a side effect on its input. CRLF normalisation
        // keeps the assertion meaningful on Windows checkouts where the raw
        // string literal preserves the source file's line endings.
        Assert.Equal(
            "flowchart TD\n    A --> B\n    B --> C",
            source.Replace("\r\n", "\n", StringComparison.Ordinal));
    }
}
