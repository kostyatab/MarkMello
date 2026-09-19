using MarkMello.Application.UseCases;
using MarkMello.Domain;
using MarkMello.Domain.Workspace;
using MarkMello.Presentation.Localization;
using MarkMello.Presentation.ViewModels;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Сворачивание сайдбара и счётчик в подвале — два куска макета, отложенные в M1
/// до того, как станет понятно, как возвращать свёрнутый сайдбар.
/// </summary>
public sealed class SidebarChromeTests
{
    private static readonly string Root = TestPaths.At("docs");

    [Fact]
    public async Task CollapsingKeepsTheFolderOpenAndPutsTheShowButtonIntoTheRow()
    {
        var harness = await CreateAsync();

        harness.ViewModel.ToggleSidebarCommand.Execute(null);

        Assert.False(harness.ViewModel.ShowsSidebar);
        Assert.Null(harness.ViewModel.SidebarContent);
        Assert.True(harness.ViewModel.ShowsExpandSidebarButton);

        // Папка остаётся открытой: сворачивание — про место на экране, а не про выход из режима.
        Assert.NotNull(harness.ViewModel.Workspace);
        Assert.True(harness.ViewModel.CanCloseFolder);
    }

    /// <summary>
    /// Свёрнутое дерево возвращает кнопка в строке окна: возврат только пунктом меню
    /// найти не получалось. Подсказка у кнопки говорит, что она сделает, и с сочетанием.
    /// </summary>
    [Fact]
    public async Task CollapsedSidebarGetsItsOwnButtonBack()
    {
        var harness = await CreateAsync();

        Assert.False(harness.ViewModel.ShowsExpandSidebarButton);
        Assert.Equal("Hide file panel (Ctrl+B)", harness.ViewModel.SidebarToggleTooltip);

        harness.ViewModel.ToggleSidebarCommand.Execute(null);

        Assert.True(harness.ViewModel.ShowsExpandSidebarButton);
        Assert.Equal("Show file panel (Ctrl+B)", harness.ViewModel.SidebarToggleTooltip);

        harness.ViewModel.ToggleSidebarCommand.Execute(null);

        Assert.False(harness.ViewModel.ShowsExpandSidebarButton);
    }

    [Fact]
    public async Task TheSameCommandBringsTheSidebarBack()
    {
        var harness = await CreateAsync();

        harness.ViewModel.ToggleSidebarCommand.Execute(null);
        harness.ViewModel.ToggleSidebarCommand.Execute(null);

        Assert.True(harness.ViewModel.ShowsSidebar);
        Assert.NotNull(harness.ViewModel.SidebarContent);
        Assert.False(harness.ViewModel.ShowsExpandSidebarButton);
    }

    [Fact]
    public async Task ReopeningAFolderExpandsTheSidebarAgain()
    {
        var harness = await CreateAsync();
        harness.ViewModel.ToggleSidebarCommand.Execute(null);

        await harness.ViewModel.CloseFolderCommand.ExecuteAsync(null);
        await harness.ViewModel.OpenFolderPathAsync(Root);

        Assert.True(harness.ViewModel.ShowsSidebar);
    }

    /// <summary>Без папки возвращать нечего: кнопка не появляется и в single-file режиме.</summary>
    [Fact]
    public void WithoutAFolderThereIsNoSidebarButton()
    {
        var shell = CreateShell();

        Assert.False(shell.ShowsExpandSidebarButton);
    }

    [Fact]
    public void WithoutAFolderThereIsNothingToToggle()
    {
        var harness = CreateShell();

        Assert.False(harness.CanToggleSidebar);
        Assert.False(harness.ToggleSidebarCommand.CanExecute(null));
    }

    [Fact]
    public async Task FooterCountsWhatTheTreeHasLoaded()
    {
        var harness = await CreateAsync();

        // Корень: README.md и first.md — каталог adr ещё не раскрыт.
        Assert.Equal(2, harness.ViewModel.Workspace!.LoadedDocumentCount);
        Assert.Equal("Documents: 2", harness.ViewModel.SidebarFooterLabel);

        var adr = harness.ViewModel.Workspace.Roots.Single(node => node.Name == "adr");
        await harness.ViewModel.Workspace.ExpandNodeAsync(adr);

        Assert.Equal(3, harness.ViewModel.Workspace.LoadedDocumentCount);
        Assert.Equal("Documents: 3", harness.ViewModel.SidebarFooterLabel);
    }

