using MarkMello.Domain.Workspace;

namespace MarkMello.Domain.Tests;

public sealed class WorkspaceModelTests
{
    // Путь строится Path-API текущей ОС: литерал «C:\docs» на macOS и Linux —
    // одно имя файла с обратными слешами, а не папка «docs».
    private static readonly string Docs = Path.Combine(Path.GetTempPath(), "MarkMello.Tests", "docs");

    [Fact]
    public void OrderingPutsDirectoriesBeforeFiles()
    {
        var entries = new List<WorkspaceEntry>
        {
            WorkspaceEntry.ForFile(Path.Combine(Docs, "architecture.md"), "architecture.md"),
            WorkspaceEntry.ForDirectory(Path.Combine(Docs, "src"), "src"),
            WorkspaceEntry.ForFile(Path.Combine(Docs, "README.md"), "README.md"),
            WorkspaceEntry.ForDirectory(Path.Combine(Docs, "adr"), "adr")
        };

        entries.Sort(WorkspaceEntryOrdering.Instance);

        Assert.Equal(
            ["adr", "src", "architecture.md", "README.md"],
            entries.Select(entry => entry.Name));
    }

    [Fact]
    public void OrderingIgnoresCaseWithinGroup()
    {
        var entries = new List<WorkspaceEntry>
        {
            WorkspaceEntry.ForFile(Path.Combine(Docs, "b.md"), "b.md"),
            WorkspaceEntry.ForFile(Path.Combine(Docs, "A.md"), "A.md")
        };

        entries.Sort(WorkspaceEntryOrdering.Instance);

        Assert.Equal(["A.md", "b.md"], entries.Select(entry => entry.Name));
    }

    [Theory]
    [InlineData(".git", true)]
    [InlineData("node_modules", true)]
    [InlineData("BIN", true)]
    [InlineData("obj", true)]
    [InlineData("src", false)]
    [InlineData("binary-notes", false)]
    public void IgnoredDirectoryNamesAreMatchedCaseInsensitively(string name, bool expected)
        => Assert.Equal(expected, WorkspaceEntryFilter.IsIgnoredDirectoryName(name));

    [Theory]
    [InlineData(".env", true)]
    [InlineData("notes.md", false)]
    public void DotPrefixedNamesAreTreatedAsHidden(string name, bool expected)
        => Assert.Equal(expected, WorkspaceEntryFilter.IsDotPrefixedName(name));

    [Theory]
    [InlineData("notes.md", true)]
    [InlineData("notes.markdown", true)]
    [InlineData("notes.txt", true)]
    [InlineData("pack.bat", false)]
    [InlineData("LICENSE", false)]
    public void FileEntriesKnowWhetherTheyOpenInTheViewer(string name, bool expected)
    {
        var entry = WorkspaceEntry.ForFile(Path.Combine(Docs, name), name);

        Assert.Equal(expected, entry.IsSupportedDocument);
    }

    [Fact]
    public void FolderDisplayNameIsTheLastSegment()
    {
        var root = Path.Combine(Path.GetTempPath(), "MarkMello.Tests", "projects", "MarkMello", "docs");

        var folder = WorkspaceFolder.Create(root + Path.DirectorySeparatorChar);

        Assert.Equal("docs", folder.DisplayName);
        Assert.Equal(root, folder.RootPath);
    }

    [Theory]
    [InlineData(null, 260d)]
    [InlineData(120d, 220d)]
    [InlineData(999d, 340d)]
    [InlineData(280d, 280d)]
    [InlineData(double.NaN, 260d)]
    public void SidebarWidthIsClampedToTheDesignRange(double? stored, double expected)
        => Assert.Equal(expected, WorkspaceSidebarWidth.Normalize(stored));
}
