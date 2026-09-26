using System.Globalization;
using Avalonia.Media;
using MarkMello.Application.Abstractions;
using MarkMello.Presentation.Views.Markdown;

namespace MarkMello.Presentation.Services;

/// <summary>
/// Measures diagram labels exactly as <see cref="AotSafeSvgImage"/> lays them
/// out: same typeface resolution, same <see cref="FormattedText"/> width.
/// Stateless, so the edit-mode preview may call it off the UI thread.
/// </summary>
public sealed class AvaloniaDiagramTextMeasurer : IDiagramTextMeasurer
{
    public DiagramTextSize Measure(string text, string fontFamily, double fontSize, bool bold)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            SvgTypeface.Resolve(fontFamily, bold ? FontWeight.Bold : FontWeight.Normal),
            fontSize,
            Brushes.Black);
        return new DiagramTextSize(formatted.Width, formatted.Height);
    }
}
