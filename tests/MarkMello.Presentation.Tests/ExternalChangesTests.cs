using MarkMello.Application.Abstractions;
using MarkMello.Application.UseCases;
using MarkMello.Domain;
using MarkMello.Domain.Workspace;
using MarkMello.Presentation.Localization;
using MarkMello.Presentation.ViewModels;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Реакция открытых вкладок на изменения, сделанные мимо приложения.
/// Таблица поведения — из раздела «Внешние изменения файла» дизайн-бандла.
/// </summary>
public sealed class ExternalChangesTests
{
    private static readonly string Root = TestPaths.At("docs");
    private static readonly string FirstPath = TestPaths.At("docs", "first.md");
    private static readonly string SecondPath = TestPaths.At("docs", "second.md");

    [Fact]
    public async Task CleanActiveTabIsReloadedSilently()
    {
        var harness = await CreateAsync();
        await harness.ViewModel.OpenPathAsync(FirstPath);
        harness.ViewModel.ReportScrollOffset(300);

        harness.Loader.Sources[FirstPath] = new MarkdownSource(FirstPath, "first.md", "# first changed");

        await harness.ViewModel.ApplyWorkspaceChangesAsync(
            [new WorkspaceChange(WorkspaceChangeKind.Changed, FirstPath)]);

        Assert.Equal("# first changed", harness.ViewModel.Document!.Content);
        Assert.False(harness.ViewModel.ShowsExternalChangeBanner);

        // Позиция чтения не должна прыгать в начало из-за чужого сохранения.
        Assert.Equal(300, harness.ViewModel.TakePendingScrollOffset());
    }

    [Fact]
    public async Task DirtyTabAsksInsteadOfReloading()
    {
        var harness = await CreateAsync();
        await harness.ViewModel.OpenPathAsync(FirstPath);
        harness.ViewModel.ToggleEditModeCommand.Execute(null);
        harness.ViewModel.EditorSession!.SourceText = "# my edits";

        harness.Loader.Sources[FirstPath] = new MarkdownSource(FirstPath, "first.md", "# changed outside");

        await harness.ViewModel.ApplyWorkspaceChangesAsync(
            [new WorkspaceChange(WorkspaceChangeKind.Changed, FirstPath)]);

        Assert.True(harness.ViewModel.ShowsExternalChangeBanner);
        Assert.Equal("# my edits", harness.ViewModel.EditorSession!.SourceText);

        // Полоса называет файл (A-External): вкладок с правками может быть несколько.
        Assert.Same(harness.ViewModel, harness.ViewModel.ExternalChangeContent);
        Assert.Equal("first.md changed on disk.", harness.ViewModel.ExternalChangeTitle);
        Assert.Equal("Load from disk", harness.ViewModel.ExternalChangeReload);
        Assert.Equal("Your edits will be lost", harness.ViewModel.ExternalChangeReloadTooltip);
    }

    /// <summary>
    /// Две вкладки с правками изменены снаружи: полоса при переходе между ними называет файл
    /// активной вкладки. Флаг полосы при этом не меняется, и без явного уведомления она
    /// называла бы прошлый файл, а «Загрузить с диска» выбросила бы правки этого.
    /// </summary>
    [Fact]
    public async Task BannerNamesTheActiveTabWhenSwitchingBetweenChangedTabs()
    {
        var harness = await CreateAsync();
        await harness.ViewModel.OpenPathAsync(FirstPath);
        var first = harness.ViewModel.OpenDocuments.ActiveTab!;
        harness.ViewModel.ToggleEditModeCommand.Execute(null);
        harness.ViewModel.EditorSession!.SourceText = "# first edits";
        await harness.ViewModel.OpenPathAsync(SecondPath);
        harness.ViewModel.ToggleEditModeCommand.Execute(null);
        harness.ViewModel.EditorSession!.SourceText = "# second edits";

        await harness.ViewModel.ApplyWorkspaceChangesAsync(
            [new WorkspaceChange(WorkspaceChangeKind.Changed, FirstPath),
             new WorkspaceChange(WorkspaceChangeKind.Changed, SecondPath)]);
        Assert.Equal("second.md changed on disk.", harness.ViewModel.ExternalChangeTitle);

        var notified = new List<string?>();
        harness.ViewModel.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        await harness.ViewModel.OpenDocuments.ActivateCommand.ExecuteAsync(first);

        Assert.True(harness.ViewModel.ShowsExternalChangeBanner);
        Assert.Contains(nameof(ShellViewModel.ExternalChangeTitle), notified);
        Assert.Equal("first.md changed on disk.", harness.ViewModel.ExternalChangeTitle);
    }

