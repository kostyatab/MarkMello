using MarkMello.Domain;

namespace MarkMello.Infrastructure.Highlighting;

/// <summary>
/// Имена команд в bash (ADR-0010 §3). Грамматика shellscript красит только
/// встроенные команды (<c>echo</c>, <c>cd</c>), а внешние — <c>dotnet</c>,
/// <c>git</c>, <c>npm</c> — оставляет простым текстом, и блок из одних команд
/// выглядит неподсвеченным. Поэтому первое слово в позиции команды получает
/// цвет функции, как на GitHub. Аргументы остаются цветом текста.
///
/// Позиция команды — начало строки и место после <c>|</c>, <c>&amp;&amp;</c>,
/// <c>;</c>, <c>(</c>, <c>$(</c>, обратной кавычки и ключевых слов
/// <c>then</c>, <c>do</c>, <c>else</c>… Уже размеченный грамматикой текст —
/// строки, комментарии, heredoc, ключевые слова — не трогается.
/// </summary>
internal static class ShellCommandNames
{
    /// <summary>После этих ключевых слов снова идёт команда.</summary>
    private static readonly HashSet<string> CommandKeywords = new(StringComparer.Ordinal)
    {
        "then", "do", "else", "elif", "if", "while", "until", "time", "!",
    };

    /// <summary>Команды, которые запускают следующую: красятся обе.</summary>
    private static readonly HashSet<string> PrefixCommands = new(StringComparer.Ordinal)
    {
        "sudo", "env", "nohup", "exec", "command", "builtin", "xargs",
    };

    public static List<MarkdownCodeToken> Mark(string code, List<MarkdownCodeToken> tokens)
    {
        var tokenAt = new int[code.Length];
        Array.Fill(tokenAt, -1);
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            for (var offset = token.Start; offset < token.End && offset < code.Length; offset++)
            {
                tokenAt[offset] = index;
            }
        }

        var commands = new List<MarkdownCodeToken>();
        var expectsCommand = true;
        var atLineStart = true;
        var position = 0;
        while (position < code.Length)
        {
            var character = code[position];
            if (character == '\n')
            {
                // Строка, продолженная «\», — аргументы той же команды.
                expectsCommand = expectsCommand || !EndsWithContinuation(code, position);
                atLineStart = true;
                position++;
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                position++;
                continue;
            }

            if (tokenAt[position] >= 0)
            {
                var token = tokens[tokenAt[position]];
                expectsCommand = token.Kind == MarkdownCodeTokenKind.Keyword
                    && CommandKeywords.Contains(LastWord(code.AsSpan(token.Start, token.Length)));
                atLineStart = false;
                position = token.End;
                continue;
            }

            if (character == '$' && position + 1 < code.Length && code[position + 1] == '(')
            {
                expectsCommand = true;
                atLineStart = false;
                position += 2;
                continue;
            }

            if (character is ';' or '|' or '&' or '(' or '`' or '{')
            {
                expectsCommand = true;
                atLineStart = false;
                position++;
                continue;
            }

            if (character is ')' or '}' or '<' or '>')
            {
                expectsCommand = false;
                atLineStart = false;
                position++;
                continue;
            }

            var end = position;
            while (end < code.Length && tokenAt[end] < 0 && !IsWordBreak(code[end]))
            {
                end++;
            }

            var word = code.AsSpan(position, end - position);
            if (expectsCommand)
            {
                if (atLineStart && word is "$" or "%")
                {
                    // Промпт в примере вывода терминала: «$ npm install».
                }
                else if (IsAssignment(word))
                {
                    // FOO=1 make build — команда идёт после присваиваний.
                }
                else if (IsCommandStart(word[0]))
                {
                    commands.Add(new MarkdownCodeToken(position, word.Length, MarkdownCodeTokenKind.Function));
                    expectsCommand = PrefixCommands.Contains(word.ToString());
                }
                else
                {
                    expectsCommand = false;
                }
            }

            atLineStart = false;
            position = end;
        }

        if (commands.Count == 0)
        {
            return tokens;
        }

        var merged = new List<MarkdownCodeToken>(tokens.Count + commands.Count);
        merged.AddRange(tokens);
        merged.AddRange(commands);
        merged.Sort(static (left, right) => left.Start.CompareTo(right.Start));
        return merged;
    }

    private static bool IsWordBreak(char character)
        => char.IsWhiteSpace(character) || character is ';' or '|' or '&' or '(' or ')' or '<' or '>' or '`';

    private static bool IsCommandStart(char character)
        => char.IsLetterOrDigit(character) || character is '.' or '/' or '~' or '_';

    private static bool IsAssignment(ReadOnlySpan<char> word)
    {
        var equals = word.IndexOf('=');
        if (equals <= 0 || !(char.IsLetter(word[0]) || word[0] == '_'))
        {
            return false;
        }

        foreach (var character in word[..equals])
        {
            if (!char.IsLetterOrDigit(character) && character != '_')
            {
                return false;
            }
        }

        return true;
    }

    private static bool EndsWithContinuation(string code, int newline)
    {
        var index = newline - 1;
        while (index >= 0 && code[index] is ' ' or '\t' or '\r')
        {
            index--;
        }

        return index >= 0 && code[index] == '\\';
    }

    private static string LastWord(ReadOnlySpan<char> text)
    {
        text = text.TrimEnd();
        var start = text.LastIndexOfAny(' ', '\t', '\n');
        return text[(start + 1)..].ToString();
    }
}
