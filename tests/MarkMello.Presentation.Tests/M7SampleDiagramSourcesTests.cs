using System.Text;
using MarkMello.Application.Abstractions;
using MarkMello.Domain;
using MarkMello.Infrastructure.Diagrams;
using MarkMello.Presentation.Views.Markdown;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// M7 regression protection: every Mermaid sample that ships in
/// <c>sample.md</c> must round-trip through the production
/// <see cref="MermaidDiagramRenderer"/> and be accepted by the native
/// <see cref="AotSafeSvgImage"/> path. Without this guard, a future
/// refactor could quietly break a documented example and only surface
/// the issue during manual UI verification.
/// </summary>
public sealed class M7SampleDiagramSourcesTests
{
    private const string FlowchartSource =
        "flowchart LR\n"
        + "    A[Open file] --> B{Mermaid fence?}\n"
        + "    B -->|yes| C[Render diagram]\n"
        + "    B -->|no| D[Render text]";

    private const string SequenceSource =
        "sequenceDiagram\n"
        + "    User->>MarkMello: Open sample.md\n"
        + "    MarkMello->>Naiad: Render Mermaid\n"
        + "    Naiad-->>MarkMello: SVG\n"
        + "    MarkMello-->>User: Diagram";

    private const string StateSource =
        "stateDiagram-v2\n"
        + "    [*] --> Reading\n"
        + "    Reading --> Editing: Ctrl+E\n"
        + "    Editing --> Reading: Ctrl+E\n"
        + "    Reading --> [*]";

    [Theory]
    [InlineData(FlowchartSource)]
    [InlineData(SequenceSource)]
    [InlineData(StateSource)]
    public void SampleDiagramRendersThroughTheRealMermaidPipeline(string source)
    {
        var renderer = new MermaidDiagramRenderer(new FixedAdvanceDiagramTextMeasurer());
        var result = renderer.Render(new DiagramRenderRequest(source));

        var success = Assert.IsType<DiagramRenderResult.Success>(result);
        Assert.True(
            AotSafeSvgImage.TryLoad(Encoding.UTF8.GetBytes(success.Svg), out var image),
            "sample.md diagram source produced an SVG that the AOT-safe viewer cannot consume.");
        Assert.True(image.DrawableCount > 0);
    }
}
