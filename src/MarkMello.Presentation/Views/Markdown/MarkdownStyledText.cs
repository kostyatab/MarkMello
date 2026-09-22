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

    /// <summary>
    /// Сноска, к метке которой ведёт иконка возврата в конце текста, — у
    /// последнего абзаца сноски. Иконка только на экране: в <see cref="Text"/>,
    /// выделение и копию она не попадает.
    /// </summary>
    public int? BackReferenceNumber { get; init; }

    /// <summary>
    /// Выделения маркером (<c>&lt;mark&gt;</c>) по порядку начала: у каждого свой
    /// фон, даже у двух вплотную.
    /// </summary>
    public IReadOnlyList<DocumentTextRange> Highlights { get; init; } = Array.Empty<DocumentTextRange>();

    public static MarkdownStyledText Empty { get; } = new(
        string.Empty,
        Array.Empty<MarkdownTextStyleSpan>(),
        Array.Empty<MarkdownLinkSpan>(),
        Array.Empty<MarkdownInlineImageSpan>());

    /// <summary>
    /// Текст блока кода с подсветкой синтаксиса: текст — ровно
    /// <paramref name="code"/>, токены — спаны со своим цветом. Без токенов —
    /// один текст, как у неподсвеченного блока. У строк вставки и удаления
    /// diff цветом выделен только знак: строку отмечает фон
    /// (<see cref="MarkdownCodeLineBands"/>), а текст остаётся цветом текста.
    /// </summary>
    public static MarkdownStyledText FromCode(string code, IReadOnlyList<MarkdownCodeToken>? tokens)
    {
        ArgumentNullException.ThrowIfNull(code);

        if (code.Length == 0)
        {
            return Empty;
        }

        var spans = new List<MarkdownTextStyleSpan>(tokens?.Count ?? 0);
        if (tokens is not null)
        {
            var previousEnd = 0;
            foreach (var token in tokens)
            {
                var start = Math.Max(token.Start, previousEnd);
                var end = Math.Min(token.End, code.Length);
                if (end <= start)
                {
                    continue;
                }

                var style = MarkdownInlineStyleState.Default with { Syntax = token.Kind };
                if (token.Kind is MarkdownCodeTokenKind.Inserted or MarkdownCodeTokenKind.Deleted)
                {
                    foreach (var line in MarkdownCodeLineBands.SplitLines(code, start, end))
                    {
                        spans.Add(new MarkdownTextStyleSpan(new DocumentTextRange(line.Start, line.Start + 1), style));
                    }
                }
                else
                {
                    spans.Add(new MarkdownTextStyleSpan(new DocumentTextRange(start, end), style));
                }

                previousEnd = end;
            }
        }

        return new MarkdownStyledText(code, spans, Array.Empty<MarkdownLinkSpan>(), Array.Empty<MarkdownInlineImageSpan>());
    }

    /// <summary>Есть ли спаны подсветки синтаксиса — нужны ли кисти <c>MmSyntax*</c>.</summary>
    public bool HasSyntax => Spans.Any(static span => span.Style.Syntax is not null);

    /// <summary>
    /// Номер сноски в блоке сносок («1 »): сам номер — ссылка обратно к метке в тексте.
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
        var highlights = new List<DocumentTextRange>();
        AppendInlines(inlines, builder, spans, links, images, footnotes, highlights, MarkdownInlineStyleState.Default);
        return builder.Length == 0
            ? Empty
            : new MarkdownStyledText(builder.ToString(), spans, links, images, footnotes)
            {
                // Вложенное выделение добавлено раньше внешнего: по началу, внешнее первым.
                Highlights = [.. highlights.OrderBy(static range => range.Start).ThenByDescending(static range => range.End)]
            };
    }

    private static void AppendInlines(
        IReadOnlyList<MarkdownInline> inlines,
        StringBuilder builder,
        List<MarkdownTextStyleSpan> spans,
        List<MarkdownLinkSpan> links,
        List<MarkdownInlineImageSpan> images,
        List<MarkdownFootnoteReferenceSpan> footnotes,
        List<DocumentTextRange> highlights,
        MarkdownInlineStyleState style)
    {
        foreach (var inline in inlines)
        {
            AppendInline(inline, builder, spans, links, images, footnotes, highlights, style);
        }
    }

    private static void AppendInline(
        MarkdownInline inline,
        StringBuilder builder,
        List<MarkdownTextStyleSpan> spans,
        List<MarkdownLinkSpan> links,
        List<MarkdownInlineImageSpan> images,
        List<MarkdownFootnoteReferenceSpan> footnotes,
        List<DocumentTextRange> highlights,
        MarkdownInlineStyleState style)
    {
        switch (inline)
        {
            case MarkdownTextInline text:
                AppendStyledText(text.Text, builder, spans, style);
                return;

            case MarkdownStrongInline strong:
                AppendInlines(strong.Inlines, builder, spans, links, images, footnotes, highlights, style with { IsBold = true });
                return;

            case MarkdownEmphasisInline emphasis:
                AppendInlines(emphasis.Inlines, builder, spans, links, images, footnotes, highlights, style with { IsItalic = true });
                return;

            case MarkdownStrikethroughInline strikethrough:
                AppendInlines(strikethrough.Inlines, builder, spans, links, images, footnotes, highlights, style with { IsStrikethrough = true });
                return;

            case MarkdownHighlightInline highlight:
                var highlightStart = builder.Length;
                AppendInlines(highlight.Inlines, builder, spans, links, images, footnotes, highlights, style);
                if (builder.Length > highlightStart)
                {
                    highlights.Add(new DocumentTextRange(highlightStart, builder.Length));
                }
                return;

            case MarkdownSubscriptInline subscript:
                AppendInlines(subscript.Inlines, builder, spans, links, images, footnotes, highlights, style with { Script = MarkdownScriptPosition.Subscript });
                return;

            case MarkdownSuperscriptInline superscript:
                AppendInlines(superscript.Inlines, builder, spans, links, images, footnotes, highlights, style with { Script = MarkdownScriptPosition.Superscript });
                return;

            case MarkdownCodeInline code:
                AppendStyledText(code.Code, builder, spans, style with { IsCode = true });
                return;

            case MarkdownKeyboardInline keyboard:
                AppendStyledText(keyboard.Text, builder, spans, style with { IsKeyboard = true });
                return;

            case MarkdownImageInline image:
                AppendImage(image, builder, spans, images, style);
                return;

            case MarkdownLinkInline link:
                AppendLink(link, builder, spans, links, images, footnotes, highlights, style with { IsLink = true });
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
        List<DocumentTextRange> highlights,
        MarkdownInlineStyleState style)
    {
        var start = builder.Length;

        if (link.Inlines.Count > 0)
        {
            AppendInlines(link.Inlines, builder, spans, links, images, footnotes, highlights, style);
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

        // Клавиши вплотную (<kbd>Ctrl</kbd><kbd>C</kbd>) — две клавиши, а не одна.
        if (spans.Count > 0 && !style.IsKeyboard)
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

/// <param name="IsKeyboard">Клавиша (<c>&lt;kbd&gt;</c>).</param>
/// <param name="Script">Индекс (<c>&lt;sub&gt;</c>, <c>&lt;sup&gt;</c>) или обычная строка.</param>
/// <param name="Syntax">
/// Вид токена подсветки в блоке кода (ADR-0010): меняет только цвет текста.
/// </param>
internal readonly record struct MarkdownInlineStyleState(
    bool IsBold,
    bool IsItalic,
    bool IsCode,
    bool IsLink,
    bool IsStrikethrough,
    bool IsKeyboard = false,
    MarkdownScriptPosition Script = MarkdownScriptPosition.None,
    MarkdownCodeTokenKind? Syntax = null)
{
    public static MarkdownInlineStyleState Default { get; } = new(false, false, false, false, false);

    /// <summary>
    /// Код и клавиша: моноширинный шрифт и рамка вокруг текста с отступами по бокам.
    /// </summary>
    public bool IsBoxed => IsCode || IsKeyboard;

    /// <summary>
    /// Текст индекса: мельче и со сдвинутой базовой линией. Код и клавиша внутри
    /// индекса остаются в строке как есть — их рамка стоит по центру строки.
    /// </summary>
    public bool IsScript => Script != MarkdownScriptPosition.None && !IsBoxed;
}

/// <summary>Положение текста относительно строки: индексы меньше и сдвинуты.</summary>
internal enum MarkdownScriptPosition
{
    None,
    Subscript,
    Superscript
}