    [Fact]
    public async Task FooterMentionsUnsavedDocumentsOfThisFolder()
    {
        var harness = await CreateAsync();
        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "first.md"));
        harness.ViewModel.ToggleEditModeCommand.Execute(null);
        harness.ViewModel.EditorSession!.SourceText = "# edited";

        Assert.Equal("Documents: 2 · Unsaved: 1", harness.ViewModel.SidebarFooterLabel);
    }

    [Fact]
    public async Task EmptyStateDisappearsAsSoonAsADocumentOpens()
    {
        var harness = await CreateAsync();

        // Регресс: вкладка регистрируется до перехода состояния в Viewing, и без
        // повторного уведомления заглушка «Документ не выбран» оставалась поверх документа.
        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "first.md"));

        Assert.False(harness.ViewModel.IsEmptyDocumentSurface);
        Assert.False(harness.ViewModel.IsWelcome);
        Assert.True(harness.ViewModel.IsViewer);
    }

    [Fact]
    public async Task TreeRowShowsTheSameDirtyMarkAsTheTab()
    {
        var harness = await CreateAsync();
        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "first.md"));
        var node = harness.ViewModel.Workspace!.Roots.Single(candidate => candidate.Name == "first.md");

        Assert.False(node.IsDirty);

        harness.ViewModel.ToggleEditModeCommand.Execute(null);
        harness.ViewModel.EditorSession!.SourceText = "# edited";

        Assert.True(node.IsDirty);

        harness.ViewModel.EditorSession.DiscardChanges();

        Assert.False(node.IsDirty);
    }

    private static async Task<ChromeHarness> CreateAsync()
    {
        var shell = CreateShell();
        await shell.OpenFolderPathAsync(Root);
        return new ChromeHarness(shell);
    }

    private static ShellViewModel CreateShell()
    {
        var fileSystem = new FakeWorkspaceFileSystem();
        fileSystem.AddDirectory(
            Root,
            WorkspaceEntry.ForDirectory(TestPaths.At("docs", "adr"), "adr"),
            WorkspaceEntry.ForFile(TestPaths.At("docs", "README.md"), "README.md"),
            WorkspaceEntry.ForFile(TestPaths.At("docs", "first.md"), "first.md"));
        fileSystem.AddDirectory(
            TestPaths.At("docs", "adr"),
            WorkspaceEntry.ForFile(TestPaths.At("docs", "adr", "adr_0001.md"), "adr_0001.md"));

        var loader = new StubDocumentLoader();
        loader.Sources[TestPaths.At("docs", "README.md")] = new MarkdownSource(TestPaths.At("docs", "README.md"), "README.md", "# readme");
        loader.Sources[TestPaths.At("docs", "first.md")] = new MarkdownSource(TestPaths.At("docs", "first.md"), "first.md", "# first");

        var platform = new FakePlatformServices(fileSystem);

        return new ShellViewModel(
            new OpenDocumentUseCase(loader),
            new SaveDocumentUseCase(new RecordingDocumentSaver()),
            new StubFilePicker(),
            new StubCommandLineActivation(),
            new LocalizationService(AppLanguage.English),
            new InMemorySettingsStore(),
            new RecordingThemeService(),
            new RecordingStartupMetrics(),
            new RenderMarkdownDocumentUseCase(new TestMarkdownRenderer(), new FakeDiagramRenderService()),
            new StubUpdateService(),
            new OpenFolderUseCase(fileSystem),
            new ExpandFolderNodeUseCase(fileSystem),
            new SearchWorkspaceFilesUseCase(fileSystem),
            new WorkspaceFileOperationsUseCase(fileSystem, platform),
            platform,
            static () => new FakeWorkspaceWatcher(),
            new RecordingWindowLauncher());
    }

    private sealed record ChromeHarness(ShellViewModel ViewModel);
}
