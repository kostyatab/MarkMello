using System.Text.Json;

namespace MarkMello.Infrastructure.Diagrams.Sequence;

/// <summary>
/// The three <c>%%{init}%%</c> sequence keys the layout honours, with
/// Mermaid's defaults. Naiad ignores init directives altogether.
/// </summary>
internal sealed record SequenceDiagramSettings(double Width = 150, double ActorMargin = 50, double MessageMargin = 35)
{
    /// <summary>
    /// Splits <paramref name="source"/> into the diagram text Naiad parses
    /// (trimmed, leading init directives stripped, as <c>Mermaid.Render</c>
    /// does) and the settings those directives carry.
    /// </summary>
    public static (string Source, SequenceDiagramSettings Settings) Read(string source)
    {
        var remaining = source.Trim();
        var settings = new SequenceDiagramSettings();
        while (remaining.StartsWith("%%{", StringComparison.Ordinal))
        {
            var end = remaining.IndexOf("}%%", StringComparison.Ordinal);
            if (end < 0)
            {
                break;
            }

            var directive = remaining[3..end].Trim();
            var colon = directive.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0 && directive[..colon].TrimEnd() is "init" or "initialize")
            {
                // Mermaid turns single quotes into double ones before parsing,
                // and its documentation writes directives that way.
                settings = ReadJson(directive[(colon + 1)..].Replace('\'', '"'), settings);
            }

            remaining = remaining[(end + 3)..].TrimStart();
        }

        return (remaining, settings);
    }

    private static SequenceDiagramSettings ReadJson(string json, SequenceDiagramSettings current)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("sequence", out var sequence)
                || sequence.ValueKind != JsonValueKind.Object)
            {
                return current;
            }

            return new SequenceDiagramSettings(
                ReadNumber(sequence, "width", current.Width),
                ReadNumber(sequence, "actorMargin", current.ActorMargin),
                ReadNumber(sequence, "messageMargin", current.MessageMargin));
        }
        catch (JsonException)
        {
            // Malformed configuration keeps the defaults, as Mermaid does.
            return current;
        }
    }

    private static double ReadNumber(JsonElement element, string name, double fallback)
        => element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out var number)
            && double.IsFinite(number)
            && number > 0
            && number <= 10000
                ? number
                : fallback;
}
