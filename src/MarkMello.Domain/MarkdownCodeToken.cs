namespace MarkMello.Domain;

/// <summary>
/// Вид токена подсветки синтаксиса (ADR-0010 §3). Всё, что не попало ни в один
/// вид, — пунктуация, операторы, имена переменных — остаётся цветом текста.
/// </summary>
public enum MarkdownCodeTokenKind
{
    Comment,
    Keyword,
    StringLiteral,

    /// <summary>Числа и именованные константы.</summary>
    Constant,

    /// <summary>Типы и классы, а также ключи JSON и YAML и теги XML и HTML.</summary>
    Type,
    Function,

    /// <summary>Свойства и поля — имя после точки и в объявлении.</summary>
    Member,

    /// <summary>Строка вставки в <c>diff</c>.</summary>
    Inserted,

    /// <summary>Строка удаления в <c>diff</c>.</summary>
    Deleted,
}

/// <summary>Отрезок текста блока кода с видом подсветки.</summary>
/// <param name="Start">Смещение в <see cref="MarkdownCodeBlock.Code"/>.</param>
/// <param name="Length">Длина отрезка, больше нуля.</param>
public readonly record struct MarkdownCodeToken(int Start, int Length, MarkdownCodeTokenKind Kind)
{
    public int End => Start + Length;
}
