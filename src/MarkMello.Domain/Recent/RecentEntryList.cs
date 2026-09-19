namespace MarkMello.Domain.Recent;

/// <summary>
/// Правила списка «Недавних» (ADR-0009 Rule 8): не больше <see cref="Limit"/> записей,
/// свежие сверху, повторное открытие поднимает запись наверх. Пути сравниваются
/// нормализованными; на Windows — без учёта регистра, как и везде в оболочке.
/// </summary>
public static class RecentEntryList
{
    public const int Limit = 5;

    /// <summary>Ставит запись первой; прежняя запись того же пути уходит, хвост сверх лимита — тоже.</summary>
    public static IReadOnlyList<RecentEntry> Add(
        IReadOnlyList<RecentEntry> entries,
        RecentEntry entry,
        bool ignoreCase)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(entry);

        var result = new List<RecentEntry>(Limit) { entry };
        foreach (var existing in entries)
        {
            if (result.Count == Limit)
            {
                break;
            }

            if (!PathsEqual(existing.Path, entry.Path, ignoreCase))
            {
                result.Add(existing);
            }
        }

        return result;
    }

    public static IReadOnlyList<RecentEntry> Remove(
        IReadOnlyList<RecentEntry> entries,
        string path,
        bool ignoreCase)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(path);

        return entries.Where(entry => !PathsEqual(entry.Path, path, ignoreCase)).ToList();
    }

    /// <summary>
    /// Приводит прочитанный из настроек список к правилам: без пустых и относительных путей,
    /// без неизвестных типов и повторов, свежие сверху, не длиннее лимита.
    /// </summary>
    public static IReadOnlyList<RecentEntry> Normalize(IEnumerable<RecentEntry?>? entries, bool ignoreCase)
    {
        if (entries is null)
        {
            return [];
        }

        var result = new List<RecentEntry>(Limit);
        foreach (var entry in entries
                     .OfType<RecentEntry>()
                     .Where(static entry => IsValid(entry))
                     .OrderByDescending(static entry => entry.OpenedAt))
        {
            if (result.Count == Limit)
            {
                break;
            }

            if (!result.Any(existing => PathsEqual(existing.Path, entry.Path, ignoreCase)))
            {
                result.Add(entry);
            }
        }

        return result;
    }

    public static bool PathsEqual(string left, string right, bool ignoreCase)
        => string.Equals(
            NormalizePath(left),
            NormalizePath(right),
            ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool IsValid(RecentEntry entry)
        => !string.IsNullOrWhiteSpace(entry.Path)
            && System.IO.Path.IsPathFullyQualified(entry.Path)
            && Enum.IsDefined(entry.Kind);

    /// <summary>«~/docs/../notes/» и «~/notes» — одна запись: точки раскрываются, хвостовой слеш снимается.</summary>
    private static string NormalizePath(string path)
    {
        try
        {
            return System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }
}
