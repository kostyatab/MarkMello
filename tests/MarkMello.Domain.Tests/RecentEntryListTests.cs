using MarkMello.Domain.Recent;

namespace MarkMello.Domain.Tests;

public sealed class RecentEntryListTests
{
    // Путь строится Path-API текущей ОС: литерал «C:\docs» на macOS и Linux — не абсолютный путь.
    private static readonly string Docs = Path.Combine(Path.GetTempPath(), "MarkMello.Tests", "recent");
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AddPutsNewEntryFirst()
    {
        var list = RecentEntryList.Add([File("a.md", 1)], File("b.md", 2), ignoreCase: false);

        Assert.Equal(["b.md", "a.md"], Names(list));
    }

    [Fact]
    public void AddOfKnownPathMovesItToTopWithoutDuplicate()
    {
        IReadOnlyList<RecentEntry> list = [File("c.md", 3), File("b.md", 2), File("a.md", 1)];

        list = RecentEntryList.Add(list, File("a.md", 4), ignoreCase: false);

        Assert.Equal(["a.md", "c.md", "b.md"], Names(list));
        Assert.Equal(Now.AddMinutes(4), list[0].OpenedAt);
    }

    [Fact]
    public void AddKeepsAtMostFiveEntries()
    {
        IReadOnlyList<RecentEntry> list = [];
        for (var index = 0; index < 10; index++)
        {
            list = RecentEntryList.Add(list, File($"{index}.md", index), ignoreCase: false);
        }

        Assert.Equal(RecentEntryList.Limit, list.Count);
        Assert.Equal("9.md", Path.GetFileName(list[0].Path));
        Assert.Equal("5.md", Path.GetFileName(list[^1].Path));
    }

    [Fact]
    public void AddComparesNormalizedPaths()
    {
        var folder = new RecentEntry(Path.Combine(Docs, "notes"), RecentEntryKind.Folder, Now);
        var sameWithSlash = new RecentEntry(
            Path.Combine(Docs, "other", "..", "notes") + Path.DirectorySeparatorChar,
            RecentEntryKind.Folder,
            Now.AddMinutes(1));

        var list = RecentEntryList.Add([folder], sameWithSlash, ignoreCase: false);

        Assert.Single(list);
    }

    [Fact]
    public void AddIgnoresCaseOnlyWhenAsked()
    {
        IReadOnlyList<RecentEntry> list = [File("Readme.md", 1)];

        Assert.Equal(2, RecentEntryList.Add(list, File("README.md", 2), ignoreCase: false).Count);
        Assert.Single(RecentEntryList.Add(list, File("README.md", 2), ignoreCase: true));
    }

    [Fact]
    public void RemoveDropsOnlyThatPath()
    {
        var list = RecentEntryList.Remove([File("b.md", 2), File("a.md", 1)], Path.Combine(Docs, "b.md"), ignoreCase: false);

        Assert.Equal(["a.md"], Names(list));
    }

    [Fact]
    public void NormalizeDropsInvalidEntriesAndDuplicatesAndSortsFreshFirst()
    {
        var list = RecentEntryList.Normalize(
            [
                File("old.md", 1),
                null,
                new RecentEntry("relative.md", RecentEntryKind.File, Now),
                new RecentEntry(string.Empty, RecentEntryKind.File, Now),
                new RecentEntry(Path.Combine(Docs, "odd.md"), (RecentEntryKind)42, Now),
                File("new.md", 5),
                File("old.md", 0)
            ],
            ignoreCase: false);

        Assert.Equal(["new.md", "old.md"], Names(list));
        Assert.Equal(Now.AddMinutes(1), list[1].OpenedAt);
    }

    [Fact]
    public void NormalizeOfMissingListIsEmpty()
    {
        Assert.Empty(RecentEntryList.Normalize(null, ignoreCase: false));
    }

    [Fact]
    public void NormalizeTrimsToLimit()
    {
        var entries = Enumerable.Range(0, 12).Select(index => File($"{index}.md", index)).ToList();

        Assert.Equal(RecentEntryList.Limit, RecentEntryList.Normalize(entries, ignoreCase: false).Count);
    }

    private static RecentEntry File(string name, int minutes)
        => new(Path.Combine(Docs, name), RecentEntryKind.File, Now.AddMinutes(minutes));

    private static IEnumerable<string> Names(IEnumerable<RecentEntry> entries)
        => entries.Select(static entry => Path.GetFileName(entry.Path));
}
