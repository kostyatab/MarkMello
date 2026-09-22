using MarkMello.Domain;

namespace MarkMello.Application.Abstractions;

/// <summary>
/// Движок подсветки синтаксиса (ADR-0010). Реализация — в Infrastructure;
/// вызывается только вне UI-потока, из <c>HighlightCodeBlocksUseCase</c>.
/// </summary>
public interface ICodeHighlighter
{
    /// <summary>
    /// Разбирает код блока.
    /// </summary>
    /// <param name="language">Первое слово метки блока в нижнем регистре.</param>
    /// <param name="code">Текст блока, строки разделены <c>\n</c>.</param>
    /// <param name="timeout">Бюджет на разбор блока (без загрузки грамматики).</param>
    /// <returns>
    /// Токены по возрастанию смещения, без пересечений, или <c>null</c> —
    /// «не подсвечено»: язык неизвестен или движок не справился. Такой ответ
    /// окончательный, его можно запомнить.
    /// </returns>
    /// <exception cref="TimeoutException">
    /// Разбор упёрся в <paramref name="timeout"/>: ответ зависит от нагрузки
    /// машины, запоминать его нельзя.
    /// </exception>
    /// <exception cref="OperationCanceledException">Разбор отменён.</exception>
    IReadOnlyList<MarkdownCodeToken>? Highlight(
        string language,
        string code,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}
