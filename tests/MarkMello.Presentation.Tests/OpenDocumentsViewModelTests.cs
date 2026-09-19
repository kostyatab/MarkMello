using System.Globalization;
using MarkMello.Presentation.ViewModels;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Логика полосы вкладок: порядок, переполнение, различение одноимённых файлов
/// и обход по `Ctrl Tab`. Всё без UI — правила должны держаться сами по себе.
/// </summary>
public sealed class OpenDocumentsViewModelTests
{
    [Fact]
    public void ClosingActiveTabActivatesTheNeighbourOnTheRight()
    {
        var documents = CreateDocuments();
        var first = Open(documents, TestPaths.At("docs", "a.md"));
        var second = Open(documents, TestPaths.At("docs", "b.md"));
        var third = Open(documents, TestPaths.At("docs", "c.md"));
        documents.Activate(second);

        documents.Remove(second);

        Assert.Equal([first, third], documents.Tabs);
        Assert.Same(third, documents.ActiveTab);
    }

    [Fact]
    public void ClosingTheLastTabLeavesNoActiveTab()
    {
        var documents = CreateDocuments();
        var only = Open(documents, TestPaths.At("docs", "a.md"));
        documents.Activate(only);

        documents.Remove(only);

        Assert.Empty(documents.Tabs);
        Assert.Null(documents.ActiveTab);
        Assert.False(documents.HasTabs);
    }

    [Fact]
    public void ActiveFlagFollowsTheActiveTab()
    {
        var documents = CreateDocuments();
        var first = Open(documents, TestPaths.At("docs", "a.md"));
        var second = Open(documents, TestPaths.At("docs", "b.md"));

        documents.Activate(first);
        Assert.True(first.IsActive);
        Assert.False(second.IsActive);

        documents.Activate(second);
        Assert.False(first.IsActive);
        Assert.True(second.IsActive);
    }

    [Fact]
    public void NeighbourWrapsAroundInBothDirections()
    {
        var documents = CreateDocuments();
        var first = Open(documents, TestPaths.At("docs", "a.md"));
        var second = Open(documents, TestPaths.At("docs", "b.md"));
        var third = Open(documents, TestPaths.At("docs", "c.md"));

        documents.Activate(third);
        Assert.Same(first, documents.GetNeighbour(1));

        documents.Activate(first);
        Assert.Same(third, documents.GetNeighbour(-1));
        Assert.Same(second, documents.GetNeighbour(1));
    }

    [Fact]
    public void SameFileNamesGetTheirParentFolderAsDisambiguator()
    {
        var documents = CreateDocuments();
        var docsReadme = Open(documents, TestPaths.At("project", "docs", "README.md"));
        var srcReadme = Open(documents, TestPaths.At("project", "src", "README.md"));
        var vision = Open(documents, TestPaths.At("project", "docs", "vision.md"));

        Assert.Equal("docs", docsReadme.Disambiguator);
        Assert.Equal("src", srcReadme.Disambiguator);
        Assert.Null(vision.Disambiguator);

        documents.Remove(srcReadme);

        Assert.Null(docsReadme.Disambiguator);
    }

    [Fact]
    public void TabsKeepTheirFullWidthWhileTheyFit()
    {
        var documents = CreateDocuments();
        documents.AvailableWidth = 1000;
        Open(documents, TestPaths.At("docs", "a.md"));
        Open(documents, TestPaths.At("docs", "b.md"));

        Assert.Equal(2, documents.VisibleTabs.Count);
        Assert.Empty(documents.OverflowTabs);
        Assert.False(documents.HasOverflow);
        Assert.Equal(OpenDocumentsViewModel.PreferredTabWidth, documents.TabWidth);
    }

    /// <summary>
    /// Ширина вкладки не зависит от имени: короткое и длинное имя получают одни 180,
    /// а длинное гаснет многоточием внутри вкладки.
    /// </summary>
    [Fact]
    public void TabWidthDoesNotDependOnTheName()
    {
        var documents = CreateDocuments();
        documents.AvailableWidth = 1000;
        Open(documents, TestPaths.At("docs", "a.md"));
        Open(documents, TestPaths.At("docs", "implementation-plan-folders-and-tabs-revision.md"));

        Assert.Equal(2, documents.VisibleTabs.Count);
        Assert.Equal(OpenDocumentsViewModel.PreferredTabWidth, documents.TabWidth);
    }

