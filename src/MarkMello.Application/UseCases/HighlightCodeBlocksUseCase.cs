using MarkMello.Application.Abstractions;
using MarkMello.Domain;

namespace MarkMello.Application.UseCases;

/// <summary>
/// Докраска блоков кода после показа документа (ADR-0010 §4): проходит блоки,
/// включая вложенные в цитаты, списки, сноски и определения, и возвращает
/// документ, у блоков кода которого заполнены <see cref="MarkdownCodeBlock.Tokens"/>.
///
/// Результаты разбора кэшируются по паре «язык + текст»: превью режима правки
/// и повторное открытие документа получают цвета неизменившихся блоков сразу.
/// Блоки без метки, с неизвестной меткой и длиннее порогов остаются без
/// токенов — выглядят как раньше. Документ без изменений возвращается тем же
/// экземпляром.
/// </summary>
public sealed class HighlightCodeBlocksUseCase
{
    public const int MaxCodeLength = 50_000;
    public const int MaxLineCount = 2_000;
    public const int CacheCapacity = 512;

    /// <summary>
    /// Сколько символов кода держит кэш: правка большого блока в превью кладёт
    /// в него каждую новую версию блока, и счёт только по записям удержал бы
    /// десятки мегабайт.
    /// </summary>
    public const int CacheCharacterBudget = 1_000_000;
    public static readonly TimeSpan BlockTimeout = TimeSpan.FromMilliseconds(500);

    private readonly ICodeHighlighter _highlighter;
    private readonly HighlightCache _cache = new(CacheCapacity, CacheCharacterBudget);

    public HighlightCodeBlocksUseCase(ICodeHighlighter highlighter)
    {
        ArgumentNullException.ThrowIfNull(highlighter);
        _highlighter = highlighter;
    }

    /// <summary>
    /// Есть ли в документе блок, который стоит отдать движку: с меткой языка,
    /// в пределах порогов, ещё без токенов и без готового ответа в кэше.
    /// Движок не вызывается — проверка дешёвая, годится для UI-потока.
    /// </summary>
    public bool NeedsHighlighting(RenderedMarkdownDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return ContainsBlock(document.Blocks, block =>
            block.Tokens is null
            && TryGetLanguage(block, out var language)
            && !_cache.Contains(language, block.Code));
    }

    /// <summary>
    /// Подставляет токены только из кэша, без движка — для UI-потока.
    /// </summary>
    public RenderedMarkdownDocument ApplyCached(RenderedMarkdownDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Rewrite(document, block =>
            TryGetLanguage(block, out var language) && _cache.TryGet(language, block.Code, out var tokens)
                ? tokens
                : null);
    }

