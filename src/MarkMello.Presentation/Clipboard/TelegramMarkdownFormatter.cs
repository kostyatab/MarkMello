using MarkMello.Domain;

namespace MarkMello.Presentation.Clipboard;

/// <remarks>
/// <c>alertTitle</c> — заголовки GitHub alerts, те же, что показывает viewer (он
/// их локализует). Выделение задаётся offset'ами текстовой карты viewer'а, а в
/// ней есть и заголовки, поэтому здесь нужны ровно они. Без них —
/// <see cref="MarkdownDocumentTextMap.GetDefaultAlertTitle"/>.
/// </remarks>
public static class TelegramMarkdownFormatter
{
    public static string Format(
        RenderedMarkdownDocument document,
        Func<MarkdownAlertKind, string>? alertTitle = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        return document.Blocks.Count == 0
            ? string.Empty
            : TelegramMarkdownV2Writer.Format(document.Blocks, MarkdownSelectionFormatContext.ForDocument(alertTitle));
    }

    public static string FormatSelection(
        RenderedMarkdownDocument document,
        DocumentTextRange selectionRange,
        Func<MarkdownAlertKind, string>? alertTitle = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        return MarkdownSelectionFormatContext.TryCreateForSelection(document, selectionRange, alertTitle, out var context)
            ? TelegramMarkdownV2Writer.Format(document.Blocks, context)
            : string.Empty;
    }

    public static string FormatSelectionHtml(
        RenderedMarkdownDocument document,
        DocumentTextRange selectionRange,
        Func<MarkdownAlertKind, string>? alertTitle = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        return MarkdownSelectionFormatContext.TryCreateForSelection(document, selectionRange, alertTitle, out var context)
            ? TelegramHtmlClipboardWriter.Format(document.Blocks, context)
            : string.Empty;
    }

    public static IReadOnlyList<string> GetSelectionLinkUrls(
        RenderedMarkdownDocument document,
        DocumentTextRange selectionRange,
        Func<MarkdownAlertKind, string>? alertTitle = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        return MarkdownSelectionFormatContext.TryCreateForSelection(document, selectionRange, alertTitle, out var context)
            ? MarkdownSelectionLinkCollector.Collect(document.Blocks, context)
            : Array.Empty<string>();
    }
}
