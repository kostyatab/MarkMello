namespace MarkMello.Domain;

/// <summary>
/// Результат markdown parse/render pipeline для native viewer M3.
/// Содержит устойчивую block/inline модель, независимую от UI framework.
/// </summary>
/// <param name="Blocks">Плоский список блоков документа.</param>
/// <param name="BaseDirectory">
/// Директория исходного .md-файла. Используется для разрешения относительных
/// путей ресурсов (изображений). Null когда источник не имеет файловой
/// локации (например, при рендере plain-text fallback или в тестах).
/// </param>
public sealed record RenderedMarkdownDocument(
    IReadOnlyList<MarkdownBlock> Blocks,
    string? BaseDirectory = null)
{
    public static RenderedMarkdownDocument Empty { get; } = new(Array.Empty<MarkdownBlock>());

    public static RenderedMarkdownDocument PlainText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Empty;
        }

        return new RenderedMarkdownDocument(
        [
            new MarkdownParagraphBlock(
            [
                new MarkdownTextInline(text)
            ])
        ]);
    }
}

/// <summary>
/// Zero-based source line span for a rendered markdown block.
/// Used by edit-mode scroll synchronization to map source lines to preview blocks.
/// </summary>
public readonly record struct MarkdownSourceSpan
{
    public MarkdownSourceSpan(int startLine, int endLine)
    {
        StartLine = Math.Max(0, startLine);
        EndLine = Math.Max(StartLine, endLine);
    }

    public MarkdownSourceSpan(int line)
        : this(line, line)
    {
    }

    public int StartLine { get; }

    public int EndLine { get; }
}

public abstract record MarkdownBlock
{
    public MarkdownSourceSpan? SourceSpan { get; init; }
}

public sealed record MarkdownHeadingBlock(int Level, IReadOnlyList<MarkdownInline> Inlines) : MarkdownBlock;

public sealed record MarkdownParagraphBlock(IReadOnlyList<MarkdownInline> Inlines) : MarkdownBlock;

/// <param name="Blocks">Содержимое цитаты.</param>
/// <param name="AlertKind">
/// Вид GitHub alert (<c>&gt; [!NOTE]</c> и др.) или <c>null</c> для обычной цитаты.
/// Маркер <c>[!NOTE]</c> в <paramref name="Blocks"/> не попадает: заголовок alert
/// показывает viewer.
/// </param>
public sealed record MarkdownQuoteBlock(
    IReadOnlyList<MarkdownBlock> Blocks,
    MarkdownAlertKind? AlertKind = null) : MarkdownBlock;

/// <summary>
/// Виды GitHub alerts. Других GitHub не поддерживает: цитата с неизвестным видом
/// (<c>&gt; [!FOO]</c>) остаётся обычной цитатой.
/// </summary>
public enum MarkdownAlertKind
{
    Note,
    Tip,
    Important,
    Warning,
    Caution
}

/// <param name="IsOrdered">Нумерованный список (<c>1.</c>) или маркированный (<c>-</c>).</param>
/// <param name="Items">Пункты списка.</param>
/// <param name="StartNumber">
/// Номер первого пункта нумерованного списка: <c>7.</c> в начале списка даёт
/// 7, 8, 9… По CommonMark это число из маркера первого пункта, номера
/// остальных пунктов в исходнике не важны. У маркированного списка не используется.
/// </param>
/// <param name="IsLoose">
/// Loose-список по CommonMark: пункты или блоки внутри пункта разделены пустыми
/// строками. Такой список рисуется с абзацным отступом между пунктами, а tight —
/// плотнее.
/// </param>
public sealed record MarkdownListBlock(
    bool IsOrdered,
    IReadOnlyList<MarkdownListItem> Items,
    int StartNumber = 1,
    bool IsLoose = false) : MarkdownBlock;

/// <param name="Blocks">Содержимое пункта.</param>
/// <param name="IsChecked">
/// Состояние пункта task list (<c>- [ ]</c> / <c>- [x]</c>): <c>false</c> — пустой
/// чекбокс, <c>true</c> — отмеченный, <c>null</c> — обычный пункт без чекбокса.
/// Сам маркер <c>[ ]</c> в <paramref name="Blocks"/> не попадает.
/// </param>
public sealed record MarkdownListItem(IReadOnlyList<MarkdownBlock> Blocks, bool? IsChecked = null);

/// <summary>
/// Сноски документа (<c>[^label]: текст</c>) — один блок в конце документа, где
/// бы ни стояли определения в исходнике. Сноски идут по номерам; сноска, на
/// которую нет ни одной ссылки, в блок не попадает.
/// </summary>
public sealed record MarkdownFootnotesBlock(IReadOnlyList<MarkdownFootnote> Footnotes) : MarkdownBlock;

/// <param name="Number">
/// Номер сноски (1, 2, …) — по порядку первых ссылок на неё в тексте. Тот же номер
/// показывают все метки <see cref="MarkdownFootnoteReferenceInline"/> этой сноски.
/// </param>
/// <param name="Blocks">Содержимое сноски: один или несколько абзацев и другие блоки.</param>
public sealed record MarkdownFootnote(int Number, IReadOnlyList<MarkdownBlock> Blocks);

public sealed record MarkdownHorizontalRuleBlock() : MarkdownBlock;

public sealed record MarkdownCodeBlock(string? Info, string Code) : MarkdownBlock;

