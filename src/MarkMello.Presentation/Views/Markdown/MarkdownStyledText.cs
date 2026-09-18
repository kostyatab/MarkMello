using System.Text;
using MarkMello.Domain;

namespace MarkMello.Presentation.Views.Markdown;

/// <param name="FootnoteReferences">
/// Метки сносок: в <paramref name="Text"/> — «[1]», на экране — номер верхним индексом.
/// </param>
internal sealed record MarkdownStyledText(
    string Text,
    IReadOnlyList<MarkdownTextStyleSpan> Spans,
    IReadOnlyList<MarkdownLinkSpan> Links,
    IReadOnlyList<MarkdownInlineImageSpan> Images,
    IReadOnlyList<MarkdownFootnoteReferenceSpan>? FootnoteReferences = null)
{
    public IReadOnlyList<MarkdownFootnoteReferenceSpan> FootnoteReferences { get; init; } =
        FootnoteReferences ?? Array.Empty<MarkdownFootnoteReferenceSpan>();

    public static MarkdownStyledText Empty { get; } = new(
        string.Empty,
        Array.Empty<MarkdownTextStyleSpan>(),
        Array.Empty<MarkdownLinkSpan>(),
        Array.Empty<MarkdownInlineImageSpan>());

    /// <summary>
    /// Номер сноски в блоке сносок («1. »): сам номер — ссылка обратно к метке в тексте.
    /// </summary>
    public static MarkdownStyledText ForFootnoteMarker(int number)
    {
        var text = MarkdownDocumentTextMap.GetFootnoteMarkerText(number);
        var numberLength = text.TrimEnd().Length;
        return new MarkdownStyledText(
            text,
            Array.Empty<MarkdownTextStyleSpan>(),
            [
                new MarkdownLinkSpan(new DocumentTextRange(0, numberLength), string.Empty, null)
                {
                    Footnote = new MarkdownFootnoteLinkTarget(number, IsBackReference: true)
                }
            ],
            Array.Empty<MarkdownInlineImageSpan>());
    }

    public static MarkdownStyledText FromInlines(IReadOnlyList<MarkdownInline> inlines)
    {
        ArgumentNullException.ThrowIfNull(inlines);

        if (inlines.Count == 0)
        {
            return Empty;
        }

        var builder = new StringBuilder();
        var spans = new List<MarkdownTextStyleSpan>();
        var links = new List<MarkdownLinkSpan>();
        var images = new List<MarkdownInlineImageSpan>();
        var footnotes = new List<MarkdownFootnoteReferenceSpan>();
        AppendInlines(inlines, builder, spans, links, images, footnotes, MarkdownInlineStyleState.Default);
        return builder.Length == 0
            ? Empty
            : new MarkdownStyledText(builder.ToString(), spans, links, images, footnotes);
    }

    private static void AppendInlines(
        IReadOnlyList<MarkdownInline> inlines,
        StringBuilder builder,
        List<MarkdownTextStyleSpan> spans,
        List<MarkdownLinkSpan> links,
        List<MarkdownInlineImageSpan> images,
        List<MarkdownFootnoteReferenceSpan> footnotes,
        MarkdownInlineStyleState style)
    {
        foreach (var inline in inlines)
        {
            AppendInline(inline, builder, spans, links, images, footnotes, style);
        }
    }

    private static void AppendInline(
        MarkdownInline inline,
        StringBuilder builder,
        List<MarkdownTextStyleSpan> spans,
        List<MarkdownLinkSpan> links,
        List<MarkdownInlineImageSpan> images,
        List<MarkdownFootnoteReferenceSpan> footnotes,
        MarkdownInlineStyleState style)
    {
        switch (inline)
        {
            case MarkdownTextInline text:
                AppendStyledText(text.Text, builder, spans, style);
                return;

            case MarkdownStrongInline strong:
                AppendInlines(strong.Inlines, builder, spans, links, images, footnotes, style with { IsBold = true });
                return;

            case MarkdownEmphasisInline emphasis:
                AppendInlines(emphasis.Inlines, builder, spans, links, images, footnotes, style with { IsItalic = true });
                return;

            case MarkdownStrikethroughInline strikethrough:
                AppendInlines(strikethrough.Inlines, builder, spans, links, images, footnotes, style with { IsStrikethrough = true });
                return;

            case MarkdownCodeInline code:
                AppendStyledText(code.Code, builder, spans, style with { IsCode = true });
                return;

            case MarkdownImageInline image:
                AppendImage(image, builder, spans, images, style);
                return;

            case MarkdownLinkInline link:
                AppendLink(link, builder, spans, links, images, footnotes, style with { IsLink = true });
                return;

            case MarkdownLineBreakInline:
                AppendStyledText("\n", builder, spans, style);
                return;

            case MarkdownFootnoteReferenceInline footnote:
                AppendFootnoteReference(footnote.Number, builder, links, footnotes);
                return;
        }
    }

    /// <summary>
    /// Метка сноски — «[1]» в тексте и ссылка к сноске. Стиль окружающего текста
    /// (жирный, курсив) на неё не переносится: верхний индекс рисуется своим стилем.
    /// </summary>
    private static void AppendFootnoteReference(
        int number,
        StringBuilder builder,
        List<MarkdownLinkSpan> links,
        List<MarkdownFootnoteReferenceSpan> footnotes)
    {
        var start = builder.Length;
        builder.Append(MarkdownDocumentTextMap.GetFootnoteReferenceText(number));
        var range = new DocumentTextRange(start, builder.Length);

        footnotes.Add(new MarkdownFootnoteReferenceSpan(range, number));
        links.Add(new MarkdownLinkSpan(range, string.Empty, null)
        {
            Footnote = new MarkdownFootnoteLinkTarget(number, IsBackReference: false)
        });
    }

    private static string GetImageInlinePlainText(MarkdownImageInline image)
    {
        if (!string.IsNullOrWhiteSpace(image.AltText))
        {
            return image.AltText;
        }

        if (!string.IsNullOrWhiteSpace(image.Title))
        {
            return image.Title;
        }

        return string.IsNullOrWhiteSpace(image.Url) || image.Url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            ? "image"
            : image.Url;
    }

    private static void AppendImage(
        MarkdownImageInline image,
        StringBuilder builder,
        List<MarkdownTextStyleSpan> spans,
        List<MarkdownInlineImageSpan> images,
        MarkdownInlineStyleState style)
    {
        var placeholderText = GetImageInlinePlainText(image);
        if (string.IsNullOrWhiteSpace(placeholderText))
        {
            placeholderText = "image";
        }

        var start = builder.Length;
        AppendStyledText(placeholderText, builder, spans, style);
        var end = builder.Length;
        if (end <= start)
        {
            return;
        }

        images.Add(new MarkdownInlineImageSpan(
            images.Count,
            new DocumentTextRange(start, end),
            image.Url,
            image.AltText,
            image.Title,
            placeholderText,
            style));
    }

    private static void AppendLink(
        MarkdownLinkInline link,
        StringBuilder builder,
        List<MarkdownTextStyleSpan> spans,
        List<MarkdownLinkSpan> links,
        List<MarkdownInlineImageSpan> images,
        List<MarkdownFootnoteReferenceSpan> footnotes,
        MarkdownInlineStyleState style)
    {
        var start = builder.Length;

        if (link.Inlines.Count > 0)
        {
            AppendInlines(link.Inlines, builder, spans, links, images, footnotes, style);
        }
        else if (!string.IsNullOrWhiteSpace(link.Url))
        {
            AppendStyledText(link.Url, builder, spans, style);
        }

        var end = builder.Length;
        if (end <= start || string.IsNullOrWhiteSpace(link.Url))
        {
            return;
        }

        var range = new DocumentTextRange(start, end);
        if (links.Count > 0)
        {
            var last = links[^1];
            if (last.Range.End == range.Start
                && string.Equals(last.Url, link.Url, StringComparison.Ordinal)
                && string.Equals(last.Title, link.Title, StringComparison.Ordinal))
            {
                links[^1] = last with { Range = new DocumentTextRange(last.Range.Start, range.End) };
                return;
            }
        }

        links.Add(new MarkdownLinkSpan(range, link.Url, link.Title));
    }

    private static void AppendStyledText(
        string text,
        StringBuilder builder,
        List<MarkdownTextStyleSpan> spans,
        MarkdownInlineStyleState style)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var start = builder.Length;
        builder.Append(text);
        var length = builder.Length - start;
        if (length == 0 || style == MarkdownInlineStyleState.Default)
        {
            return;
        }

        var range = new DocumentTextRange(start, start + length);
        if (spans.Count > 0)
        {
            var last = spans[^1];
            if (last.Style == style && last.Range.End == range.Start)
            {
                spans[^1] = last with { Range = new DocumentTextRange(last.Range.Start, range.End) };
                return;
            }
        }

        spans.Add(new MarkdownTextStyleSpan(range, style));
    }
}