    /// <summary>
    /// Подсвечивает все подходящие блоки. Вызывается вне UI-потока.
    /// </summary>
    /// <exception cref="OperationCanceledException">Документ сменился.</exception>
    public RenderedMarkdownDocument Execute(RenderedMarkdownDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Rewrite(document, block =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return TryGetLanguage(block, out var language)
                ? Highlight(language, block.Code, cancellationToken)
                : null;
        });
    }

    private IReadOnlyList<MarkdownCodeToken>? Highlight(string language, string code, CancellationToken cancellationToken)
    {
        if (_cache.TryGet(language, code, out var cached))
        {
            return cached;
        }

        IReadOnlyList<MarkdownCodeToken>? tokens;
        try
        {
            tokens = _highlighter.Highlight(language, code, BlockTimeout, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TimeoutException)
        {
            // Таймаут зависит от нагрузки машины: блок сейчас без цветов, но
            // ответ не запоминается — следующая докраска попробует снова.
            return null;
        }
        catch (Exception)
        {
            // Ошибка движка — блок просто остаётся без цветов (ADR-0010 §5);
            // запоминаем ответ, чтобы не повторять разбор на каждой правке.
            tokens = null;
        }

        _cache.Add(language, code, tokens);
        return tokens;
    }

    /// <summary>
    /// Язык блока — первое слово метки в нижнем регистре. Блок без метки или за
    /// порогами не подсвечивается.
    /// </summary>
    private static bool TryGetLanguage(MarkdownCodeBlock block, out string language)
    {
        language = string.Empty;
        var info = block.Info.AsSpan().Trim();
        if (info.IsEmpty || block.Code.Length == 0 || block.Code.Length > MaxCodeLength)
        {
            return false;
        }

        var end = info.IndexOfAny(' ', '\t');
        if (end >= 0)
        {
            info = info[..end];
        }

        if (CountLines(block.Code) > MaxLineCount)
        {
            return false;
        }

        language = info.ToString().ToLowerInvariant();
        return true;
    }

    private static int CountLines(string code) => code.AsSpan().Count('\n') + 1;

    private static RenderedMarkdownDocument Rewrite(
        RenderedMarkdownDocument document,
        Func<MarkdownCodeBlock, IReadOnlyList<MarkdownCodeToken>?> getTokens)
    {
        var blocks = RewriteBlocks(document.Blocks, getTokens);
        return ReferenceEquals(blocks, document.Blocks) ? document : document with { Blocks = blocks };
    }

    /// <summary>
    /// Пересобирает только ветки, где у блока кода поменялись токены; остальные
    /// списки и блоки остаются теми же экземплярами.
    /// </summary>
    private static IReadOnlyList<MarkdownBlock> RewriteBlocks(
        IReadOnlyList<MarkdownBlock> blocks,
        Func<MarkdownCodeBlock, IReadOnlyList<MarkdownCodeToken>?> getTokens)
    {
        List<MarkdownBlock>? result = null;
        for (var index = 0; index < blocks.Count; index++)
        {
            var block = blocks[index];
            var rewritten = RewriteBlock(block, getTokens);
            if (result is null && !ReferenceEquals(rewritten, block))
            {
                result = new List<MarkdownBlock>(blocks.Count);
                for (var previous = 0; previous < index; previous++)
                {
                    result.Add(blocks[previous]);
                }
            }

            result?.Add(rewritten);
        }

        return result ?? blocks;
    }

    private static MarkdownBlock RewriteBlock(
        MarkdownBlock block,
        Func<MarkdownCodeBlock, IReadOnlyList<MarkdownCodeToken>?> getTokens)
    {
        switch (block)
        {
            case MarkdownCodeBlock code when code.Tokens is null:
                var tokens = getTokens(code);
                return tokens is null ? code : code with { Tokens = tokens };
            case MarkdownQuoteBlock quote:
                var quoteBlocks = RewriteBlocks(quote.Blocks, getTokens);
                return ReferenceEquals(quoteBlocks, quote.Blocks) ? quote : quote with { Blocks = quoteBlocks };
            case MarkdownListBlock list:
                var items = RewriteItems(list.Items, item => item.Blocks, (item, value) => item with { Blocks = value }, getTokens);
                return ReferenceEquals(items, list.Items) ? list : list with { Items = items };
            case MarkdownFootnotesBlock footnotes:
                var notes = RewriteItems(footnotes.Footnotes, note => note.Blocks, (note, value) => note with { Blocks = value }, getTokens);
                return ReferenceEquals(notes, footnotes.Footnotes) ? footnotes : footnotes with { Footnotes = notes };
            case MarkdownDefinitionListBlock definitionList:
                var definitionItems = RewriteDefinitionItems(definitionList.Items, getTokens);
                return ReferenceEquals(definitionItems, definitionList.Items)
                    ? definitionList
                    : definitionList with { Items = definitionItems };
            default:
                return block;
        }
    }

    private static IReadOnlyList<T> RewriteItems<T>(
        IReadOnlyList<T> items,
        Func<T, IReadOnlyList<MarkdownBlock>> getBlocks,
        Func<T, IReadOnlyList<MarkdownBlock>, T> withBlocks,
        Func<MarkdownCodeBlock, IReadOnlyList<MarkdownCodeToken>?> getTokens)
        where T : class
    {
        List<T>? result = null;
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var blocks = getBlocks(item);
            var rewrittenBlocks = RewriteBlocks(blocks, getTokens);
            var rewritten = ReferenceEquals(rewrittenBlocks, blocks) ? item : withBlocks(item, rewrittenBlocks);
            if (result is null && !ReferenceEquals(rewritten, item))
            {
                result = new List<T>(items.Count);
                for (var previous = 0; previous < index; previous++)
                {
                    result.Add(items[previous]);
                }
            }

            result?.Add(rewritten);
        }

        return result ?? items;
    }

    private static IReadOnlyList<MarkdownDefinitionItem> RewriteDefinitionItems(
        IReadOnlyList<MarkdownDefinitionItem> items,
        Func<MarkdownCodeBlock, IReadOnlyList<MarkdownCodeToken>?> getTokens)
    {
        List<MarkdownDefinitionItem>? result = null;
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var definitions = RewriteItems(
                item.Definitions,
                definition => definition.Blocks,
                (definition, value) => definition with { Blocks = value },
                getTokens);
            var rewritten = ReferenceEquals(definitions, item.Definitions) ? item : item with { Definitions = definitions };
            if (result is null && !ReferenceEquals(rewritten, item))
            {
                result = new List<MarkdownDefinitionItem>(items.Count);
                for (var previous = 0; previous < index; previous++)
                {
                    result.Add(items[previous]);
                }
            }

            result?.Add(rewritten);
        }

        return result ?? items;
    }

    private static bool ContainsBlock(IReadOnlyList<MarkdownBlock> blocks, Func<MarkdownCodeBlock, bool> predicate)
    {
        foreach (var block in blocks)
        {
            var found = block switch
            {
                MarkdownCodeBlock code => predicate(code),
                MarkdownQuoteBlock quote => ContainsBlock(quote.Blocks, predicate),
                MarkdownListBlock list => list.Items.Any(item => ContainsBlock(item.Blocks, predicate)),
                MarkdownFootnotesBlock footnotes => footnotes.Footnotes.Any(note => ContainsBlock(note.Blocks, predicate)),
                MarkdownDefinitionListBlock definitionList => definitionList.Items.Any(item =>
                    item.Definitions.Any(definition => ContainsBlock(definition.Blocks, predicate))),
                _ => false,
            };
            if (found)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Ответы движка по паре «язык + текст», включая «не подсвечено».
    /// Потокобезопасен: пишет фоновая докраска, читает UI-поток. При
    /// переполнении по числу записей или по объёму кода вытесняется самый
    /// давний по использованию.
    /// </summary>
    private sealed class HighlightCache(int capacity, int characterBudget)
    {
        private int _characters;

        private readonly Dictionary<(string Language, string Code), LinkedListNode<Entry>> _entries = [];
        private readonly LinkedList<Entry> _order = new();
        private readonly Lock _gate = new();

        public bool Contains(string language, string code)
        {
            lock (_gate)
            {
                return _entries.ContainsKey((language, code));
            }
        }

        public bool TryGet(string language, string code, out IReadOnlyList<MarkdownCodeToken>? tokens)
        {
            lock (_gate)
            {
                if (_entries.TryGetValue((language, code), out var node))
                {
                    _order.Remove(node);
                    _order.AddFirst(node);
                    tokens = node.Value.Tokens;
                    return true;
                }
            }

            tokens = null;
            return false;
        }

        public void Add(string language, string code, IReadOnlyList<MarkdownCodeToken>? tokens)
        {
            lock (_gate)
            {
                var key = (language, code);
                if (_entries.TryGetValue(key, out var existing))
                {
                    _order.Remove(existing);
                    _characters -= code.Length;
                }

                var node = _order.AddFirst(new Entry(key, tokens));
                _entries[key] = node;
                _characters += code.Length;
                while ((_entries.Count > capacity || _characters > characterBudget) && _order.Last is { } oldest)
                {
                    _order.RemoveLast();
                    _entries.Remove(oldest.Value.Key);
                    _characters -= oldest.Value.Key.Code.Length;
                }
            }
        }

        private sealed record Entry((string Language, string Code) Key, IReadOnlyList<MarkdownCodeToken>? Tokens);
    }
}
