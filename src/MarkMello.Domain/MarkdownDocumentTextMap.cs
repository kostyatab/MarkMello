using System.Globalization;
using System.Text;

namespace MarkMello.Domain;

/// <summary>
/// Канонический plain-text view документа для document-wide selection и copy semantics.
/// В текущей итерации покрывает весь документ как текстовый поток и даёт стабильные offset-ы
/// для simple blocks; advanced fragments будут дорабатываться в следующих slice.
/// </summary>
public sealed class MarkdownDocumentTextMap
{
    private readonly Dictionary<string, MarkdownDocumentTextFragment> _fragmentsByKey;

    private MarkdownDocumentTextMap(string text, IReadOnlyList<MarkdownDocumentTextFragment> fragments)
    {
        Text = text;
        Fragments = fragments;
        _fragmentsByKey = fragments.ToDictionary(static fragment => fragment.Key, StringComparer.Ordinal);
    }

    // Римские цифры от больших к меньшим, с вычитающими парами.
    private static readonly (int Value, string Symbol)[] RomanDigits =
    [
        (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"),
        (100, "C"), (90, "XC"), (50, "L"), (40, "XL"),
        (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")
    ];

    public static MarkdownDocumentTextMap Empty { get; } = new(string.Empty, Array.Empty<MarkdownDocumentTextFragment>());

    public string Text { get; }

    public IReadOnlyList<MarkdownDocumentTextFragment> Fragments { get; }

    public bool TryGetFragment(string key, out MarkdownDocumentTextFragment fragment)
        => _fragmentsByKey.TryGetValue(key, out fragment!);

    public string GetText(DocumentTextRange range)
    {
        if (Text.Length == 0 || range.IsEmpty)
        {
            return string.Empty;
        }

        var start = Math.Clamp(range.Start, 0, Text.Length);
        var end = Math.Clamp(range.End, start, Text.Length);
        return end <= start ? string.Empty : Text[start..end];
    }

    /// <param name="document">Документ.</param>
    /// <param name="alertTitle">
    /// Заголовок GitHub alert в text flow — тот, что показывает viewer (он
    /// локализован). Без него — <see cref="GetDefaultAlertTitle"/>. Карта, по
    /// которой считают выделение, и карта, по которой его копируют, должны
    /// строиться с одними и теми же заголовками, иначе offset'ы разойдутся.
    /// </param>
    public static MarkdownDocumentTextMap Create(
        RenderedMarkdownDocument document,
        Func<MarkdownAlertKind, string>? alertTitle = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.Blocks.Count == 0)
        {
            return Empty;
        }

        var builder = new Builder(alertTitle ?? GetDefaultAlertTitle);
        for (var index = 0; index < document.Blocks.Count; index++)
        {
            builder.AppendBlock(document.Blocks[index], $"b{index}", isTopLevel: true);
        }

        return builder.Build();
    }

    /// <summary>
    /// Маркер пункта списка в text flow — тот же, что на экране. Маркированный
    /// список по уровню: «• », «◦ », с третьего уровня «▪ ». Нумерованный — номер
    /// от <see cref="MarkdownListBlock.StartNumber"/> в виде, который написал автор
    /// (<c>a.</c>, <c>A.</c>, <c>i.</c>, <c>I.</c>), а у списка, написанного
    /// цифрами, — по уровню: «1. », «a. », с третьего уровня «i. ». В
    /// маркированном списке чекбокс task list встаёт на место маркера, поэтому у
    /// такого пункта маркера нет; в нумерованном номер остаётся и идёт перед
    /// чекбоксом.
    /// </summary>
    /// <param name="list">Список.</param>
    /// <param name="itemIndex">Номер пункта в списке с нуля.</param>
    /// <param name="level">
    /// Уровень списка с нуля среди списков того же вида вокруг него
    /// (<see cref="MarkdownListNesting.LevelOf"/>).
    /// </param>
    public static string GetListMarkerText(MarkdownListBlock list, int itemIndex, int level)
    {
        ArgumentNullException.ThrowIfNull(list);

        if (list.IsOrdered)
        {
            return FormatListNumber(list.StartNumber + itemIndex, GetShownNumbering(list, level)) + ". ";
        }

        if (list.Items[itemIndex].IsChecked is not null)
        {
            return string.Empty;
        }

        return level switch
        {
            0 => "• ",
            1 => "◦ ",
            _ => "▪ "
        };
    }

    /// <summary>
    /// Вид номеров на экране: вид автора, если он написал буквы или римские
    /// цифры, а у списка из цифр — по уровню: 1 → a → i.
    /// </summary>
    private static MarkdownListNumbering GetShownNumbering(MarkdownListBlock list, int level)
        => list.Numbering != MarkdownListNumbering.Digits
            ? list.Numbering
            : level switch
            {
                0 => MarkdownListNumbering.Digits,
                1 => MarkdownListNumbering.LowerAlpha,
                _ => MarkdownListNumbering.LowerRoman
            };

    /// <summary>
    /// Номер в виде нумерации, как <c>list-style-type</c> в CSS: буквы после
    /// «z» идут «aa», «ab»…, римские — от 1 до 3999. Номер, которого в этом виде
    /// нет (0 буквой, 4000 римскими), пишется цифрами.
    /// </summary>
    public static string FormatListNumber(int number, MarkdownListNumbering numbering) => numbering switch
    {
        MarkdownListNumbering.LowerAlpha when number > 0 => FormatAlpha(number, 'a'),
        MarkdownListNumbering.UpperAlpha when number > 0 => FormatAlpha(number, 'A'),
        MarkdownListNumbering.LowerRoman when number is > 0 and < 4000 => FormatRoman(number, upperCase: false),
        MarkdownListNumbering.UpperRoman when number is > 0 and < 4000 => FormatRoman(number, upperCase: true),
        _ => number.ToString(CultureInfo.InvariantCulture)
    };

    private static string FormatAlpha(int number, char first)
    {
        var letters = new StringBuilder();
        for (var rest = number; rest > 0; rest = (rest - 1) / 26)
        {
            letters.Insert(0, (char)(first + (rest - 1) % 26));
        }

        return letters.ToString();
    }

    private static string FormatRoman(int number, bool upperCase)
    {
        var roman = new StringBuilder();
        var rest = number;
        foreach (var (value, symbol) in RomanDigits)
        {
            for (; rest >= value; rest -= value)
            {
                foreach (var letter in symbol)
                {
                    roman.Append(upperCase ? letter : char.ToLowerInvariant(letter));
                }
            }
        }

        return roman.ToString();
    }

    /// <summary>
    /// Чекбокс task list в text flow. На экране это иконка, а в выделение,
    /// поиск и буфер обмена идёт этот текст — так в скопированном списке видно,
    /// какие пункты отмечены.
    /// </summary>
    public static string GetTaskCheckboxText(bool isChecked) => isChecked ? "☑ " : "☐ ";

    /// <summary>
    /// Метка сноски в text flow: «[1]». На экране это номер верхним индексом, а в
    /// выделение, поиск и буфер обмена идёт этот текст — в скопированном тексте метка
    /// не теряется и не сливается с соседним числом.
    /// </summary>
    public static string GetFootnoteReferenceText(int number)
        => string.Create(CultureInfo.InvariantCulture, $"[{number}]");

    /// <summary>
    /// Номер сноски в блоке сносок: «1 » — без точки, как на экране; пробел
    /// отделяет номер от текста сноски в скопированном тексте.
    /// </summary>
    public static string GetFootnoteMarkerText(int number)
        => string.Create(CultureInfo.InvariantCulture, $"{number} ");

    /// <summary>
    /// Заголовок GitHub alert, когда локализованного нет: имя вида, как его пишет
    /// GitHub, — «Note», «Tip», «Important», «Warning», «Caution».
    /// </summary>
    public static string GetDefaultAlertTitle(MarkdownAlertKind kind) => kind switch
    {
        MarkdownAlertKind.Note => "Note",
        MarkdownAlertKind.Tip => "Tip",
        MarkdownAlertKind.Important => "Important",
        MarkdownAlertKind.Warning => "Warning",
        MarkdownAlertKind.Caution => "Caution",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    public static string ExtractPlainText(IReadOnlyList<MarkdownInline> inlines)
    {
        ArgumentNullException.ThrowIfNull(inlines);

        if (inlines.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        AppendPlainText(inlines, builder);
        return builder.ToString();
    }

    public static string ExtractPlainText(MarkdownBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);

        return block switch
        {
            MarkdownHeadingBlock heading => ExtractPlainText(heading.Inlines),
            MarkdownParagraphBlock paragraph => ExtractPlainText(paragraph.Inlines),
            MarkdownQuoteBlock quote => string.Join(Environment.NewLine, quote.Blocks.Select(ExtractPlainText)),
            MarkdownListBlock list => string.Join(Environment.NewLine, list.Items.Select(static item => string.Join(Environment.NewLine, item.Blocks.Select(ExtractPlainText)))),
            MarkdownDefinitionListBlock definitionList => string.Join(Environment.NewLine, EnumerateDefinitionListLines(definitionList)),
            MarkdownHorizontalRuleBlock => string.Empty,
            MarkdownImageBlock => string.Empty,
            MarkdownDiagramBlock => string.Empty,
            MarkdownCodeBlock code => code.Code,
            MarkdownTableBlock table => ExtractPlainText(table),
            MarkdownFrontMatterBlock frontMatter => ExtractPlainText(frontMatter),
            MarkdownFootnotesBlock footnotes => string.Join(Environment.NewLine, footnotes.Footnotes.Select(static footnote =>
                GetFootnoteMarkerText(footnote.Number) + string.Join(Environment.NewLine, footnote.Blocks.Select(ExtractPlainText)))),
            _ => block.ToString() ?? string.Empty
        };
    }

    /// <summary>Термин строкой, его определения — строками под ним.</summary>
    private static IEnumerable<string> EnumerateDefinitionListLines(MarkdownDefinitionListBlock definitionList)
    {
        foreach (var item in definitionList.Items)
        {
            foreach (var term in item.Terms)
            {
                yield return ExtractPlainText(term.Inlines);
            }

            foreach (var definition in item.Definitions)
            {
                foreach (var block in definition.Blocks)
                {
                    yield return ExtractPlainText(block);
                }
            }
        }
    }

    private static string ExtractPlainText(MarkdownTableBlock table)
    {
        var lines = new List<string>();

        if (table.Header.Count > 0)
        {
            lines.Add(string.Join("\t", table.Header.Select(static cell => ExtractPlainText(cell.Inlines))));
        }

        foreach (var row in table.Rows)
        {
            lines.Add(string.Join("\t", row.Select(static cell => ExtractPlainText(cell.Inlines))));
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Тот же текст, что у таблицы без строки заголовка: ключ и значение через
    /// табуляцию, пары через перевод строки.
    /// </summary>
    private static string ExtractPlainText(MarkdownFrontMatterBlock frontMatter)
        => string.Join(
            Environment.NewLine,
            frontMatter.Entries.Select(static entry => entry.Key + "\t" + entry.Value));

    private static void AppendPlainText(IReadOnlyList<MarkdownInline> inlines, StringBuilder builder)
    {
        foreach (var inline in inlines)
        {
            AppendPlainText(inline, builder);
        }
    }

    private static void AppendPlainText(MarkdownInline inline, StringBuilder builder)
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

            case MarkdownHighlightInline highlight:
                AppendPlainText(highlight.Inlines, builder);
                break;

            case MarkdownSubscriptInline subscript:
                AppendPlainText(subscript.Inlines, builder);
                break;

            case MarkdownSuperscriptInline superscript:
                AppendPlainText(superscript.Inlines, builder);
                break;

            case MarkdownCodeInline code:
                builder.Append(code.Code);
                break;

            case MarkdownKeyboardInline keyboard:
                builder.Append(keyboard.Text);
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
                builder.Append('\n');
                break;

            case MarkdownFootnoteReferenceInline footnote:
                builder.Append(GetFootnoteReferenceText(footnote.Number));
                break;
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

    private sealed class Builder(Func<MarkdownAlertKind, string> alertTitle)
    {
        private readonly StringBuilder _text = new();
        private readonly List<MarkdownDocumentTextFragment> _fragments = new();
        private MarkdownListNesting _listNesting;

        public MarkdownDocumentTextMap Build()
            => _text.Length == 0 && _fragments.Count == 0
                ? Empty
                : new MarkdownDocumentTextMap(_text.ToString(), _fragments);

        public void AppendBlock(MarkdownBlock block, string path, bool isTopLevel)
        {
            switch (block)
            {
                case MarkdownHeadingBlock heading:
                    AppendInlineFragment(path, MarkdownDocumentTextFragmentKind.Heading, heading.Inlines);
                    AppendBlockSeparator(doubleBreak: true);
                    return;

                case MarkdownParagraphBlock paragraph:
                    AppendInlineFragment(path, MarkdownDocumentTextFragmentKind.Paragraph, paragraph.Inlines);
                    AppendBlockSeparator(doubleBreak: isTopLevel);
                    return;

                case MarkdownQuoteBlock quote:
                    if (quote.AlertKind is { } alertKind
                        && alertTitle(alertKind) is { Length: > 0 } title)
                    {
                        // Заголовок alert — отдельная строка перед телом.
                        AppendTextFragment($"{path}.a", MarkdownDocumentTextFragmentKind.AlertTitle, title);
                        EnsureSingleLineBreakAtEnd();
                    }

                    for (var index = 0; index < quote.Blocks.Count; index++)
                    {
                        AppendBlock(quote.Blocks[index], $"{path}.b{index}", isTopLevel: false);
                    }

                    if (isTopLevel)
                    {
                        AppendBlockSeparator(doubleBreak: true);
                    }
                    else
                    {
                        EnsureSingleLineBreakAtEnd();
                    }
                    return;

                case MarkdownListBlock list:
                    var outerNesting = _listNesting;
                    var level = outerNesting.LevelOf(list);
                    _listNesting = outerNesting.Enter(list);
                    for (var itemIndex = 0; itemIndex < list.Items.Count; itemIndex++)
                    {
                        var item = list.Items[itemIndex];
                        AppendTextFragment(
                            $"{path}.i{itemIndex}.m",
                            MarkdownDocumentTextFragmentKind.ListMarker,
                            GetListMarkerText(list, itemIndex, level));

                        if (item.IsChecked is { } isChecked)
                        {
                            AppendTextFragment(
                                $"{path}.i{itemIndex}.t",
                                MarkdownDocumentTextFragmentKind.TaskCheckbox,
                                GetTaskCheckboxText(isChecked));
                        }

                        for (var blockIndex = 0; blockIndex < item.Blocks.Count; blockIndex++)
                        {
                            AppendBlock(item.Blocks[blockIndex], $"{path}.i{itemIndex}.b{blockIndex}", isTopLevel: false);
                        }

                        if (itemIndex < list.Items.Count - 1)
                        {
                            EnsureSingleLineBreakAtEnd();
                        }
                    }

                    _listNesting = outerNesting;
                    AppendBlockSeparator(doubleBreak: true);
                    return;

                case MarkdownDefinitionListBlock definitionList:
                    // Термин строкой, определения строками под ним.
                    for (var itemIndex = 0; itemIndex < definitionList.Items.Count; itemIndex++)
                    {
                        var item = definitionList.Items[itemIndex];
                        for (var termIndex = 0; termIndex < item.Terms.Count; termIndex++)
                        {
                            AppendInlineFragment(
                                $"{path}.i{itemIndex}.t{termIndex}",
                                MarkdownDocumentTextFragmentKind.Paragraph,
                                item.Terms[termIndex].Inlines);
                            EnsureSingleLineBreakAtEnd();
                        }

                        for (var definitionIndex = 0; definitionIndex < item.Definitions.Count; definitionIndex++)
                        {
                            var definition = item.Definitions[definitionIndex];
                            for (var blockIndex = 0; blockIndex < definition.Blocks.Count; blockIndex++)
                            {
                                AppendBlock(
                                    definition.Blocks[blockIndex],
                                    $"{path}.i{itemIndex}.d{definitionIndex}.b{blockIndex}",
                                    isTopLevel: false);
                            }
                        }
                    }

                    AppendBlockSeparator(doubleBreak: true);
                    return;

                case MarkdownFootnotesBlock footnotes:
                    // Как нумерованный список: номер, затем содержимое сноски.
                    for (var footnoteIndex = 0; footnoteIndex < footnotes.Footnotes.Count; footnoteIndex++)
                    {
                        var footnote = footnotes.Footnotes[footnoteIndex];
                        AppendTextFragment(
                            $"{path}.f{footnoteIndex}.m",
                            MarkdownDocumentTextFragmentKind.FootnoteMarker,
                            GetFootnoteMarkerText(footnote.Number));

                        for (var blockIndex = 0; blockIndex < footnote.Blocks.Count; blockIndex++)
                        {
                            AppendBlock(footnote.Blocks[blockIndex], $"{path}.f{footnoteIndex}.b{blockIndex}", isTopLevel: false);
                        }

                        if (footnoteIndex < footnotes.Footnotes.Count - 1)
                        {
                            EnsureSingleLineBreakAtEnd();
                        }
                    }

                    AppendBlockSeparator(doubleBreak: true);
                    return;

                case MarkdownHorizontalRuleBlock:
                    AppendBlockSeparator(doubleBreak: true);
                    return;

                case MarkdownCodeBlock code:
                    AppendTextFragment(path, MarkdownDocumentTextFragmentKind.CodeBlock, code.Code);
                    AppendBlockSeparator(doubleBreak: true);
                    return;

                case MarkdownTableBlock table:
                    AppendTable(table, path);
                    AppendBlockSeparator(doubleBreak: true);
                    return;

                case MarkdownFrontMatterBlock frontMatter:
                    AppendFrontMatter(frontMatter, path);
                    AppendBlockSeparator(doubleBreak: true);
                    return;

                case MarkdownImageBlock:
                    // Image blocks participate in vertical rhythm but not in
                    // the text stream. Copying "para above, image, para below"
                    // yields the two paragraphs joined by a paragraph break.
                    AppendBlockSeparator(doubleBreak: true);
                    return;

                case MarkdownDiagramBlock:
                    // Diagram blocks follow image-block selection semantics
                    // (ADR-0005 section 8): a successfully rendered diagram
                    // is not part of the continuous text flow. Final copy/
                    // selection behavior for error blocks is refined in M6.
                    AppendBlockSeparator(doubleBreak: true);
                    return;

                default:
                    var fallbackText = ExtractPlainText(block);
                    AppendTextFragment(path, MarkdownDocumentTextFragmentKind.Fallback, fallbackText);
                    AppendBlockSeparator(doubleBreak: true);
                    return;
            }
        }

        private void AppendTable(MarkdownTableBlock table, string path)
        {
            if (table.Header.Count > 0)
            {
                AppendTableRow(table.Header, $"{path}.h");
            }

            for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
            {
                if (_text.Length > 0 && _text[^1] != '\n')
                {
                    _text.Append('\n');
                }

                AppendTableRow(table.Rows[rowIndex], $"{path}.r{rowIndex}.c");
            }
        }

        /// <summary>
        /// Front matter идёт в карту как таблица без строки заголовка: те же пути
        /// фрагментов и тот же вид <see cref="MarkdownDocumentTextFragmentKind.TableCell"/>,
        /// чтобы выделение, поиск и копирование не отличались от табличных.
        /// </summary>
        private void AppendFrontMatter(MarkdownFrontMatterBlock frontMatter, string path)
        {
            for (var entryIndex = 0; entryIndex < frontMatter.Entries.Count; entryIndex++)
            {
                if (_text.Length > 0 && _text[^1] != '\n')
                {
                    _text.Append('\n');
                }

                var entry = frontMatter.Entries[entryIndex];
                AppendTextFragment($"{path}.r{entryIndex}.c0", MarkdownDocumentTextFragmentKind.TableCell, entry.Key);
                _text.Append('\t');
                AppendTextFragment($"{path}.r{entryIndex}.c1", MarkdownDocumentTextFragmentKind.TableCell, entry.Value);
            }
        }

        private void AppendTableRow(IReadOnlyList<MarkdownTableCell> cells, string pathPrefix)
        {
            for (var cellIndex = 0; cellIndex < cells.Count; cellIndex++)
            {
                if (cellIndex > 0)
                {
                    _text.Append('\t');
                }

                AppendInlineFragment($"{pathPrefix}{cellIndex}", MarkdownDocumentTextFragmentKind.TableCell, cells[cellIndex].Inlines);
            }
        }

        private void AppendInlineFragment(string key, MarkdownDocumentTextFragmentKind kind, IReadOnlyList<MarkdownInline> inlines)
            => AppendTextFragment(key, kind, ExtractPlainText(inlines));

        private void AppendTextFragment(string key, MarkdownDocumentTextFragmentKind kind, string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var start = _text.Length;
            _text.Append(text);
            var range = new DocumentTextRange(start, _text.Length);
            _fragments.Add(new MarkdownDocumentTextFragment(key, kind, range, text));
        }

        private void AppendBlockSeparator(bool doubleBreak)
        {
            if (_text.Length == 0)
            {
                return;
            }

            if (doubleBreak)
            {
                TrimTrailingLineBreaks(maxAllowed: 0);
                _text.Append('\n');
                _text.Append('\n');
            }
            else
            {
                EnsureSingleLineBreakAtEnd();
            }
        }

        private void EnsureSingleLineBreakAtEnd()
        {
            if (_text.Length == 0)
            {
                return;
            }

            TrimTrailingLineBreaks(maxAllowed: 0);
            _text.Append('\n');
        }

        private void TrimTrailingLineBreaks(int maxAllowed)
        {
            var trailing = 0;
            for (var index = _text.Length - 1; index >= 0 && _text[index] == '\n'; index--)
            {
                trailing++;
            }

            while (trailing > maxAllowed)
            {
                _text.Length--;
                trailing--;
            }
        }
    }
}