    /// <summary>
    /// Чистую активную вкладку не удалось перечитать после чужого сохранения: ошибка встаёт
    /// поверх, а вкладка остаётся документом со снимком и позицией. Esc возвращает к тексту;
    /// раньше вкладка превращалась в ошибку, и Esc закрывал сам документ.
    /// </summary>
    [Fact]
    public async Task FailedSilentReloadKeepsTheTabAsADocument()
    {
        var harness = await CreateAsync();
        await harness.ViewModel.OpenPathAsync(FirstPath);
        var first = harness.ViewModel.OpenDocuments.ActiveTab!;
        harness.ViewModel.ReportScrollOffset(300);

        harness.Loader.NextException = new IOException("The file is in use.");
        await harness.ViewModel.ApplyWorkspaceChangesAsync(
            [new WorkspaceChange(WorkspaceChangeKind.Changed, FirstPath)]);

        Assert.True(harness.ViewModel.IsError);
        Assert.Same(first, harness.ViewModel.OpenDocuments.ActiveTab);
        Assert.False(first.IsLoadError);
        Assert.Equal(300, first.ScrollOffset);

        await harness.ViewModel.ClearErrorCommand.ExecuteAsync(null);

        Assert.Same(first, Assert.Single(harness.ViewModel.OpenDocuments.Tabs));
        Assert.True(harness.ViewModel.IsViewer);
        Assert.Equal("# first", harness.ViewModel.Document!.Content);
    }

    [Fact]
    public async Task KeepingEditsDismissesTheBannerAndLeavesTheText()
    {
        var harness = await CreateAsync();
        await harness.ViewModel.OpenPathAsync(FirstPath);
        harness.ViewModel.ToggleEditModeCommand.Execute(null);
        harness.ViewModel.EditorSession!.SourceText = "# my edits";
        await harness.ViewModel.ApplyWorkspaceChangesAsync(
            [new WorkspaceChange(WorkspaceChangeKind.Changed, FirstPath)]);

        harness.ViewModel.KeepExternalChangeCommand.Execute(null);

        Assert.False(harness.ViewModel.ShowsExternalChangeBanner);
        Assert.Equal("# my edits", harness.ViewModel.EditorSession!.SourceText);
    }

    [Fact]
    public async Task ReloadingDropsTheEditsAndTakesTheDiskVersion()
    {
        var harness = await CreateAsync();
        await harness.ViewModel.OpenPathAsync(FirstPath);
        harness.ViewModel.ToggleEditModeCommand.Execute(null);
        harness.ViewModel.EditorSession!.SourceText = "# my edits";

        harness.Loader.Sources[FirstPath] = new MarkdownSource(FirstPath, "first.md", "# changed outside");
        await harness.ViewModel.ApplyWorkspaceChangesAsync(
            [new WorkspaceChange(WorkspaceChangeKind.Changed, FirstPath)]);

        await harness.ViewModel.ReloadExternalChangeCommand.ExecuteAsync(null);

        Assert.False(harness.ViewModel.ShowsExternalChangeBanner);
        Assert.Equal("# changed outside", harness.ViewModel.Document!.Content);
        Assert.False(harness.ViewModel.IsDirty);
    }

    [Fact]
    public async Task BackgroundTabIsRereadOnlyWhenItComesBack()
    {
        var harness = await CreateAsync();
        await harness.ViewModel.OpenPathAsync(FirstPath);
        await harness.ViewModel.OpenPathAsync(SecondPath);

        harness.Loader.Sources[FirstPath] = new MarkdownSource(FirstPath, "first.md", "# first changed");
        var readsBefore = harness.Loader.LoadCount;

        await harness.ViewModel.ApplyWorkspaceChangesAsync(
            [new WorkspaceChange(WorkspaceChangeKind.Changed, FirstPath)]);

        // Фоновую вкладку не перечитываем: на неё никто не смотрит.
        Assert.Equal(readsBefore, harness.Loader.LoadCount);

        var first = harness.ViewModel.OpenDocuments.Tabs[0];
        await harness.ViewModel.OpenDocuments.ActivateCommand.ExecuteAsync(first);

        Assert.Equal("# first changed", harness.ViewModel.Document!.Content);
    }

