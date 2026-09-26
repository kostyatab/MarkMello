using System.Globalization;
using System.Xml.Linq;

namespace MarkMello.Infrastructure.Diagrams.Sequence;

/// <summary>Writes coordinates into Naiad's SVG elements.</summary>
internal static class SequenceSvgGeometry
{
    public static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    public static void Set(XElement element, string attribute, double value)
        => element.SetAttributeValue(attribute, Format(value));

    public static void SetLine(XElement line, double x1, double y1, double x2, double y2)
    {
        Set(line, "x1", x1);
        Set(line, "y1", y1);
        Set(line, "x2", x2);
        Set(line, "y2", y2);
    }

    /// <summary>Points joined the way Naiad writes a path: <c>M</c>, then <c>L</c> for the rest.</summary>
    public static string Path(bool closed, params ReadOnlySpan<(double X, double Y)> points)
    {
        var parts = new string[points.Length + (closed ? 1 : 0)];
        for (var index = 0; index < points.Length; index++)
        {
            parts[index] = (index == 0 ? "M" : "L") + Point(points[index]);
        }

        if (closed)
        {
            parts[^1] = "Z";
        }

        return string.Join(' ', parts);
    }

    public static string Points(params ReadOnlySpan<(double X, double Y)> points)
    {
        var parts = new string[points.Length];
        for (var index = 0; index < points.Length; index++)
        {
            parts[index] = Point(points[index]);
        }

        return string.Join(' ', parts);
    }

    private static string Point((double X, double Y) point) => Format(point.X) + "," + Format(point.Y);
}
