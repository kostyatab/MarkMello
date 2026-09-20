using MarkMello.Domain;

namespace MarkMello.Presentation.Views.Markdown;

/// <summary>
/// Раскладка front matter: пары ключ/значение показываются и копируются как
/// таблица без строки заголовка — ключ жирным, значение дословно.
///
/// Ячейки строятся в одном месте, чтобы вид в окне и оба формата копирования в
/// Telegram не разошлись: помощник общий для всех троих, и правка здесь меняет
/// сразу и вид, и копирование.
/// </summary>
internal static class MarkdownFrontMatterRows
{
    public static IReadOnlyList<IReadOnlyList<MarkdownTableCell>> Create(MarkdownFrontMatterBlock frontMatter)
    {
        var rows = new List<IReadOnlyList<MarkdownTableCell>>(frontMatter.Entries.Count);

        foreach (var entry in frontMatter.Entries)
        {
            rows.Add([
                new MarkdownTableCell([
                    new MarkdownStrongInline([new MarkdownTextInline(entry.Key)])
                ]),
                new MarkdownTableCell(entry.Value.Length == 0 ? [] : [new MarkdownTextInline(entry.Value)])
            ]);
        }

        return rows;
    }
}
