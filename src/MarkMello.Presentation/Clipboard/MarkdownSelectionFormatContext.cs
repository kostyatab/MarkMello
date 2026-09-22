using MarkMello.Domain;

namespace MarkMello.Presentation.Clipboard;

internal sealed class MarkdownSelectionFormatContext
{
    private readonly MarkdownDocumentTextMap? _textMap;
    private readonly DocumentTextRange _selectionRange;
    private readonly Func<MarkdownAlertKind, string> _alertTitle;

    private MarkdownSelectionFormatContext(
        MarkdownDocumentTextMap? textMap,
        DocumentTextRange selectionRange,
        Func<MarkdownAlertKind, string>? alertTitle)
    {
        _textMap = textMap;
        _selectionRange = selectionRange;
        _alertTitle = alertTitle ?? MarkdownDocumentTextMap.GetDefaultAlertTitle;
    }

    public bool IsSelection => _textMap is not null;

    /// <summary>
    /// Вложенность списков в том месте, где сейчас идёт обход документа: от неё
    /// зависит маркер пункта. Писатель заходит в список через
    /// <see cref="MarkdownListNesting.Enter"/> и восстанавливает значение на выходе.
    /// </summary>
    public MarkdownListNesting ListNesting { get; set; }

    /// <param name="alertTitle">
    /// Заголовки GitHub alerts — те же, что показывает viewer. Без них —
    /// <see cref="MarkdownDocumentTextMap.GetDefaultAlertTitle"/>.
    /// </param>
    public static MarkdownSelectionFormatContext ForDocument(Func<MarkdownAlertKind, string>? alertTitle = null)
        => new(null, DocumentTextRange.Empty, alertTitle);

    /// <param name="document">Документ.</param>
    /// <param name="selectionRange">Выделение в текстовой карте viewer'а.</param>
    /// <param name="alertTitle">
    /// Заголовки GitHub alerts, с которыми viewer строил свою текстовую карту:
    /// иначе offset'ы выделения здесь и во viewer'е разойдутся.
    /// </param>
    /// <param name="context">Контекст выделения.</param>
    public static bool TryCreateForSelection(
        RenderedMarkdownDocument document,
        DocumentTextRange selectionRange,
        Func<MarkdownAlertKind, string>? alertTitle,
        out MarkdownSelectionFormatContext context)
    {
        context = ForDocument(alertTitle);
        if (document.Blocks.Count == 0 || selectionRange.IsEmpty)
        {
            return false;
        }

        var textMap = MarkdownDocumentTextMap.Create(document, alertTitle);
        if (textMap.Text.Length == 0)
        {
            return false;
        }

        var start = Math.Clamp(selectionRange.Start, 0, textMap.Text.Length);
        var end = Math.Clamp(selectionRange.End, start, textMap.Text.Length);
        if (end <= start)
        {
            return false;
        }

        context = new MarkdownSelectionFormatContext(textMap, new DocumentTextRange(start, end), alertTitle);
        return true;
    }

    public string GetAlertTitle(MarkdownAlertKind kind) => _alertTitle(kind);

    public bool TryGetFragmentLocalRange(string path, out DocumentTextRange? localRange)
    {
        if (!IsSelection)
        {
            localRange = null;
            return true;
        }

        if (TryGetLocalSelection(path, out _, out var selectedRange))
        {
            localRange = selectedRange;
            return true;
        }

        localRange = null;
        return false;
    }

    public bool TryGetFragmentText(string path, string text, out string selectedText)
    {
        if (!IsSelection)
        {
            selectedText = text;
            return selectedText.Length > 0;
        }

        if (!TryGetLocalSelection(path, out var fragment, out var selectedRange))
        {
            selectedText = string.Empty;
            return false;
        }

        selectedText = MarkdownClipboardTextHelpers.Slice(fragment.Text, selectedRange);
        return selectedText.Length > 0;
    }

    public bool TryGetLocalSelection(
        string path,
        out MarkdownDocumentTextFragment fragment,
        out DocumentTextRange localRange)
    {
        fragment = null!;
        localRange = DocumentTextRange.Empty;

        if (_textMap is null || !_textMap.TryGetFragment(path, out fragment))
        {
            return false;
        }

        var intersection = fragment.Range.Intersection(_selectionRange);
        if (intersection.IsEmpty)
        {
            return false;
        }

        localRange = new DocumentTextRange(
            intersection.Start - fragment.Range.Start,
            intersection.End - fragment.Range.Start);
        return true;
    }
}
