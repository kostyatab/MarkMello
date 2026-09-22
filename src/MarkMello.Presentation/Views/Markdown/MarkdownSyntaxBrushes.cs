using Avalonia.Media;
using MarkMello.Domain;

namespace MarkMello.Presentation.Views.Markdown;

/// <summary>
/// Кисти подсветки синтаксиса из темы (<c>MmSyntax*Brush</c> в
/// <c>Themes/Colors.axaml</c>, ADR-0010 §3). Берутся заново при каждой
/// пересборке раскладки фрагмента, поэтому смена темы перекрашивает код без
/// переоткрытия документа. Вида без кисти в теме нет — он рисуется цветом текста.
/// </summary>
internal sealed class MarkdownSyntaxBrushes
{
    private readonly IBrush?[] _brushes;

    private MarkdownSyntaxBrushes(IBrush?[] brushes)
    {
        _brushes = brushes;
    }

    public static string GetResourceKey(MarkdownCodeTokenKind kind) => kind switch
    {
        MarkdownCodeTokenKind.Comment => "MmSyntaxCommentBrush",
        MarkdownCodeTokenKind.Keyword => "MmSyntaxKeywordBrush",
        MarkdownCodeTokenKind.StringLiteral => "MmSyntaxStringBrush",
        MarkdownCodeTokenKind.Constant => "MmSyntaxConstantBrush",
        MarkdownCodeTokenKind.Type => "MmSyntaxTypeBrush",
        MarkdownCodeTokenKind.Function => "MmSyntaxFunctionBrush",
        MarkdownCodeTokenKind.Member => "MmSyntaxMemberBrush",
        MarkdownCodeTokenKind.Inserted => "MmSyntaxInsertedBrush",
        MarkdownCodeTokenKind.Deleted => "MmSyntaxDeletedBrush",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static MarkdownSyntaxBrushes Resolve(Func<string, IBrush?> findBrush)
    {
        ArgumentNullException.ThrowIfNull(findBrush);

        var kinds = Enum.GetValues<MarkdownCodeTokenKind>();
        var brushes = new IBrush?[kinds.Length];
        foreach (var kind in kinds)
        {
            brushes[(int)kind] = findBrush(GetResourceKey(kind));
        }

        return new MarkdownSyntaxBrushes(brushes);
    }

    public IBrush? Get(MarkdownCodeTokenKind kind)
        => (uint)kind < (uint)_brushes.Length ? _brushes[(int)kind] : null;
}