/// <param name="Header">Ячейки строки заголовка.</param>
/// <param name="Rows">Строки данных.</param>
/// <param name="ColumnAlignments">
/// Выравнивание колонок из строки-разделителя, по индексу колонки. Колонка без
/// записи (или всей таблицы без списка) выравнивается по левому краю.
/// </param>
public sealed record MarkdownTableBlock(
    IReadOnlyList<MarkdownTableCell> Header,
    IReadOnlyList<IReadOnlyList<MarkdownTableCell>> Rows,
    IReadOnlyList<MarkdownTableColumnAlignment>? ColumnAlignments = null) : MarkdownBlock
{
    public IReadOnlyList<MarkdownTableColumnAlignment> ColumnAlignments { get; init; } =
        ColumnAlignments ?? Array.Empty<MarkdownTableColumnAlignment>();

    public MarkdownTableColumnAlignment GetColumnAlignment(int columnIndex)
        => (uint)columnIndex < (uint)ColumnAlignments.Count
            ? ColumnAlignments[columnIndex]
            : MarkdownTableColumnAlignment.Left;
}

public sealed record MarkdownTableCell(IReadOnlyList<MarkdownInline> Inlines);

/// <summary>
/// Выравнивание колонки таблицы: <c>:---</c> и колонка без маркера (<c>---</c>) —
/// по левому краю, <c>:---:</c> — по центру, <c>---:</c> — по правому краю.
/// </summary>
public enum MarkdownTableColumnAlignment
{
    Left,
    Center,
    Right
}

/// <summary>
/// Block-level image. Emitted when a markdown source paragraph contains
/// exactly one image node (e.g. a standalone ![alt](url) line) or a block
/// of HTML whose sole meaningful content is a &lt;img&gt; tag. Rendered as
/// an own non-selectable visual, outside the document text flow and text
/// map. Alt text is shown as a caption below the image, or as a
/// placeholder when the image cannot be loaded.
/// </summary>
public sealed record MarkdownImageBlock(
    string Url,
    string? AltText,
    string? Title,
    double? Width = null,
    double? Height = null) : MarkdownBlock;

/// <summary>
/// Diagram dialects MarkMello may emit as <see cref="MarkdownDiagramBlock"/>.
/// Membership in this enum does not imply runtime support — that is decided
/// by <c>SupportedDiagramDialects</c> in the Application layer plus mandatory
/// renderer composition validation.
/// </summary>
public enum MarkdownDiagramKind
{
    /// <summary>Mermaid diagrams (first supported dialect, see ADR-0005).</summary>
    Mermaid,

    /// <summary>
    /// PlantUML diagrams. Reserved in the model for future support; not
    /// emitted by the parser and not declared as a supported dialect until
    /// a renderer backend is selected via a follow-up ADR.
    /// </summary>
    PlantUml,
}

/// <summary>
/// Block-level diagram. Emitted by the markdown pipeline when a fenced code
/// block declares a recognized diagram dialect (currently only
/// <c>mermaid</c>). The block carries the original diagram source so it can
/// be rendered later by an <c>IDiagramRenderService</c>, and optionally a
/// runtime <see cref="RenderResult"/> populated by that service.
/// </summary>
/// <param name="Kind">Diagram dialect, decided by the fence info string.</param>
/// <param name="Source">
/// Raw diagram source as written in the markdown file, with line breaks
/// preserved and trailing newline trimmed (matches <see cref="MarkdownCodeBlock"/>
/// behavior).
/// </param>
/// <param name="Info">
/// Remainder of the fence info string after the dialect token, or <c>null</c>
/// when the fence carried only the dialect name. Preserved for diagnostics
/// and future title/attribute parsing.
/// </param>
/// <param name="Title">
/// Optional human-readable title for the diagram. Reserved for future fence
/// attribute parsing; left <c>null</c> by the current parser.
/// </param>
public sealed record MarkdownDiagramBlock(
    MarkdownDiagramKind Kind,
    string Source,
    string? Info = null,
    string? Title = null) : MarkdownBlock
{
    /// <summary>
    /// Result of rendering <see cref="Source"/> through the diagram service.
    /// Null until a render pass populates it; never set by the markdown
    /// parser itself.
    /// </summary>
    public DiagramRenderResult? RenderResult { get; init; }
}

public abstract record MarkdownInline;

public sealed record MarkdownTextInline(string Text) : MarkdownInline;

public sealed record MarkdownStrongInline(IReadOnlyList<MarkdownInline> Inlines) : MarkdownInline;

public sealed record MarkdownEmphasisInline(IReadOnlyList<MarkdownInline> Inlines) : MarkdownInline;

public sealed record MarkdownStrikethroughInline(IReadOnlyList<MarkdownInline> Inlines) : MarkdownInline;

public sealed record MarkdownCodeInline(string Code) : MarkdownInline;

/// <summary>
/// Клавиша (<c>&lt;kbd&gt;Ctrl&lt;/kbd&gt;</c>): показывается как клавиша, а в тексте
/// документа — выделении, поиске, копировании — это обычный текст <paramref name="Text"/>.
/// </summary>
public sealed record MarkdownKeyboardInline(string Text) : MarkdownInline;

public sealed record MarkdownImageInline(string Url, string? AltText, string? Title) : MarkdownInline;

public sealed record MarkdownLinkInline(IReadOnlyList<MarkdownInline> Inlines, string Url, string? Title) : MarkdownInline;

public sealed record MarkdownLineBreakInline() : MarkdownInline;

/// <summary>
/// Метка сноски в тексте (<c>[^label]</c>). Повторная ссылка на ту же сноску даёт
/// ту же метку.
/// </summary>
/// <param name="Number">Номер сноски — <see cref="MarkdownFootnote.Number"/>.</param>
public sealed record MarkdownFootnoteReferenceInline(int Number) : MarkdownInline;