    /// <summary>
    /// Фоновую вкладку не удалось перечитать при возврате: ошибка встаёт поверх неё самой,
    /// а вкладка с её снимком остаётся — это документ, а не неудачное открытие. Сессия и
    /// правки вкладки, с которой ушли, остаются там, а не переезжают в shell поверх вкладки
    /// с ошибкой, и Esc показывает её последний снимок, а не чужой документ.
    /// </summary>
    [Fact]
    public async Task FailedRereadOfABackgroundTabLeavesThePreviousTabsEditsInPlace()
    {
        var harness = await CreateAsync();
        await harness.ViewModel.OpenPathAsync(FirstPath);
        var first = harness.ViewModel.OpenDocuments.ActiveTab!;

        await harness.ViewModel.OpenPathAsync(SecondPath);
        harness.ViewModel.ToggleEditModeCommand.Execute(null);
        var second = harness.ViewModel.OpenDocuments.ActiveTab!;
        var secondSession = harness.ViewModel.EditorSession!;
        secondSession.SourceText = "# second edited";

        await harness.ViewModel.ApplyWorkspaceChangesAsync(
            [new WorkspaceChange(WorkspaceChangeKind.Changed, FirstPath)]);
        harness.Loader.Sources.Remove(FirstPath);

        await harness.ViewModel.OpenDocuments.ActivateCommand.ExecuteAsync(first);

        Assert.True(harness.ViewModel.IsError);
        Assert.Same(first, harness.ViewModel.OpenDocuments.ActiveTab);
        Assert.False(first.IsLoadError);
        Assert.Null(harness.ViewModel.EditorSession);
        Assert.False(harness.ViewModel.IsEditMode);
        Assert.Null(first.EditorSession);
        Assert.Same(secondSession, second.EditorSession);
        Assert.True(second.IsEditMode);
        Assert.True(second.IsDirty);

        await harness.ViewModel.ClearErrorCommand.ExecuteAsync(null);

        Assert.Equal(2, harness.ViewModel.OpenDocuments.Tabs.Count);
        Assert.True(harness.ViewModel.IsViewer);
        Assert.Equal("# first", harness.ViewModel.Document!.Content);
        Assert.Equal(FirstPath, harness.ViewModel.CurrentDocumentPath);
        Assert.True(harness.ViewModel.TryQueueCloseRequest());
    }

    [Fact]
    public async Task ExternallyDeletedCleanTabCloses()
    {
        var harness = await CreateAsync();
        await harness.ViewModel.OpenPathAsync(FirstPath);

        await harness.ViewModel.ApplyWorkspaceChangesAsync(
            [new WorkspaceChange(WorkspaceChangeKind.Deleted, FirstPath)]);

        Assert.Empty(harness.ViewModel.OpenDocuments.Tabs);
    }

    [Fact]
    public async Task ExternallyDeletedDirtyTabSurvivesWithAMark()
    {
        var harness = await CreateAsync();
        await harness.ViewModel.OpenPathAsync(FirstPath);
        harness.ViewModel.ToggleEditModeCommand.Execute(null);
        harness.ViewModel.EditorSession!.SourceText = "# unsaved work";

        await harness.ViewModel.ApplyWorkspaceChangesAsync(
            [new WorkspaceChange(WorkspaceChangeKind.Deleted, FirstPath)]);

        var tab = Assert.Single(harness.ViewModel.OpenDocuments.Tabs);
        Assert.Equal("first.md (deleted)", tab.DisplayTitle);
        Assert.Equal("# unsaved work", harness.ViewModel.EditorSession!.SourceText);
    }

    [Fact]
    public async Task ExternalRenameMovesTheTabToTheNewPath()
    {
        var harness = await CreateAsync();
        await harness.ViewModel.OpenPathAsync(FirstPath);

        await harness.ViewModel.ApplyWorkspaceChangesAsync(
            [new WorkspaceChange(WorkspaceChangeKind.Renamed, TestPaths.At("docs", "moved.md"), FirstPath)]);

        var tab = Assert.Single(harness.ViewModel.OpenDocuments.Tabs);
        Assert.Equal(TestPaths.At("docs", "moved.md"), tab.Path);
        Assert.Equal("moved.md", tab.Title);
    }

    [Fact]
    public async Task WatcherStartsWithTheFolderAndStopsWithIt()
    {
        var harness = await CreateAsync();

        Assert.Equal([Root], harness.Watcher.StartedRoots);

        await harness.ViewModel.CloseFolderCommand.ExecuteAsync(null);

        Assert.Equal(1, harness.Watcher.StopCount);
    }

