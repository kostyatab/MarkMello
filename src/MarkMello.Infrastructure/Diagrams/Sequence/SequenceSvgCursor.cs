using System.Xml.Linq;

namespace MarkMello.Infrastructure.Diagrams.Sequence;

/// <summary>
/// Walks Naiad's SVG elements in the order its renderer emits them. An
/// element of the wrong kind means the SVG is not what the layout expects,
/// and the whole rewrite is abandoned.
/// </summary>
internal sealed class SequenceSvgCursor(IReadOnlyList<XElement> elements)
{
    private int _index;

    public bool AtEnd => _index == elements.Count;

    public XElement Take(string name, string? fill = null, string? stroke = null)
    {
        if (_index >= elements.Count)
        {
            throw new InvalidOperationException($"Sequence SVG ended before its <{name}>.");
        }

        var element = elements[_index++];
        if (element.Name.LocalName != name
            || (fill is not null && (string?)element.Attribute("fill") != fill)
            || (stroke is not null && (string?)element.Attribute("stroke") != stroke))
        {
            throw new InvalidOperationException($"Sequence SVG has <{element.Name.LocalName}> where <{name}> was expected.");
        }

        return element;
    }
}
