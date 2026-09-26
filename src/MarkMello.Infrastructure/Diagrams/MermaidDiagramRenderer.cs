using MarkMello.Application.Abstractions;
using MarkMello.Domain;
using MarkMello.Infrastructure.Diagrams.Sequence;
using System.Xml;
using Naiad;

namespace MarkMello.Infrastructure.Diagrams;

/// <summary>
/// Mandatory <see cref="IDiagramRenderer"/> for <see cref="MarkdownDiagramKind.Mermaid"/>.
/// Wraps the Naiad managed library: in-process, no browser/Node/network/external
/// process — see ADR-0005 Decision 4 and the M0 spike note in
/// <c>tests/m0-naiad-spike.md</c>.
///
/// Failure policy: a backend exception raised for an individual diagram is
/// converted to <see cref="DiagramRenderResult.Failure"/> so one bad fence
/// does not crash the document. Composition errors (missing/duplicate
/// renderer) live outside this class and surface from
/// <c>DiagramRenderService</c>.
///
/// Sequence diagrams get their labels measured and their layout redone on
/// top of Naiad's SVG (ADR-0005 Decision 13); other diagram types pass
/// through as Naiad draws them.
/// </summary>
public sealed class MermaidDiagramRenderer : IDiagramRenderer
{
    private readonly IDiagramTextMeasurer _textMeasurer;

    public MermaidDiagramRenderer(IDiagramTextMeasurer textMeasurer)
    {
        ArgumentNullException.ThrowIfNull(textMeasurer);
        _textMeasurer = textMeasurer;
    }

    public MarkdownDiagramKind Kind => MarkdownDiagramKind.Mermaid;

    public DiagramRenderResult Render(DiagramRenderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var source = request.Source ?? string.Empty;

        try
        {
            // Options are built per call: edit-mode preview renders off the UI
            // thread, so nothing here may be shared mutable state.
            var options = new RenderOptions();
            var svg = Mermaid.Render(source, options);
            if (string.IsNullOrEmpty(svg))
            {
                return new DiagramRenderResult.Failure("Mermaid produced empty SVG output.", source);
            }

            svg = SequenceSvgLayout.Apply(source, svg, options, _textMeasurer);

            // Naiad 1.x parses some broken sources (an unclosed flowchart node,
            // for one) into an empty diagram instead of throwing. Showing that
            // as a blank picture would hide the source, so it is a failure.
            return HasDrawableContent(svg)
                ? new DiagramRenderResult.Success(svg)
                : new DiagramRenderResult.Failure("Mermaid produced an empty diagram.", source)
                {
                    Reason = DiagramFailureReason.EmptyDiagram,
                };
        }
        catch (MermaidException ex)
        {
            return new DiagramRenderResult.Failure(ex.Message, source);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // Backend safety net: Naiad may surface internal parser/layout
            // failures through non-Mermaid exception types. We do NOT swallow
            // composition or environment errors (those propagate as
            // OutOfMemoryException/StackOverflowException), only diagram-
            // specific failures.
            return new DiagramRenderResult.Failure(ex.Message, source);
        }
    }

    private static readonly HashSet<string> DrawableElements = new(StringComparer.Ordinal)
    {
        "rect", "circle", "ellipse", "line", "polyline", "polygon", "path", "text",
    };

    /// <summary>
    /// Whether the SVG draws anything: a shape or text outside
    /// <c>&lt;defs&gt;</c>, whose markers and gradients are only referenced.
    /// </summary>
    internal static bool HasDrawableContent(string svg)
    {
        try
        {
            using var reader = XmlReader.Create(new StringReader(svg), new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
            var defsDepth = -1;
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (defsDepth >= 0 && reader.Depth <= defsDepth)
                {
                    defsDepth = -1;
                }

                if (defsDepth < 0 && reader.LocalName == "defs" && !reader.IsEmptyElement)
                {
                    defsDepth = reader.Depth;
                }
                else if (defsDepth < 0 && DrawableElements.Contains(reader.LocalName))
                {
                    return true;
                }
            }

            return false;
        }
        catch (XmlException)
        {
            // Malformed markup is the SVG viewer's call, not this check's.
            return true;
        }
    }
}
