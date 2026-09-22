using MarkMello.Domain;

namespace MarkMello.Infrastructure.Highlighting;

/// <summary>
/// Скоупы TextMate → вид токена (ADR-0010 §3). Скоупы токена идут от внешнего
/// к внутреннему; цвет даёт самый внутренний скоуп, у которого есть вид.
/// </summary>
internal static class TextMateScopeClassifier
{
    public static MarkdownCodeTokenKind? Classify(IReadOnlyList<string> scopes, ReadOnlySpan<char> text)
    {
        for (var index = scopes.Count - 1; index >= 0; index--)
        {
            switch (Match(scopes[index], text))
            {
                case { IsPlain: true }:
                    return null;
                case { Kind: { } kind }:
                    return kind;
            }
        }

        return null;
    }

    private static ScopeMatch Match(string scope, ReadOnlySpan<char> text)
    {
        // Подстановка внутри строки или шаблона — это код, а не строка: поиск
        // останавливается, чтобы токен не взял цвет строки-родителя.
        if (Is(scope, "meta.embedded") || Is(scope, "meta.interpolation") || Is(scope, "meta.template.expression"))
        {
            return ScopeMatch.Plain;
        }

        // Подстановка команды в shell — $(git describe), `whoami` — грамматика
        // размечает строкой, но это код: имя команды внутри красит ShellCommandNames.
        if ((Is(scope, "string.interpolated.dollar") || Is(scope, "string.interpolated.backtick"))
            && scope.EndsWith(".shell", StringComparison.Ordinal))
        {
            return ScopeMatch.Plain;
        }

        // Свойства и поля: имя после точки (x.Products, this.items, obj.attr) и в
        // объявлении. Объект перед точкой (dbContext, Gender) — поле, локальная
        // переменная или тип — по тексту не различить, он остаётся цветом текста.
        if (Is(scope, "variable.other.object.property") || Is(scope, "variable.other.property")
            || Is(scope, "support.variable.property") || Is(scope, "entity.name.variable.field")
            || Is(scope, "entity.name.variable.property") || Is(scope, "meta.attribute"))
        {
            return MarkdownCodeTokenKind.Member;
        }

        if (Is(scope, "variable"))
        {
            return Is(scope, "variable.language") ? MarkdownCodeTokenKind.Keyword : ScopeMatch.Plain;
        }

        // Знак комментария — и отдельно: грамматика XML ставит его вне comment.*.
        if (Is(scope, "comment") || Is(scope, "punctuation.definition.comment"))
        {
            return MarkdownCodeTokenKind.Comment;
        }

        // storage.type.string — префикс строки Python (f"…", r"…"): часть строки.
        if (Is(scope, "constant.character.escape") || Is(scope, "string") || Is(scope, "storage.type.string"))
        {
            return MarkdownCodeTokenKind.StringLiteral;
        }

        // null, true, false, None — ключевые слова, как в Rider.
        if (Is(scope, "constant.language"))
        {
            return MarkdownCodeTokenKind.Keyword;
        }

        if (Is(scope, "constant.numeric") || Is(scope, "support.constant") || Is(scope, "entity.other.attribute-name"))
        {
            return MarkdownCodeTokenKind.Constant;
        }

        // Операторы словами (new, typeof, as, and) — ключевые слова; значки
        // (=, =>, &&) — цветом текста.
        if (Is(scope, "keyword.operator") || Is(scope, "storage.type") || Is(scope, "storage.modifier"))
        {
            return HasLetter(text) ? MarkdownCodeTokenKind.Keyword : ScopeMatch.Plain;
        }

        if (Is(scope, "keyword") || Is(scope, "markup.heading") || Is(scope, "entity.name.section"))
        {
            return MarkdownCodeTokenKind.Keyword;
        }

        if (Is(scope, "entity.name.type") || Is(scope, "entity.name.class") || Is(scope, "entity.other.inherited-class")
            || Is(scope, "support.type") || Is(scope, "support.class") || Is(scope, "entity.name.tag"))
        {
            return MarkdownCodeTokenKind.Type;
        }

        if (Is(scope, "entity.name.function") || Is(scope, "support.function"))
        {
            return MarkdownCodeTokenKind.Function;
        }

        if (Is(scope, "markup.inserted"))
        {
            return MarkdownCodeTokenKind.Inserted;
        }

        if (Is(scope, "markup.deleted"))
        {
            return MarkdownCodeTokenKind.Deleted;
        }

        if (Is(scope, "meta.diff.header") || Is(scope, "meta.diff.range"))
        {
            return MarkdownCodeTokenKind.Comment;
        }

        return Is(scope, "invalid") ? ScopeMatch.Plain : ScopeMatch.None;
    }

    /// <summary><paramref name="scope"/> равен <paramref name="prefix"/> или начинается с него и точки.</summary>
    private static bool Is(string scope, string prefix)
        => scope.StartsWith(prefix, StringComparison.Ordinal)
            && (scope.Length == prefix.Length || scope[prefix.Length] == '.');

    private static bool HasLetter(ReadOnlySpan<char> text)
    {
        foreach (var character in text)
        {
            if (char.IsLetter(character))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Ответ по одному скоупу: вид, «цветом текста» (поиск останавливается) или
    /// «не знаю» (идём к внешнему скоупу).
    /// </summary>
    private readonly record struct ScopeMatch(MarkdownCodeTokenKind? Kind, bool IsPlain)
    {
        public static ScopeMatch None => default;

        public static ScopeMatch Plain => new(null, IsPlain: true);

        public static implicit operator ScopeMatch(MarkdownCodeTokenKind kind) => new(kind, IsPlain: false);
    }
}
