using MarkMello.Domain;

namespace MarkMello.Presentation.Views.Markdown;

/// <summary>
/// Front matter для копирования: пары ключ/значение копируются как таблица без
/// строки заголовка — ключ жирным, значение дословно.
///
/// Ячейки строятся в одном месте, чтобы оба формата копирования в Telegram не
/// разошлись. В окне front matter рисуется своим видом — свойствами, а не
/// таблицей (<c>MarkdownDocumentView.BuildFrontMatter</c>).
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
