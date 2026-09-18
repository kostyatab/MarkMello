using System.Net;
using System.Globalization;
using System.Text.RegularExpressions;
using Markdig;
using MarkdigMarkdown = Markdig.Markdown;
using Markdig.Extensions.Alerts;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MarkMello.Application.Abstractions;
using MarkMello.Application.Diagrams;
using MarkMello.Domain;
using CodeBlock = Markdig.Syntax.CodeBlock;

namespace MarkMello.Infrastructure.Markdown;

/// <summary>
/// Markdig-based parse layer for M3.
/// Даёт устойчивый AST CommonMark + common extensions, затем переводит его
/// в UI-agnostic document model.
/// </summary>
public sealed class MarkdigMarkdownDocumentRenderer : IMarkdownDocumentRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public RenderedMarkdownDocument Render(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return RenderedMarkdownDocument.Empty;
        }

        var document = MarkdigMarkdown.Parse(markdown, Pipeline);
        KeepMisplacedTaskListMarkersAsText(document, markdown);
        var blocks = ConvertBlocks(document, markdown);
        return new RenderedMarkdownDocument(blocks);
    }

    private static List<MarkdownBlock> ConvertBlocks(ContainerBlock container, string source)
    {
        var result = new List<MarkdownBlock>(container.Count);

        foreach (var block in container)
        {
            AddConvertedBlock(block, result, source);
        }

        return result;
    }

    private static void AddConvertedBlock(Block block, List<MarkdownBlock> target, string source)
    {
        switch (block)
        {
            case HeadingBlock heading:
                target.Add(WithSourceSpan(
                    new MarkdownHeadingBlock(
                        Math.Clamp(heading.Level, 1, 6),
                        ConvertInlines(heading.Inline)),
                    heading,
                    source));
                return;

            case ParagraphBlock paragraph:
                // A paragraph whose only meaningful inline node is an image
                // becomes a block-level image. This matches how authors write
                // "figure" style images as a standalone paragraph.
                if (TryExtractStandaloneImage(paragraph.Inline, out var standaloneImage))
                {
                    target.Add(WithSourceSpan(standaloneImage, paragraph, source));
                    return;
                }
                target.Add(WithSourceSpan(
                    new MarkdownParagraphBlock(ConvertInlines(paragraph.Inline)),
                    paragraph,
                    source));
                return;

            // AlertBlock — наследник QuoteBlock, поэтому ветка идёт раньше.
            case AlertBlock alert:
                target.Add(WithSourceSpan(ConvertAlert(alert, source), alert, source));
                return;

            case QuoteBlock quote:
                target.Add(WithSourceSpan(
                    new MarkdownQuoteBlock(ConvertBlocks(quote, source)),
                    quote,
                    source));
                return;

            case ListBlock list:
                target.Add(WithSourceSpan(ConvertList(list, source), list, source));
                return;

            case ThematicBreakBlock thematicBreak:
                target.Add(WithSourceSpan(new MarkdownHorizontalRuleBlock(), thematicBreak, source));
                return;

            case FencedCodeBlock fencedCode:
                target.Add(WithSourceSpan(
                    ConvertFencedCodeBlock(fencedCode),
                    fencedCode,
                    source));
                return;

            case CodeBlock codeBlock:
                target.Add(WithSourceSpan(
                    new MarkdownCodeBlock(null, ExtractCode(codeBlock)),
                    codeBlock,
                    source));
                return;

            case Table table:
                target.Add(WithSourceSpan(ConvertTable(table, source), table, source));
                return;

            case HtmlBlock htmlBlock:
                // We intentionally do NOT switch on htmlBlock.Type here --
                // Markdig's HtmlBlockType enum has changed names across
                // versions (ScriptBlock, ScriptTag, ScriptPreOrStyle...).
                // Instead we strip scripts/styles/comments/CDATA by content,
                // which is stable regardless of Markdig's internal classification.
                AppendHtmlBlock(htmlBlock.Lines.ToString(), target, CreateSourceSpan(htmlBlock, source));
                return;

            case ContainerBlock nested:
                foreach (var nestedBlock in ConvertBlocks(nested, source))
                {
                    target.Add(nestedBlock);
                }
                return;

            case LeafBlock leaf:
                var leafText = ExtractLeafText(leaf);
                if (!string.IsNullOrWhiteSpace(leafText))
                {
                    target.Add(WithSourceSpan(
                        new MarkdownParagraphBlock([
                            new MarkdownTextInline(leafText)
                        ]),
                        leaf,
                        source));
                }
                return;
        }
    }

    /// <summary>
    /// GitHub alert: Markdig разбирает <c>&gt; [!NOTE]</c> в <see cref="AlertBlock"/>,
    /// и маркер в текст не попадает. Вид Markdig принимает любой (<c>[!FOO]</c>),
    /// а GitHub — только пять, в любом регистре. С неизвестным видом цитата
    /// остаётся обычной, а маркер — текстом, как на GitHub.
    /// </summary>
    private static MarkdownQuoteBlock ConvertAlert(AlertBlock alert, string source)
    {
        var blocks = ConvertBlocks(alert, source);

        // Если на строке маркера больше ничего нет, а текст начинается через
        // пустую строку (или его нет совсем), Markdig оставляет от первой строки
        // пустой абзац.
        var markerParagraphIsEmpty = alert.Count > 0
            && alert[0] is ParagraphBlock paragraph
            && paragraph.Inline?.FirstChild is null;
        if (markerParagraphIsEmpty && blocks.Count > 0 && blocks[0] is MarkdownParagraphBlock { Inlines.Count: 0 })
        {
            blocks.RemoveAt(0);
        }

        var kindName = alert.Kind.ToString();
        if (TryGetAlertKind(kindName, out var kind))
        {
            return new MarkdownQuoteBlock(blocks, kind);
        }

        var marker = $"[!{kindName}]";
        if (!markerParagraphIsEmpty && blocks.Count > 0 && blocks[0] is MarkdownParagraphBlock first)
        {
            // Текст шёл со следующей строки того же абзаца: перевод строки внутри
            // абзаца — такой же пробел, как в остальном документе.
            blocks[0] = first with { Inlines = [new MarkdownTextInline(marker + " "), .. first.Inlines] };
        }
        else
        {
            blocks.Insert(0, new MarkdownParagraphBlock([new MarkdownTextInline(marker)]));
        }

        return new MarkdownQuoteBlock(blocks);
    }

    private static bool TryGetAlertKind(string name, out MarkdownAlertKind kind)
    {
        MarkdownAlertKind? parsed = name.ToUpperInvariant() switch
        {
            "NOTE" => MarkdownAlertKind.Note,
            "TIP" => MarkdownAlertKind.Tip,
            "IMPORTANT" => MarkdownAlertKind.Important,
            "WARNING" => MarkdownAlertKind.Warning,
            "CAUTION" => MarkdownAlertKind.Caution,
            _ => null
        };

        kind = parsed.GetValueOrDefault();
        return parsed is not null;
    }

    private static MarkdownListBlock ConvertList(ListBlock list, string source)
    {
        var items = new List<MarkdownListItem>(list.Count);

        foreach (var child in list)
        {
            if (child is not ListItemBlock item)
            {
                continue;
            }

            var isChecked = DetachTaskListMarker(item);
            var blocks = ConvertBlocks(item, source);
            if (isChecked is not null)
            {
                TrimTaskListParagraphStart(blocks);
            }

            items.Add(new MarkdownListItem(blocks, isChecked));
        }

        return new MarkdownListBlock(list.IsOrdered, items);
    }

    /// <summary>
    /// Markdig создаёт <see cref="TaskList"/> для <c>[ ]</c> / <c>[x]</c> в любом
    /// месте абзаца внутри пункта списка. Чекбоксом становится только маркер в
    /// начале пункта (<see cref="DetachTaskListMarker"/>), а остальные — например,
    /// в середине текста или в начале второго абзаца — заменяются исходным текстом,
    /// чтобы <c>[X]</c> и осталось <c>[X]</c>.
    /// </summary>
    private static void KeepMisplacedTaskListMarkersAsText(MarkdownDocument document, string source)
    {
        foreach (var taskList in document.Descendants<TaskList>().ToList())
        {
            if (!IsListItemMarker(taskList))
            {
                // Документ разбирается заново на каждый рендер, поэтому AST можно менять.
                taskList.ReplaceBy(new LiteralInline(source.Substring(taskList.Span.Start, taskList.Span.Length)));
            }
        }
    }

    private static bool IsListItemMarker(TaskList taskList)
        => taskList.Parent is { ParentBlock: ParagraphBlock { Parent: ListItemBlock item } paragraph }
            && ReferenceEquals(item[0], paragraph)
            && ReferenceEquals(paragraph.Inline?.FirstChild, taskList);

    /// <summary>
    /// Забирает маркер task list (<c>[ ]</c> / <c>[x]</c>) из начала первого абзаца
    /// пункта: отметка становится состоянием пункта, а не текстом.
    /// </summary>
    /// <returns>Состояние чекбокса или <c>null</c>, если пункт обычный.</returns>
    private static bool? DetachTaskListMarker(ListItemBlock item)
    {
        if (item.Count == 0
            || item[0] is not ParagraphBlock { Inline.FirstChild: TaskList taskList })
        {
            return null;
        }

        // Документ разбирается заново на каждый рендер, поэтому AST можно менять.
        taskList.Remove();
        return taskList.Checked;
    }

    /// <summary>
    /// Убирает пробел, который в исходнике отделял маркер task list от текста,
    /// а абзац, в котором кроме маркера ничего не было, — целиком.
    /// </summary>
    private static void TrimTaskListParagraphStart(List<MarkdownBlock> blocks)
    {
        if (blocks.Count == 0 || blocks[0] is not MarkdownParagraphBlock paragraph)
        {
            return;
        }

        var inlines = paragraph.Inlines.ToList();
        while (inlines.Count > 0 && inlines[0] is MarkdownTextInline text)
        {
            var trimmed = text.Text.TrimStart();
            if (trimmed.Length > 0)
            {
                inlines[0] = new MarkdownTextInline(trimmed);
                break;
            }

            inlines.RemoveAt(0);
        }

        if (inlines.Count == 0)
        {
            blocks.RemoveAt(0);
        }
        else
        {
            blocks[0] = paragraph with { Inlines = inlines };
        }
    }

    private static MarkdownTableBlock ConvertTable(Table table, string source)
    {
        var header = new List<MarkdownTableCell>();
        var rows = new List<IReadOnlyList<MarkdownTableCell>>();

        foreach (var child in table)
        {
            if (child is not TableRow row)
            {
                continue;
            }

            var cells = new List<MarkdownTableCell>(row.Count);
            foreach (var rowChild in row)
            {
                if (rowChild is not TableCell cell)
                {
                    continue;
                }

                cells.Add(new MarkdownTableCell(ConvertBlocksToInlines(cell, source)));
            }

            if (row.IsHeader)
            {
                header.AddRange(cells);
            }
            else
            {
                rows.Add(cells);
            }
        }

        return new MarkdownTableBlock(header, rows, ConvertColumnAlignments(table));
    }

    private static MarkdownTableColumnAlignment[] ConvertColumnAlignments(Table table)
    {
        var alignments = new MarkdownTableColumnAlignment[table.ColumnDefinitions.Count];
        for (var index = 0; index < alignments.Length; index++)
        {
            alignments[index] = table.ColumnDefinitions[index].Alignment switch
            {
                TableColumnAlign.Center => MarkdownTableColumnAlignment.Center,
                TableColumnAlign.Right => MarkdownTableColumnAlignment.Right,
                _ => MarkdownTableColumnAlignment.Left
            };
        }

        return alignments;
    }

    private static IReadOnlyList<MarkdownInline> ConvertBlocksToInlines(ContainerBlock container, string source)
    {
        var blocks = ConvertBlocks(container, source);
        if (blocks.Count == 0)
        {
            return Array.Empty<MarkdownInline>();
        }

        var result = new List<MarkdownInline>();
        var first = true;

        foreach (var block in blocks)
        {
            if (!first)
            {
                result.Add(new MarkdownLineBreakInline());
            }
            first = false;

            switch (block)
            {
                case MarkdownParagraphBlock paragraph:
                    AddInlineRange(result, paragraph.Inlines);
                    break;

                case MarkdownHeadingBlock heading:
                    AddInlineRange(result, heading.Inlines);
                    break;

                case MarkdownCodeBlock code:
                    result.Add(new MarkdownCodeInline(code.Code));
                    break;

                default:
                    var text = ExtractPlainText(block);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        result.Add(new MarkdownTextInline(text));
                    }
                    break;
            }
        }

        return result;
    }

    private static IReadOnlyList<MarkdownInline> ConvertInlines(ContainerInline? container)
    {
        if (container is null)
        {
            return Array.Empty<MarkdownInline>();
        }

        var result = new List<MarkdownInline>();

        foreach (var inline in container)
        {
            AddConvertedInline(inline, result);
        }

        return result;
    }

    private static void AddConvertedInline(Inline inline, List<MarkdownInline> target)
    {
        switch (inline)
        {
            case LiteralInline literal:
                var text = literal.Content.ToString();
                if (!string.IsNullOrEmpty(text))
                {
                    target.Add(new MarkdownTextInline(text));
                }
                return;

            case LineBreakInline lineBreak:
                if (lineBreak.IsHard || lineBreak.IsBackslash)
                {
                    target.Add(new MarkdownLineBreakInline());
                }
                else
                {
                    target.Add(new MarkdownTextInline(" "));
                }
                return;

            case CodeInline code:
                target.Add(new MarkdownCodeInline(code.Content.ToString()));
                return;

            case AutolinkInline autolink:
                var autolinkText = NormalizeNullable(autolink.Url) ?? string.Empty;
                if (autolinkText.Length == 0)
                {
                    return;
                }

                var autolinkUrl = autolink.IsEmail && !autolinkText.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
                    ? $"mailto:{autolinkText}"
                    : autolinkText;
                target.Add(new MarkdownLinkInline(
                    [new MarkdownTextInline(autolinkText)],
                    autolinkUrl,
                    null));
                return;

            case LinkInline link when link.IsImage:
                var altInlines = ConvertInlines(link);
                var altText = ExtractPlainText(altInlines);
                target.Add(new MarkdownImageInline(
                    NormalizeNullable(link.Url) ?? string.Empty,
                    string.IsNullOrWhiteSpace(altText) ? null : altText,
                    NormalizeNullable(link.Title)));
                return;

            case LinkInline link when !link.IsImage:
                var linkText = ConvertInlines(link);
                target.Add(new MarkdownLinkInline(
                    linkText,
                    NormalizeNullable(link.Url) ?? string.Empty,
                    NormalizeNullable(link.Title)));
                return;

            case EmphasisInline emphasis:
                var children = ConvertInlines(emphasis);
                if (emphasis.DelimiterChar == '~' && emphasis.DelimiterCount == 2)
                {
                    target.Add(new MarkdownStrikethroughInline(children));
                }
                else if (emphasis.DelimiterCount >= 2)
                {
                    target.Add(new MarkdownStrongInline(children));
                }
                else
                {
                    target.Add(new MarkdownEmphasisInline(children));
                }
                return;

            case HtmlEntityInline entityInline:
                var decoded = entityInline.Transcoded.ToString();
                if (!string.IsNullOrEmpty(decoded))
                {
                    target.Add(new MarkdownTextInline(decoded));
                }
                return;

            case HtmlInline htmlInline:
                HandleInlineHtmlTag(htmlInline.Tag, target);
                return;

            case ContainerInline nested:
                foreach (var child in ConvertInlines(nested))
                {
                    target.Add(child);
                }
                return;
        }
    }

    private static string ExtractCode(CodeBlock codeBlock)
    {
        var text = codeBlock.Lines.ToString();
        return text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');
    }

    private static MarkdownBlock ConvertFencedCodeBlock(FencedCodeBlock fencedCode)
    {
        // Markdig already splits the info line: Info is the first token
        // (the language/dialect), Arguments is the trimmed remainder.
        var token = NormalizeNullable(fencedCode.Info?.ToString());
        var arguments = NormalizeNullable(fencedCode.Arguments?.ToString());
        var code = ExtractCode(fencedCode);

        if (SupportedDiagramDialects.TryParseFenceToken(token, out var kind))
        {
            return new MarkdownDiagramBlock(kind, code, arguments);
        }

        // Preserve the legacy code-block info shape: keep dialect token plus
        // arguments together so existing code-block consumers don't lose the
        // remainder of an unknown info line.
        var legacyInfo = arguments is null
            ? token
            : token is null ? arguments : $"{token} {arguments}";

        return new MarkdownCodeBlock(legacyInfo, code);
    }

    private static string ExtractLeafText(LeafBlock block)
    {
        if (block.Inline is not null)
        {
            return ExtractPlainText(ConvertInlines(block.Inline));
        }

        return block.Lines.ToString();
    }

    private static void AddInlineRange(List<MarkdownInline> target, IReadOnlyList<MarkdownInline> inlines)
    {
        foreach (var inline in inlines)
        {
            target.Add(inline);
        }
    }

    private static string ExtractPlainText(MarkdownBlock block) => block switch
    {
        MarkdownParagraphBlock paragraph => ExtractPlainText(paragraph.Inlines),
        MarkdownHeadingBlock heading => ExtractPlainText(heading.Inlines),
        MarkdownCodeBlock code => code.Code,
        MarkdownQuoteBlock quote => string.Join(Environment.NewLine, quote.Blocks.Select(ExtractPlainText)),
        MarkdownListBlock list => string.Join(Environment.NewLine, list.Items.Select(item => string.Join(" ", item.Blocks.Select(ExtractPlainText)))),
        MarkdownTableBlock table => string.Join(Environment.NewLine, table.Rows.Select(row => string.Join(" | ", row.Select(cell => ExtractPlainText(cell.Inlines))))),
        _ => string.Empty
    };

    private static string ExtractPlainText(IReadOnlyList<MarkdownInline> inlines)
    {
        if (inlines.Count == 0)
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder();
        foreach (var inline in inlines)
        {
            AppendPlainText(inline, builder);
        }
        return builder.ToString();
    }

    private static void AppendPlainText(MarkdownInline inline, System.Text.StringBuilder builder)
    {
        switch (inline)
        {
            case MarkdownTextInline text:
                builder.Append(text.Text);
                break;
            case MarkdownStrongInline strong:
                AppendPlainText(strong.Inlines, builder);
                break;
            case MarkdownEmphasisInline emphasis:
                AppendPlainText(emphasis.Inlines, builder);
                break;
            case MarkdownStrikethroughInline strikethrough:
                AppendPlainText(strikethrough.Inlines, builder);
                break;
            case MarkdownCodeInline code:
                builder.Append(code.Code);
                break;
            case MarkdownImageInline image:
                builder.Append(GetImageInlinePlainText(image));
                break;
            case MarkdownLinkInline link:
                if (link.Inlines.Count > 0)
                {
                    AppendPlainText(link.Inlines, builder);
                }
                else if (!string.IsNullOrWhiteSpace(link.Url))
                {
                    builder.Append(link.Url);
                }
                break;
            case MarkdownLineBreakInline:
                builder.AppendLine();
                break;
        }
    }

    private static void AppendPlainText(IReadOnlyList<MarkdownInline> inlines, System.Text.StringBuilder builder)
    {
        foreach (var inline in inlines)
        {
            AppendPlainText(inline, builder);
        }
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

    private static string? NormalizeNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static MarkdownBlock WithSourceSpan(MarkdownBlock block, Block sourceBlock, string source)
        => WithSourceSpan(block, CreateSourceSpan(sourceBlock, source));

    private static MarkdownBlock WithSourceSpan(MarkdownBlock block, MarkdownSourceSpan? sourceSpan)
        => sourceSpan is null ? block : block with { SourceSpan = sourceSpan };

    private static MarkdownSourceSpan? CreateSourceSpan(Block block, string source)
    {
        int? startLine = block.Line >= 0 ? block.Line : null;
        int? endLine = startLine is null
            ? null
            : startLine.Value + CountSourceLineBreaks(block, source);

        if (block is ContainerBlock container)
        {
            foreach (var child in container)
            {
                var childSpan = CreateSourceSpan(child, source);
                if (childSpan is null)
                {
                    continue;
                }

                startLine = startLine is null
                    ? childSpan.Value.StartLine
                    : Math.Min(startLine.Value, childSpan.Value.StartLine);
                endLine = endLine is null
                    ? childSpan.Value.EndLine
                    : Math.Max(endLine.Value, childSpan.Value.EndLine);
            }
        }

        return startLine is null
            ? null
            : new MarkdownSourceSpan(startLine.Value, endLine ?? startLine.Value);
    }

    /// <summary>
    /// Сколько переводов строки блок занимает в исходнике.
    ///
    /// Считается по <see cref="MarkdownObject.Span"/> исходного текста, а не по
    /// <see cref="LeafBlock.Lines"/>: у абзацев и заголовков Markdig очищает
    /// Lines после разбора inline-содержимого, а у фенсед-блоков туда не входят
    /// строки ограждения. И то и другое давало span короче реального, из-за чего
    /// синхронизация скролла промахивалась мимо конца блока.
    /// </summary>
    private static int CountSourceLineBreaks(Block block, string source)
        => Math.Max(CountSpanLineBreaks(block, source), CountFenceLineBreaks(block));

    private static int CountSpanLineBreaks(Block block, string source)
    {
        var span = block.Span;
        if (span.Start < 0 || span.End < span.Start || span.End >= source.Length)
        {
            return 0;
        }

        var lineBreaks = 0;
        for (var index = span.Start; index <= span.End; index++)
        {
            if (source[index] == '\n')
            {
                lineBreaks++;
            }
        }

        // Завершающий перевод строки принадлежит последней строке блока,
        // а не следующей за ним.
        return source[span.End] == '\n' ? Math.Max(0, lineBreaks - 1) : lineBreaks;
    }

    /// <summary>
    /// Запасной подсчёт для фенсед-блока: у незакрытого ограждения Markdig не
    /// растягивает Span за строку открытия, а такой блок нормален во время
    /// набора текста.
    /// </summary>
    private static int CountFenceLineBreaks(Block block)
    {
        if (block is not FencedCodeBlock fenced)
        {
            return 0;
        }

        var contentLines = Math.Max(0, fenced.Lines.Count - 1);
        var closingFence = fenced.ClosingFencedCharCount > 0 ? 1 : 0;
        return 1 + contentLines + closingFence;
    }

    // --- HTML handling ----------------------------------------------------

    private static readonly Regex ImgTagPattern = new(
        @"<img\b[^>]*/?>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AltAttrPattern = new(
        @"\balt\s*=\s*(?:""([^""]*)""|'([^']*)')",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SrcAttrPattern = new(
        @"\bsrc\s*=\s*(?:""([^""]*)""|'([^']*)')",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TitleAttrPattern = new(
        @"\btitle\s*=\s*(?:""([^""]*)""|'([^']*)')",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex WidthAttrPattern = new(
        @"\bwidth\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s""'>/]+))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex HeightAttrPattern = new(
        @"\bheight\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s""'>/]+))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LineBreakTagPattern = new(
        @"^<br\b[^>]*/?>$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AnyTagPattern = new(
        @"<[^>]+>",
        RegexOptions.Compiled);

    private static readonly Regex WhitespacePattern = new(
        @"\s+",
        RegexOptions.Compiled);

    // Patterns that must strip their *entire* body, not just the opening/closing
    // tags. If we only stripped <script> without its content we would leak
    // executable code text into the reading view.
    private static readonly Regex ScriptBodyPattern = new(
        @"<script\b[^>]*>.*?</script\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex StyleBodyPattern = new(
        @"<style\b[^>]*>.*?</style\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex CommentBodyPattern = new(
        @"<!--.*?-->",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex CDataBodyPattern = new(
        @"<!\[CDATA\[.*?\]\]>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex ProcessingInstructionPattern = new(
        @"<\?.*?\?>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex DoctypePattern = new(
        @"<!DOCTYPE\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Splits an HTML block body into a sequence of document blocks. Each
    /// &lt;img&gt; in the source becomes its own <see cref="MarkdownImageBlock"/>;
    /// the surrounding HTML fragments (with container tags like &lt;p&gt;,
    /// &lt;div&gt;, &lt;picture&gt; stripped and entities decoded) become
    /// paragraphs when they carry any visible text. This lets common README
    /// patterns -- "figure with caption", "centered picture", etc. -- render
    /// as image + caption rather than a literal "[image: alt] caption" line.
    /// </summary>
    private static void AppendHtmlBlock(string html, List<MarkdownBlock> target, MarkdownSourceSpan? sourceSpan)
    {
        if (string.IsNullOrEmpty(html))
        {
            return;
        }

        // Drop things whose *content* must not appear in the viewer at all,
        // not just their surrounding tags: script bodies, style rules,
        // comments, CDATA, processing instructions, doctype. Doing this by
        // content (not by Markdig's HtmlBlockType enum) keeps the code
        // independent of Markdig version-specific enum names.
        var scrubbed = html;
        scrubbed = ScriptBodyPattern.Replace(scrubbed, string.Empty);
        scrubbed = StyleBodyPattern.Replace(scrubbed, string.Empty);
        scrubbed = CommentBodyPattern.Replace(scrubbed, string.Empty);
        scrubbed = CDataBodyPattern.Replace(scrubbed, string.Empty);
        scrubbed = ProcessingInstructionPattern.Replace(scrubbed, string.Empty);
        scrubbed = DoctypePattern.Replace(scrubbed, string.Empty);

        var imgMatches = ImgTagPattern.Matches(scrubbed);
        if (imgMatches.Count == 0)
        {
            AppendHtmlTextParagraph(scrubbed, target, sourceSpan);
            return;
        }

        var cursor = 0;
        foreach (Match match in imgMatches)
        {
            AppendHtmlTextParagraph(scrubbed[cursor..match.Index], target, sourceSpan);

            if (TryBuildImageBlockFromImgTag(match.Value, out var imageBlock))
            {
                target.Add(WithSourceSpan(imageBlock, sourceSpan));
            }
            else
            {
                // Malformed <img> (no src) -- keep the alt-text placeholder
                // so the author still sees that something was intended here.
                target.Add(WithSourceSpan(
                    new MarkdownParagraphBlock([
                        new MarkdownTextInline(FormatImagePlaceholder(match.Value))
                    ]),
                    sourceSpan));
            }

            cursor = match.Index + match.Length;
        }

        AppendHtmlTextParagraph(scrubbed[cursor..], target, sourceSpan);
    }

    private static void AppendHtmlTextParagraph(string htmlFragment, List<MarkdownBlock> target, MarkdownSourceSpan? sourceSpan)
    {
        if (string.IsNullOrEmpty(htmlFragment))
        {
            return;
        }

        // Strip every remaining tag (<picture>, <source>, <div>, <p>, <br>, ...),
        // decode HTML entities, collapse whitespace.
        var text = AnyTagPattern.Replace(htmlFragment, " ");
        text = WebUtility.HtmlDecode(text);
        text = WhitespacePattern.Replace(text, " ").Trim();

        if (text.Length == 0)
        {
            return;
        }

        target.Add(WithSourceSpan(
            new MarkdownParagraphBlock([new MarkdownTextInline(text)]),
            sourceSpan));
    }

    private static bool TryBuildImageBlockFromImgTag(string imgTag, out MarkdownImageBlock imageBlock)
    {
        imageBlock = null!;
        var src = ExtractAttr(SrcAttrPattern, imgTag);
        if (string.IsNullOrWhiteSpace(src))
        {
            return false;
        }

        var alt = ExtractAttr(AltAttrPattern, imgTag);
        var title = ExtractAttr(TitleAttrPattern, imgTag);
        var width = TryParseHtmlPixelDimension(ExtractAttr(WidthAttrPattern, imgTag));
        var height = TryParseHtmlPixelDimension(ExtractAttr(HeightAttrPattern, imgTag));
        imageBlock = new MarkdownImageBlock(
            Url: src,
            AltText: string.IsNullOrWhiteSpace(alt) ? null : alt,
            Title: string.IsNullOrWhiteSpace(title) ? null : title,
            Width: width,
            Height: height);
        return true;
    }

    private static void HandleInlineHtmlTag(string tag, List<MarkdownInline> target)
    {
        if (string.IsNullOrEmpty(tag))
        {
            return;
        }

        if (LineBreakTagPattern.IsMatch(tag))
        {
            // <br>, <br/>, <br /> should preserve the author's intended line break.
            target.Add(new MarkdownLineBreakInline());
            return;
        }

        if (ImgTagPattern.IsMatch(tag))
        {
            target.Add(new MarkdownTextInline(FormatImagePlaceholder(tag)));
            return;
        }

        // Generic container/opener/closer tags (<b>, </b>, <span>, ...): drop them.
        // The surrounding literal inlines already carry the visible text, so
        // skipping the tag text is equivalent to "strip tags, keep content".
    }

    private static string FormatImagePlaceholder(string imgTag)
    {
        var altMatch = AltAttrPattern.Match(imgTag);
        if (altMatch.Success)
        {
            var alt = altMatch.Groups[1].Success
                ? altMatch.Groups[1].Value
                : altMatch.Groups[2].Value;
            if (!string.IsNullOrWhiteSpace(alt))
            {
                return $"[image: {alt}]";
            }
        }

        return "[image]";
    }

    /// <summary>
    /// Detects the "figure" pattern: a paragraph whose only visible content
    /// is a single markdown image (![alt](url) optionally preceded/followed
    /// by whitespace or a line break). Returns the extracted image block.
    /// </summary>
    private static bool TryExtractStandaloneImage(
        ContainerInline? container,
        out MarkdownImageBlock imageBlock)
    {
        imageBlock = null!;
        if (container is null)
        {
            return false;
        }

        LinkInline? onlyImage = null;
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    // Allow whitespace-only literals around the image.
                    var text = literal.Content.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return false;
                    }
                    break;

                case LineBreakInline:
                    // A trailing soft break doesn't disqualify the figure pattern.
                    break;

                case LinkInline link when link.IsImage:
                    if (onlyImage is not null)
                    {
                        // Two or more images in the same paragraph -- keep
                        // them inline so the user's layout intent is clear.
                        return false;
                    }
                    onlyImage = link;
                    break;

                default:
                    // Anything else (emphasis, other links, code, nested HTML)
                    // means this paragraph is a real paragraph, not a figure.
                    return false;
            }
        }

        if (onlyImage is null)
        {
            return false;
        }

        var altText = ExtractPlainText(ConvertInlines(onlyImage));
        imageBlock = new MarkdownImageBlock(
            Url: NormalizeNullable(onlyImage.Url) ?? string.Empty,
            AltText: string.IsNullOrWhiteSpace(altText) ? null : altText,
            Title: NormalizeNullable(onlyImage.Title));
        return true;
    }

    private static string? ExtractAttr(Regex pattern, string tag)
    {
        var m = pattern.Match(tag);
        if (!m.Success)
        {
            return null;
        }

        for (var groupIndex = 1; groupIndex < m.Groups.Count; groupIndex++)
        {
            var group = m.Groups[groupIndex];
            if (!group.Success || string.IsNullOrWhiteSpace(group.Value))
            {
                continue;
            }

            return group.Value;
        }

        return null;
    }

    private static double? TryParseHtmlPixelDimension(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^2].TrimEnd();
        }

        if (normalized.Contains('%', StringComparison.Ordinal)
            || normalized.Contains("calc(", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("var(", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return double.TryParse(
                normalized,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed)
            && parsed > 0
                ? parsed
                : null;
    }
}
