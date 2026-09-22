using MarkMello.Domain;

namespace MarkMello.Presentation.Clipboard;

internal static class MarkdownSelectionLinkCollector
{
    public static IReadOnlyList<string> Collect(
        IReadOnlyList<MarkdownBlock> blocks,
        MarkdownSelectionFormatContext context)
    {
        var urls = new List<string>();
        for (var index = 0; index < blocks.Count; index++)
        {
            CollectBlockLinkUrls(blocks[index], $"b{index}", context, urls);
        }

        return urls;
    }

    private static void CollectBlockLinkUrls(
        MarkdownBlock block,
        string path,
        MarkdownSelectionFormatContext context,
        List<string> urls)
    {
        switch (block)
        {
            case MarkdownHeadingBlock heading:
                CollectInlineLinkUrls(heading.Inlines, path, context, urls);
                break;

            case MarkdownParagraphBlock paragraph:
                CollectInlineLinkUrls(paragraph.Inlines, path, context, urls);
                break;

            case MarkdownQuoteBlock quote:
                for (var index = 0; index < quote.Blocks.Count; index++)
                {
                    CollectBlockLinkUrls(quote.Blocks[index], $"{path}.b{index}", context, urls);
                }
                break;

            case MarkdownListBlock list:
                for (var itemIndex = 0; itemIndex < list.Items.Count; itemIndex++)
                {
                    var item = list.Items[itemIndex];
                    for (var blockIndex = 0; blockIndex < item.Blocks.Count; blockIndex++)
                    {
                        CollectBlockLinkUrls(item.Blocks[blockIndex], $"{path}.i{itemIndex}.b{blockIndex}", context, urls);
                    }
                }
                break;

            case MarkdownDefinitionListBlock definitionList:
                for (var itemIndex = 0; itemIndex < definitionList.Items.Count; itemIndex++)
                {
                    var item = definitionList.Items[itemIndex];
                    for (var termIndex = 0; termIndex < item.Terms.Count; termIndex++)
                    {
                        CollectInlineLinkUrls(item.Terms[termIndex].Inlines, $"{path}.i{itemIndex}.t{termIndex}", context, urls);
                    }

                    for (var definitionIndex = 0; definitionIndex < item.Definitions.Count; definitionIndex++)
                    {
                        var definition = item.Definitions[definitionIndex];
                        for (var blockIndex = 0; blockIndex < definition.Blocks.Count; blockIndex++)
                        {
                            CollectBlockLinkUrls(definition.Blocks[blockIndex], $"{path}.i{itemIndex}.d{definitionIndex}.b{blockIndex}", context, urls);
                        }
                    }
                }
                break;

            case MarkdownFootnotesBlock footnotes:
                for (var footnoteIndex = 0; footnoteIndex < footnotes.Footnotes.Count; footnoteIndex++)
                {
                    var footnote = footnotes.Footnotes[footnoteIndex];
                    for (var blockIndex = 0; blockIndex < footnote.Blocks.Count; blockIndex++)
                    {
                        CollectBlockLinkUrls(footnote.Blocks[blockIndex], $"{path}.f{footnoteIndex}.b{blockIndex}", context, urls);
                    }
                }
                break;

            case MarkdownTableBlock table:
                for (var cellIndex = 0; cellIndex < table.Header.Count; cellIndex++)
                {
                    CollectInlineLinkUrls(table.Header[cellIndex].Inlines, $"{path}.h{cellIndex}", context, urls);
                }

                for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
                {
                    var row = table.Rows[rowIndex];
                    for (var cellIndex = 0; cellIndex < row.Count; cellIndex++)
                    {
                        CollectInlineLinkUrls(row[cellIndex].Inlines, $"{path}.r{rowIndex}.c{cellIndex}", context, urls);
                    }
                }
                break;
        }
    }

    private static void CollectInlineLinkUrls(
        IReadOnlyList<MarkdownInline> inlines,
        string path,
        MarkdownSelectionFormatContext context,
        List<string> urls)
    {
        if (!context.TryGetLocalSelection(path, out _, out var selectedRange))
        {
            return;
        }

        CollectInlineLinkUrls(inlines, selectedRange, urls);
    }

    private static void CollectInlineLinkUrls(
        IReadOnlyList<MarkdownInline> inlines,
        DocumentTextRange selectedRange,
        List<string> urls)
    {
        var offset = 0;
        foreach (var inline in inlines)
        {
            var length = MarkdownClipboardTextHelpers.GetPlainTextLength(inline);
            if (length == 0)
            {
                continue;
            }

            var inlineRange = new DocumentTextRange(offset, offset + length);
            var localRange = selectedRange.Intersection(inlineRange);
            if (!localRange.IsEmpty)
            {
                CollectInlineLinkUrls(
                    inline,
                    new DocumentTextRange(
                        localRange.Start - inlineRange.Start,
                        localRange.End - inlineRange.Start),
                    urls);
            }

            offset += length;
        }
    }

    private static void CollectInlineLinkUrls(
        MarkdownInline inline,
        DocumentTextRange selectedRange,
        List<string> urls)
    {
        switch (inline)
        {
            case MarkdownLinkInline link when !selectedRange.IsEmpty && !string.IsNullOrWhiteSpace(link.Url):
                urls.Add(link.Url);
                break;

            case MarkdownStrongInline strong:
                CollectInlineLinkUrls(strong.Inlines, selectedRange, urls);
                break;

            case MarkdownEmphasisInline emphasis:
                CollectInlineLinkUrls(emphasis.Inlines, selectedRange, urls);
                break;

            case MarkdownStrikethroughInline strikethrough:
                CollectInlineLinkUrls(strikethrough.Inlines, selectedRange, urls);
                break;

            case MarkdownHighlightInline highlight:
                CollectInlineLinkUrls(highlight.Inlines, selectedRange, urls);
                break;

            case MarkdownSubscriptInline subscript:
                CollectInlineLinkUrls(subscript.Inlines, selectedRange, urls);
                break;

            case MarkdownSuperscriptInline superscript:
                CollectInlineLinkUrls(superscript.Inlines, selectedRange, urls);
                break;
        }
    }
}
