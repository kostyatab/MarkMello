using System.Diagnostics;
using MarkMello.Application.Abstractions;
using MarkMello.Domain;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;

namespace MarkMello.Infrastructure.Highlighting;

/// <summary>
/// Подсветка на TextMateSharp (ADR-0010). Всё ленивое: каталог грамматик
/// читается при первом блоке с меткой, реестр TextMateSharp создаётся при
/// первом блоке знакомого языка, грамматика — при первом блоке этого языка.
///
/// Грамматики и разбор TextMateSharp не потокобезопасны, поэтому разбор идёт
/// под одной блокировкой: докраска документа и превью режима правки
/// выстраиваются в очередь.
/// </summary>
public sealed class TextMateCodeHighlighter : ICodeHighlighter, IDisposable
{
    private readonly Lazy<TextMateGrammarCatalog?> _catalog = new(LoadCatalog, LazyThreadSafetyMode.ExecutionAndPublication);
    private readonly Lock _gate = new();
    private readonly Dictionary<string, IGrammar?> _grammars = new(StringComparer.Ordinal);
    private Registry? _registry;
    private TextMateRegistryOptions? _registryOptions;

    /// <summary>Создан ли реестр TextMateSharp — для тестов быстрого пути.</summary>
    internal bool IsEngineCreated
    {
        get
        {
            lock (_gate)
            {
                return _registry is not null;
            }
        }
    }

    public IReadOnlyList<MarkdownCodeToken>? Highlight(
        string language,
        string code,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(language);
        ArgumentNullException.ThrowIfNull(code);

        var scope = _catalog.Value?.FindScope(language);
        if (scope is null)
        {
            return null;
        }

        lock (_gate)
        {
            try
            {
                // Бюджет — на разбор, а не на загрузку грамматики: первый блок
                // тяжёлого языка (C++) не должен упираться в таймаут из-за неё.
                var grammar = GetGrammar(scope);
                if (grammar is null)
                {
                    return null;
                }

                var tokens = Tokenize(grammar, code, Stopwatch.StartNew(), timeout, cancellationToken);
                return IsShell(scope) ? ShellCommandNames.Mark(code, tokens) : tokens;
            }
            catch (Exception exception) when (exception is OperationCanceledException or TimeoutException)
            {
                throw;
            }
            catch (Exception)
            {
                // Грамматика или регулярное выражение не справились — блок без
                // цветов (ADR-0010 §5). Лог на каждый блок не пишем.
                return null;
            }
        }
    }

    public void Dispose()
    {
        if (_catalog.IsValueCreated)
        {
            _catalog.Value?.Dispose();
        }
    }

    private static TextMateGrammarCatalog? LoadCatalog()
    {
        try
        {
            return TextMateGrammarCatalog.LoadEmbedded();
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or System.Text.Json.JsonException)
        {
            Trace.TraceWarning($"Code highlighting is unavailable: {exception.Message}");
            return null;
        }
    }

    private bool IsShell(string scope)
        => string.Equals(_catalog.Value?.FindPackage(scope), "shellscript", StringComparison.Ordinal);

    private IGrammar? GetGrammar(string scope)
    {
        if (_grammars.TryGetValue(scope, out var cached))
        {
            return cached;
        }

        var catalog = _catalog.Value!;
        _registryOptions ??= new TextMateRegistryOptions(catalog);
        _registry ??= new Registry(_registryOptions);
        IGrammar? grammar;
        try
        {
            _registryOptions.Package = catalog.FindPackage(scope);
            grammar = _registry.LoadGrammar(scope);

            // Правила грамматики компилируются на первой строке разбора, а не при
            // загрузке: прогрев здесь выводит эту цену из бюджета блока.
            grammar?.TokenizeLine(new LineText(string.Empty));
        }
        catch (Exception)
        {
            grammar = null;
        }
        finally
        {
            _registryOptions.Package = null;
        }

        _grammars[scope] = grammar;
        return grammar;
    }

    /// <exception cref="TimeoutException">Разбор не уложился в бюджет.</exception>
    private static List<MarkdownCodeToken> Tokenize(
        IGrammar grammar,
        string code,
        Stopwatch stopwatch,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var tokens = new List<MarkdownCodeToken>();
        IStateStack? state = null;
        var lineStart = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = timeout - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                throw new TimeoutException();
            }

            var newline = code.IndexOf('\n', lineStart);
            var lineEnd = newline < 0 ? code.Length : newline;

            // \r из CRLF грамматики за конец строки не считают: «# comment\r» в YAML
            // уже не комментарий. Такую строку отдаём без перевода.
            var crlf = lineEnd > lineStart && code[lineEnd - 1] == '\r';
            if (crlf)
            {
                lineEnd--;
            }

            // С переводом строки, если он есть: TextMateSharp сам дописывает его
            // к строке без него, копируя её.
            var line = code.AsMemory(lineStart, (newline < 0 || crlf ? lineEnd : lineEnd + 1) - lineStart);
            var result = grammar.TokenizeLine(new LineText(line), state, remaining);

            // Разбор строки прерывается по лимиту молча — узнаём по времени.
            if (stopwatch.Elapsed >= timeout)
            {
                throw new TimeoutException();
            }

            state = result.RuleStack;
            foreach (var token in result.Tokens)
            {
                var start = lineStart + Math.Min(token.StartIndex, lineEnd - lineStart);
                var end = lineStart + Math.Min(token.EndIndex, lineEnd - lineStart);
                if (end <= start)
                {
                    continue;
                }

                var text = code.AsSpan(start, end - start);
                if (text.IsWhiteSpace()
                    || TextMateScopeClassifier.Classify(token.Scopes, text) is not { } kind)
                {
                    continue;
                }

                Append(tokens, code, new MarkdownCodeToken(start, end - start, kind));
            }

            if (newline < 0)
            {
                return tokens;
            }

            lineStart = newline + 1;
        }
    }

    /// <summary>
    /// Склеивает соседние токены одного вида, если между ними только пробелы и
    /// переводы строк: многострочный комментарий — один отрезок, а не десяток.
    /// </summary>
    private static void Append(List<MarkdownCodeToken> tokens, string code, MarkdownCodeToken token)
    {
        if (tokens.Count > 0)
        {
            var previous = tokens[^1];
            if (previous.Kind == token.Kind && code.AsSpan(previous.End, token.Start - previous.End).IsWhiteSpace())
            {
                tokens[^1] = previous with { Length = token.End - previous.Start };
                return;
            }
        }

        tokens.Add(token);
    }
}
