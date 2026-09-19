namespace MarkMello.Domain.Recent;

/// <summary>Что открывала запись «Недавних»: документ или папку.</summary>
public enum RecentEntryKind
{
    File,
    Folder
}

/// <summary>
/// Запись «Недавних» на стартовом экране (ADR-0009 Rule 8): полный путь, тип и время
/// последнего явного открытия.
/// </summary>
public sealed record RecentEntry(string Path, RecentEntryKind Kind, DateTimeOffset OpenedAt);
