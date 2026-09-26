namespace MarkMello.Application.Abstractions;

/// <summary>
/// Measures diagram labels with the font engine that later draws the SVG, so
/// a layout sized from these metrics fits the text the viewer actually shows.
/// <paramref name="fontFamily"/> is an SVG <c>font-family</c> list; the
/// measurer resolves it the same way the SVG viewer does.
/// </summary>
public interface IDiagramTextMeasurer
{
    DiagramTextSize Measure(string text, string fontFamily, double fontSize, bool bold);
}

public readonly record struct DiagramTextSize(double Width, double Height);
