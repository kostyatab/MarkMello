using Avalonia.Media;

namespace MarkMello.Presentation.Views.Markdown;

/// <summary>
/// Resolves an SVG <c>font-family</c> list to one Avalonia typeface. The SVG
/// viewer draws text with it and the diagram text measurer sizes labels with
/// it, so a label measured for layout is the label that gets drawn — including
/// the fallback font on a system without the named family.
/// </summary>
internal static class SvgTypeface
{
    public static Typeface Resolve(string? fontFamily, FontWeight weight)
    {
        if (string.IsNullOrWhiteSpace(fontFamily))
        {
            return new Typeface(FontFamily.Default, FontStyle.Normal, weight);
        }

        // SVG font-family is a comma-separated fallback list; Avalonia's
        // FontFamily accepts a single family. Take the first non-empty
        // token, otherwise fall back to the system default so glyphs
        // always render.
        foreach (var candidate in fontFamily.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var unquoted = candidate.Trim('"', '\'').Trim();
            if (unquoted.Length == 0)
            {
                continue;
            }

            try
            {
                return new Typeface(new FontFamily(unquoted), FontStyle.Normal, weight);
            }
            catch (ArgumentException)
            {
                continue;
            }
        }

        return new Typeface(FontFamily.Default, FontStyle.Normal, weight);
    }
}