    [Fact]
    public async Task ChangesInOneDirectoryRefreshItOnlyOnce()
    {
        var harness = await CreateAsync();
        harness.FileSystem.EnumeratedPaths.Clear();

        await harness.ViewModel.ApplyWorkspaceChangesAsync(
        [
            new WorkspaceChange(WorkspaceChangeKind.Created, TestPaths.At("docs", "a.md")),
            new WorkspaceChange(WorkspaceChangeKind.Created, TestPaths.At("docs", "b.md")),
            new WorkspaceChange(WorkspaceChangeKind.Changed, TestPaths.At("docs", "c.md"))
        ]);

        Assert.Equal([Root], harness.FileSystem.EnumeratedPaths);
    }

    /// <summary>
    /// Правка снаружи перечитывает каталог, но не должна сворачивать дерево:
    /// раскрытые пользователем папки остаются раскрытыми.
    /// </summary>
    [Fact]
    public async Task ExternalChangeKeepsExpandedDirectories()
    {
        var harness = await CreateAsync();
        var workspace = harness.ViewModel.Workspace!;
        var adr = workspace.Roots.Single(node => node.Name == "adr");
        await workspace.ExpandNodeAsync(adr);

        Assert.True(adr.IsExpanded);

        await harness.ViewModel.ApplyWorkspaceChangesAsync(
            [new WorkspaceChange(WorkspaceChangeKind.Created, TestPaths.At("docs", "third.md"))]);

        var refreshed = workspace.Roots.Single(node => node.Name == "adr");

        Assert.True(refreshed.IsExpanded);
        Assert.True(refreshed.HasLoadedChildren);
    }

    private static async Task<WatcherHarness> CreateAsync()
    {
        var fileSystem = new FakeWorkspaceFileSystem();
        fileSystem.AddDirectory(
            Root,
            WorkspaceEntry.ForDirectory(TestPaths.At("docs", "adr"), "adr"),
            WorkspaceEntry.ForFile(FirstPath, "first.md"),
            WorkspaceEntry.ForFile(SecondPath, "second.md"));
        fileSystem.AddDirectory(TestPaths.At("docs", "adr"), WorkspaceEntry.ForFile(TestPaths.At("docs", "adr", "adr_0001.md"), "adr_0001.md"));

        var loader = new CountingDocumentLoader();
        loader.Sources[FirstPath] = new MarkdownSource(FirstPath, "first.md", "# first");
        loader.Sources[SecondPath] = new MarkdownSource(SecondPath, "second.md", "# second");

        var watcher = new FakeWorkspaceWatcher();
        var platform = new FakePlatformServices(fileSystem);

        var viewModel = new ShellViewModel(
            new OpenDocumentUseCase(loader),
            new SaveDocumentUseCase(new RecordingDocumentSaver()),
            new StubFilePicker(),
            new StubCommandLineActivation(),
            new LocalizationService(AppLanguage.English),
            new InMemorySettingsStore(),
            new RecordingThemeService(),
            new RecordingStartupMetrics(),
            new RenderMarkdownDocumentUseCase(new TestMarkdownRenderer(), new FakeDiagramRenderService()),
            TestUpdates.CreateViewModel(),
            new OpenFolderUseCase(fileSystem),
            new ExpandFolderNodeUseCase(fileSystem),
            new SearchWorkspaceFilesUseCase(fileSystem),
            new WorkspaceFileOperationsUseCase(fileSystem, platform),
            platform,
            () => watcher,
            new RecordingWindowLauncher());

        await viewModel.OpenFolderPathAsync(Root);

        return new WatcherHarness(fileSystem, loader, watcher, viewModel);
    }

    private sealed record WatcherHarness(
        FakeWorkspaceFileSystem FileSystem,
        CountingDocumentLoader Loader,
        FakeWorkspaceWatcher Watcher,
        ShellViewModel ViewModel);
}

/// <summary>Watcher без файловой системы: события подаются тестом напрямую.</summary>
internal sealed class FakeWorkspaceWatcher : IWorkspaceWatcher
{
    public List<string> StartedRoots { get; } = [];

    public int StopCount { get; private set; }

    public event EventHandler<IReadOnlyList<WorkspaceChange>>? Changed;

    public void Start(string rootPath) => StartedRoots.Add(rootPath);

    public void StopWatching() => StopCount++;

    public void Raise(params WorkspaceChange[] changes) => Changed?.Invoke(this, changes);

    public void Dispose() => StopWatching();
}
