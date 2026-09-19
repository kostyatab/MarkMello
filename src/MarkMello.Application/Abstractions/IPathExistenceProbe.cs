using MarkMello.Domain.Recent;

namespace MarkMello.Application.Abstractions;

/// <summary>
/// Проверка, на месте ли записи «Недавних». Диск может отвечать медленно (сетевая папка,
/// спящий внешний диск), поэтому проверка асинхронная и уходит с UI-потока; вызывается
/// только пока показан стартовый экран (ADR-0009 Rule 8).
/// </summary>
public interface IPathExistenceProbe
{
    /// <summary>Пути записей, которых больше нет: файла — как файла, папки — как папки.</summary>
    Task<IReadOnlyList<string>> FindMissingAsync(
        IReadOnlyList<RecentEntry> entries,
        CancellationToken cancellationToken = default);
}