internal readonly record struct MarkdownTextStyleSpan(DocumentTextRange Range, MarkdownInlineStyleState Style);

internal readonly record struct MarkdownLinkSpan(DocumentTextRange Range, string Url, string? Title)
{
    /// <summary>
    /// Переход по сноске вместо адреса: у метки сноски и номера в блоке сносок
    /// <see cref="Url"/> пустой.
    /// </summary>
    public MarkdownFootnoteLinkTarget? Footnote { get; init; }
}

/// <param name="Number">Номер сноски.</param>
/// <param name="IsBackReference">
/// <c>false</c> — метка в тексте, ведёт к сноске; <c>true</c> — номер сноски в блоке
/// сносок, ведёт обратно к метке.
/// </param>
internal readonly record struct MarkdownFootnoteLinkTarget(int Number, bool IsBackReference);

/// <summary>Метка сноски: «[1]» по <paramref name="Range"/> в тексте фрагмента.</summary>
internal readonly record struct MarkdownFootnoteReferenceSpan(DocumentTextRange Range, int Number);

internal readonly record struct MarkdownInlineImageSpan(
    int Index,
    DocumentTextRange Range,
    string Url,
    string? AltText,
    string? Title,
    string PlaceholderText,
    MarkdownInlineStyleState Style);

internal readonly record struct MarkdownInlineStyleState(bool IsBold, bool IsItalic, bool IsCode, bool IsLink, bool IsStrikethrough)
{
    public static MarkdownInlineStyleState Default { get; } = new(false, false, false, false, false);
}