    /// <summary>
    /// Четыре вкладки по 180 с «+» не помещаются в 600, а по 120 — помещаются:
    /// все остаются видимыми и делят место поровну.
    /// </summary>
    [Fact]
    public void CrowdedTabsShrinkBeforeAnyOfThemOverflows()
    {
        var documents = CreateDocuments();
        documents.AvailableWidth = 600;
        for (var index = 0; index < 4; index++)
        {
            Open(documents, TestPaths.At("docs", $"doc-{index}.md"));
        }

        Assert.Equal(4, documents.VisibleTabs.Count);
        Assert.False(documents.HasOverflow);

        // 600 − «+» с промежутком (36) − три промежутка между вкладками (18) = 546 на четыре.
        Assert.Equal(136.5, documents.TabWidth);
    }

    /// <summary>
    /// Уже 120 вкладки не сжимаются: при 500 четыре по 120 с «+» не помещаются,
    /// и лишняя уходит в «ещё N», а видимые остаются по 120.
    /// </summary>
    [Fact]
    public void TabsOverflowOnlyOnceTheyHitTheMinimumWidth()
    {
        var documents = CreateDocuments();
        documents.AvailableWidth = 500;
        var tabs = Enumerable.Range(0, 4)
            .Select(index => Open(documents, TestPaths.At("docs", $"doc-{index}.md")))
            .ToList();
        documents.Activate(tabs[0]);

        Assert.Equal(tabs.Take(3), documents.VisibleTabs);
        Assert.Equal([tabs[3]], documents.OverflowTabs);
        Assert.Equal(OpenDocumentsViewModel.MinimumTabWidth, documents.TabWidth);
    }

    [Fact]
    public void ExtraTabsMoveIntoOverflowFromTheEnd()
    {
        var documents = CreateDocuments();
        documents.AvailableWidth = 400;
        var first = Open(documents, TestPaths.At("docs", "first-document.md"));
        var second = Open(documents, TestPaths.At("docs", "second-document.md"));
        var third = Open(documents, TestPaths.At("docs", "third-document.md"));
        documents.Activate(first);

        Assert.Equal([first, second], documents.VisibleTabs);
        Assert.Equal([third], documents.OverflowTabs);
        Assert.True(documents.HasOverflow);
        Assert.Equal(documents.OverflowTabs.Count.ToString(CultureInfo.CurrentCulture), documents.OverflowCountLabel);
    }

    [Fact]
    public void ActiveTabStaysVisibleEvenWhenItWouldOverflow()
    {
        var documents = CreateDocuments();
        documents.AvailableWidth = 400;
        var first = Open(documents, TestPaths.At("docs", "first-document.md"));
        var second = Open(documents, TestPaths.At("docs", "second-document.md"));
        var third = Open(documents, TestPaths.At("docs", "third-document.md"));

        documents.Activate(third);

        Assert.Equal([first, third], documents.VisibleTabs);
        Assert.Equal([second], documents.OverflowTabs);
    }

    [Fact]
    public void NarrowStripStillShowsOneTab()
    {
        var documents = CreateDocuments();
        documents.AvailableWidth = 80;
        var only = Open(documents, TestPaths.At("docs", "a-very-long-document-name.md"));

        Assert.Equal([only], documents.VisibleTabs);
        Assert.Equal(OpenDocumentsViewModel.MinimumTabWidth, documents.TabWidth);
    }

    [Fact]
    public void NarrowStripWithManyTabsKeepsTheActiveOneVisible()
    {
        var documents = CreateDocuments();
        documents.AvailableWidth = 80;
        Open(documents, TestPaths.At("docs", "a.md"));
        var second = Open(documents, TestPaths.At("docs", "b.md"));
        documents.Activate(second);

        Assert.Equal([second], documents.VisibleTabs);
        Assert.Single(documents.OverflowTabs);
    }

    [Fact]
    public async Task CloseOthersClosesEveryOtherTab()
    {
        var closed = new List<DocumentTabViewModel>();
        var documents = new OpenDocumentsViewModel(
            static _ => Task.CompletedTask,
            tab =>
            {
                closed.Add(tab);
                return Task.CompletedTask;
            });

        var first = Open(documents, TestPaths.At("docs", "a.md"));
        var second = Open(documents, TestPaths.At("docs", "b.md"));
        var third = Open(documents, TestPaths.At("docs", "c.md"));

        await documents.CloseOthersCommand.ExecuteAsync(second);

        Assert.Equal([first, third], closed);
    }

    private static OpenDocumentsViewModel CreateDocuments()
        => new(static _ => Task.CompletedTask, static _ => Task.CompletedTask);

    private static DocumentTabViewModel Open(OpenDocumentsViewModel documents, string path)
        => documents.Add(new DocumentTabViewModel(path, Path.GetFileName(path)));
}
