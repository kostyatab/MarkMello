using MarkMello.Application.Abstractions;
using MarkMello.Domain.Recent;

namespace MarkMello.Infrastructure.Platform;

/// <summary>
/// Проверка записей «Недавних» на диске. Идёт в пуле потоков: отключённый сетевой диск
/// может отвечать секунды, и UI-поток ждать его не должен.
/// </summary>
public sealed class FileSystemPathExistenceProbe : IPathExistenceProbe
{
    public Task<IReadOnlyList<string>> FindMissingAsync(
        IReadOnlyList<RecentEntry> entries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var snapshot = entries.ToArray();
        return Task.Run<IReadOnlyList<string>>(
            () =>
            {
                var missing = new List<string>();
                foreach (var entry in snapshot)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!Exists(entry))
                    {
                        missing.Add(entry.Path);
                    }
                }

                return missing;
            },
            cancellationToken);
    }

    private static bool Exists(RecentEntry entry)
    {
        try
        {
            return entry.Kind == RecentEntryKind.Folder
                ? Directory.Exists(entry.Path)
                : File.Exists(entry.Path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
