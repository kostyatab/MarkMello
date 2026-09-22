namespace MarkMello.Domain;

/// <summary>
/// UI-agnostic result of rendering a <see cref="MarkdownDiagramBlock"/>
/// source through the diagram service. Carries either a rendered SVG payload
/// or a controlled failure describing why this specific diagram could not be
/// rendered. The success path is the only one that yields drawable output;
/// failures are not used to mask composition errors (missing renderer for a
/// supported dialect must surface as a composition exception, not as a
/// <see cref="Failure"/>).
/// </summary>
public abstract record DiagramRenderResult
{
    /// <summary>Successful diagram render — SVG payload ready for display.</summary>
    public sealed record Success(string Svg) : DiagramRenderResult;

    /// <summary>
    /// Controlled failure for an individual diagram. <paramref name="Source"/>
    /// is the original diagram source so the viewer can fall back to a
    /// readable error block showing what the author wrote.
    /// </summary>
    public sealed record Failure(string Message, string Source) : DiagramRenderResult
    {
        /// <summary>
        /// Why the diagram failed. <see cref="Message"/> stays the renderer's
        /// own diagnostic; a known reason lets the viewer explain it in the
        /// reader's language instead.
        /// </summary>
        public DiagramFailureReason Reason { get; init; } = DiagramFailureReason.RendererError;
    }
}

/// <summary>Why a diagram could not be shown.</summary>
public enum DiagramFailureReason
{
    /// <summary>The renderer rejected the source or failed on it; its message explains why.</summary>
    RendererError,

    /// <summary>The renderer accepted the source but drew nothing — usually broken syntax it skipped.</summary>
    EmptyDiagram,
}
