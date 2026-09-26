using MarkMello.Application.Abstractions;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Diagram text measurer for tests that need no Avalonia: every character
/// advances by the same fraction of the font size.
/// </summary>
internal sealed class FixedAdvanceDiagramTextMeasurer : IDiagramTextMeasurer
{
    public DiagramTextSize Measure(string text, string fontFamily, double fontSize, bool bold)
        => new(text.Length * fontSize * 0.6, fontSize * 1.2);
}
