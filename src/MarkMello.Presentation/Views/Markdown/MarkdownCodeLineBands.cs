using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using MarkMello.Domain;

namespace MarkMello.Presentation.Views.Markdown;

/// <summary>
/// Фон строк вставки и удаления в блоке diff (ADR-0010 §3) — во всю ширину
/// листа кода, от рамки до рамки, как на GitHub. Лежит слоем под прокруткой
/// кода: полоса не уезжает при горизонтальной прокрутке, а выделение и
/// подсветка поиска, которые рисует сам фрагмент, остаются поверх неё.
/// Высоту и положение строк берёт из раскладки фрагмента.
/// </summary>
internal sealed class MarkdownCodeLineBands : Control
{
    private readonly MarkdownSelectionTextFragment _fragment;
    private readonly IReadOnlyList<Band> _bands;

    public MarkdownCodeLineBands(MarkdownSelectionTextFragment fragment, IReadOnlyList<Band> bands)
    {
        _fragment = fragment;
        _bands = bands;
        IsHitTestVisible = false;

        // Строки сдвигаются вместе с раскладкой фрагмента: другой кегль,
        // межстрочный, пересборка.
        _fragment.PropertyChanged += (_, args) =>
        {
            if (args.Property == BoundsProperty)
            {
                InvalidateVisual();
            }
        };
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    internal IReadOnlyList<Band> Bands => _bands;

    /// <summary>Строки токенов вставки и удаления — по полосе на строку.</summary>
    public static List<Band> FromTokens(string code, IReadOnlyList<MarkdownCodeToken>? tokens)
    {
        var bands = new List<Band>();
        if (tokens is null)
        {
            return bands;
        }

        foreach (var token in tokens)
        {
            if (token.Kind is not (MarkdownCodeTokenKind.Inserted or MarkdownCodeTokenKind.Deleted))
            {
                continue;
            }

            foreach (var line in SplitLines(code, token.Start, Math.Min(token.End, code.Length)))
            {
                bands.Add(new Band(line, token.Kind));
            }
        }

        return bands;
    }

    /// <summary>Строки отрезка <c>[start, end)</c> без переводов строк.</summary>
    public static IEnumerable<DocumentTextRange> SplitLines(string code, int start, int end)
    {
        var lineStart = start;
        while (lineStart < end)
        {
            var newline = code.IndexOf('\n', lineStart, end - lineStart);
            var lineEnd = newline < 0 ? end : newline;
            if (lineEnd > lineStart)
            {
                yield return new DocumentTextRange(lineStart, lineEnd);
            }

            lineStart = lineEnd + 1;
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var inserted = FindBrush("MmSyntaxInsertedBackgroundBrush");
        var deleted = FindBrush("MmSyntaxDeletedBackgroundBrush");
        foreach (var band in _bands)
        {
            var brush = band.Kind == MarkdownCodeTokenKind.Inserted ? inserted : deleted;
            if (brush is null)
            {
                continue;
            }

            foreach (var rect in _fragment.GetLineRects(band.Range))
            {
                if (_fragment.TranslatePoint(new Point(0, rect.Top), this) is { } top)
                {
                    context.FillRectangle(brush, new Rect(0, top.Y, Bounds.Width, rect.Height));
                }
            }
        }
    }

    private IBrush? FindBrush(string key)
        => this.TryFindResource(key, ActualThemeVariant, out var value) ? value as IBrush : null;

    internal readonly record struct Band(DocumentTextRange Range, MarkdownCodeTokenKind Kind);
}
