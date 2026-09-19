using MarkMello.Application.UseCases;
using MarkMello.Domain;
using MarkMello.Presentation.Localization;
using MarkMello.Presentation.ViewModels;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Editor-сессия принадлежит вкладке: уход на соседнюю не должен терять несохранённый
/// текст, а возврат — показывать его же и в том же режиме.
/// </summary>
public sealed class PerTabEditorSessionTests
{
    [Fact]
    public async Task EditingSurvivesSwitchingToAnotherTabAndBack()
    {
        var harness = CreateHarness();
        await harness.ViewModel.OpenPathAsync(@"C:\docs\first.md");

        harness.ViewModel.ToggleEditModeCommand.Execute(null);
        harness.ViewModel.EditorSession!.SourceText = "# first edited";

        var first = harness.ViewModel.OpenDocuments.Tabs[0];
        Assert.True(first.IsDirty);

        await harness.ViewModel.OpenPathAsync(@"C:\docs\second.md");

        Assert.False(harness.ViewModel.IsEditMode);
        Assert.True(first.IsDirty);

        await harness.ViewModel.OpenDocuments.ActivateCommand.ExecuteAsync(first);

        Assert.True(harness.ViewModel.IsEditMode);
        Assert.Equal("# first edited", harness.ViewModel.EditorSession!.SourceText);
        Assert.True(harness.ViewModel.IsDirty);
    }

    [Fact]
    public async Task SwitchingAwayFromDirtyTabDoesNotAskAnything()
    {
        var harness = CreateHarness();
        await harness.ViewModel.OpenPathAsync(@"C:\docs\first.md");
        harness.ViewModel.ToggleEditModeCommand.Execute(null);
        harness.ViewModel.EditorSession!.SourceText = "# edited";

        await harness.ViewModel.OpenPathAsync(@"C:\docs\second.md");

        Assert.False(harness.ViewModel.IsDirtyPromptOpen);
        Assert.Equal(@"C:\docs\second.md", harness.ViewModel.CurrentDocumentPath);
    }

    [Fact]
    public async Task ClosingDirtyBackgroundTabShowsItAndAsks()
    {
        var harness = CreateHarness();
        await harness.ViewModel.OpenPathAsync(@"C:\docs\first.md");
        harness.ViewModel.ToggleEditModeCommand.Execute(null);
        harness.ViewModel.EditorSession!.SourceText = "# edited";
        var first = harness.ViewModel.OpenDocuments.Tabs[0];

        await harness.ViewModel.OpenPathAsync(@"C:\docs\second.md");
        await harness.ViewModel.OpenDocuments.CloseCommand.ExecuteAsync(first);

        // Пользователю показали именно ту вкладку, о правках которой спрашивают.
        Assert.True(harness.ViewModel.IsDirtyPromptOpen);
        Assert.Same(first, harness.ViewModel.OpenDocuments.ActiveTab);
        Assert.Equal(2, harness.ViewModel.OpenDocuments.Tabs.Count);
    }

    [Fact]
    public async Task DiscardingChangesClosesTheTab()
    {
        var harness = CreateHarness();
        await harness.ViewModel.OpenPathAsync(@"C:\docs\first.md");
        harness.ViewModel.ToggleEditModeCommand.Execute(null);
        harness.ViewModel.EditorSession!.SourceText = "# edited";
        var first = harness.ViewModel.OpenDocuments.Tabs[0];

        await harness.ViewModel.OpenDocuments.CloseCommand.ExecuteAsync(first);
        await harness.ViewModel.ConfirmDirtyDiscardCommand.ExecuteAsync(null);

        Assert.Empty(harness.ViewModel.OpenDocuments.Tabs);
        Assert.False(harness.ViewModel.IsDirtyPromptOpen);
    }

    [Fact]
    public async Task ClosingWindowAsksAboutADirtyBackgroundTab()
    {
        var harness = CreateHarness();
        await harness.ViewModel.OpenPathAsync(@"C:\docs\first.md");
        harness.ViewModel.ToggleEditModeCommand.Execute(null);
        harness.ViewModel.EditorSession!.SourceText = "# edited";

        await harness.ViewModel.OpenPathAsync(@"C:\docs\second.md");

        var queued = harness.ViewModel.TryQueueCloseRequest();

        Assert.True(queued);
        Assert.True(harness.ViewModel.IsDirtyPromptOpen);
    }

    /// <summary>
    /// Раньше окно спрашивало только про первую грязную вкладку и закрывалось сразу после
    /// ответа, молча теряя правки остальных.
    /// </summary>
    [Fact]
    public async Task ClosingWindowAsksAboutEveryDirtyTabInTurn()
    {
        var harness = await CreateHarnessWithTwoDirtyTabsAsync();
        var closeRequests = 0;
        harness.ViewModel.CloseRequested += (_, _) => closeRequests++;

        Assert.True(harness.ViewModel.TryQueueCloseRequest());
        Assert.True(harness.ViewModel.IsDirtyPromptOpen);
        Assert.Equal(@"C:\docs\first.md", harness.ViewModel.CurrentDocumentPath);

        await harness.ViewModel.ConfirmDirtyDiscardCommand.ExecuteAsync(null);

        Assert.Equal(0, closeRequests);
        Assert.True(harness.ViewModel.IsDirtyPromptOpen);
        Assert.Equal(@"C:\docs\second.md", harness.ViewModel.CurrentDocumentPath);

        await harness.ViewModel.ConfirmDirtyDiscardCommand.ExecuteAsync(null);

        Assert.Equal(1, closeRequests);
        Assert.False(harness.ViewModel.IsDirtyPromptOpen);
    }

    [Fact]
    public async Task CancellingOnAnyDirtyTabKeepsTheWindowOpen()
    {
        var harness = await CreateHarnessWithTwoDirtyTabsAsync();
        var closeRequests = 0;
        harness.ViewModel.CloseRequested += (_, _) => closeRequests++;

        harness.ViewModel.TryQueueCloseRequest();
        await harness.ViewModel.ConfirmDirtyDiscardCommand.ExecuteAsync(null);
        harness.ViewModel.CancelDirtyPromptCommand.Execute(null);

        Assert.Equal(0, closeRequests);
        Assert.False(harness.ViewModel.IsDirtyPromptOpen);
        Assert.True(harness.ViewModel.OpenDocuments.Tabs[1].IsDirty);

        // Следующая попытка закрыть окно снова спрашивает про оставшиеся правки.
        Assert.True(harness.ViewModel.TryQueueCloseRequest());
    }

    [Fact]
    public async Task CleanTabsDoNotBlockClosingTheWindow()
    {
        var harness = CreateHarness();
        await harness.ViewModel.OpenPathAsync(@"C:\docs\first.md");
        await harness.ViewModel.OpenPathAsync(@"C:\docs\second.md");

        Assert.False(harness.ViewModel.TryQueueCloseRequest());
    }

    /// <summary>
    /// Раньше сессия активной вкладки при закрытии не гасилась: shell отвязывал её, пока
    /// вкладка ещё была в списке, синхронизация снимала её с вкладки, и tab.Dispose()
    /// оставалось нечего гасить — отложенный рендер preview жил дальше.
    /// </summary>
    [Fact]
    public async Task ClosingTheActiveEditingTabDisposesItsSessionOnce()
    {
        var schedulers = new List<DisposalRecordingPreviewScheduler>();
        var harness = CreateHarness(schedulers);
        await harness.ViewModel.OpenPathAsync(@"C:\docs\first.md");
        await harness.ViewModel.OpenPathAsync(@"C:\docs\second.md");
        harness.ViewModel.ToggleEditModeCommand.Execute(null);
        var scheduler = Assert.Single(schedulers);

        await harness.ViewModel.CloseActiveTabCommand.ExecuteAsync(null);

        Assert.Equal(1, scheduler.DisposeCount);

        // Активность перешла к соседу, и закрытая сессия в него не просочилась.
        var first = Assert.Single(harness.ViewModel.OpenDocuments.Tabs);
        Assert.Same(first, harness.ViewModel.OpenDocuments.ActiveTab);
        Assert.Null(first.EditorSession);
        Assert.Null(harness.ViewModel.EditorSession);
        Assert.False(harness.ViewModel.IsEditMode);
    }

    [Fact]
    public async Task ClosingTheOnlyDraftTabDisposesItsSessionOnce()
    {
        var schedulers = new List<DisposalRecordingPreviewScheduler>();
        var harness = CreateHarness(schedulers);
        await harness.ViewModel.CreateNewDocumentCommand.ExecuteAsync(null);
        var scheduler = Assert.Single(schedulers);

        await harness.ViewModel.CloseActiveTabCommand.ExecuteAsync(null);

        Assert.Equal(1, scheduler.DisposeCount);
        Assert.Empty(harness.ViewModel.OpenDocuments.Tabs);
        Assert.Null(harness.ViewModel.EditorSession);
    }

    [Fact]
    public async Task ClosingABackgroundEditingTabDisposesItsSessionOnce()
    {
        var schedulers = new List<DisposalRecordingPreviewScheduler>();
        var harness = CreateHarness(schedulers);
        await harness.ViewModel.OpenPathAsync(@"C:\docs\first.md");
        harness.ViewModel.ToggleEditModeCommand.Execute(null);
        var scheduler = Assert.Single(schedulers);
        var first = harness.ViewModel.OpenDocuments.Tabs[0];

        await harness.ViewModel.OpenPathAsync(@"C:\docs\second.md");
        await harness.ViewModel.OpenDocuments.CloseCommand.ExecuteAsync(first);

        Assert.Equal(1, scheduler.DisposeCount);
        Assert.Equal(@"C:\docs\second.md", harness.ViewModel.CurrentDocumentPath);
    }

    /// <summary>
    /// ⌘N из вкладки в чтении: черновик правится в своей вкладке, вкладка файла остаётся
    /// в чтении без сессии. Раньше сессия черновика и режим правки доставались вкладке файла,
    /// и возврат на неё показывал пустой редактор черновика вместо документа.
    /// </summary>
    [Fact]
    public async Task NewDocumentFromAReadingTabKeepsEachTabsTextAndMode()
    {
        var schedulers = new List<DisposalRecordingPreviewScheduler>();
        var harness = CreateHarness(schedulers);
        await harness.ViewModel.OpenPathAsync(@"C:\docs\first.md");
        var first = harness.ViewModel.OpenDocuments.ActiveTab!;

        await harness.ViewModel.CreateNewDocumentCommand.ExecuteAsync(null);
        var draft = harness.ViewModel.OpenDocuments.ActiveTab!;
        var draftSession = harness.ViewModel.EditorSession!;
        draftSession.SourceText = "# draft";

        Assert.NotSame(first, draft);
        Assert.Null(first.EditorSession);
        Assert.False(first.IsEditMode);
        Assert.Same(draftSession, draft.EditorSession);
        Assert.True(draft.IsEditMode);

        for (var round = 0; round < 2; round++)
        {
            await harness.ViewModel.OpenDocuments.ActivateCommand.ExecuteAsync(first);

            Assert.False(harness.ViewModel.IsEditMode);
            Assert.Null(harness.ViewModel.EditorSession);
            Assert.Equal(@"C:\docs\first.md", harness.ViewModel.CurrentDocumentPath);
            Assert.Equal("# first", harness.ViewModel.Document!.Content);

            await harness.ViewModel.OpenDocuments.ActivateCommand.ExecuteAsync(draft);

            Assert.True(harness.ViewModel.IsEditMode);
            Assert.Same(draftSession, harness.ViewModel.EditorSession);
            Assert.Equal("# draft", draftSession.SourceText);
            Assert.Null(harness.ViewModel.CurrentDocumentPath);
        }

        var scheduler = Assert.Single(schedulers);
        Assert.Equal(0, scheduler.DisposeCount);

        await CloseAllTabsDiscardingChangesAsync(harness);

        Assert.Equal(1, scheduler.DisposeCount);
    }

    /// <summary>
    /// ⌘N из правки: у вкладки файла остаются её сессия, правки и режим, у черновика — свои.
    /// Раньше вкладка файла получала сессию черновика, а её собственная терялась, так и не
    /// выброшенная. ⌘N о правках не спрашивает — они остаются в своей вкладке (ADR-0009 Rule 3).
    /// </summary>
    [Fact]
    public async Task NewDocumentFromAnEditingTabKeepsEachTabsTextAndMode()
    {
        var schedulers = new List<DisposalRecordingPreviewScheduler>();
        var harness = CreateHarness(schedulers);
        await harness.ViewModel.OpenPathAsync(@"C:\docs\first.md");
        harness.ViewModel.ToggleEditModeCommand.Execute(null);
        var first = harness.ViewModel.OpenDocuments.ActiveTab!;
        var firstSession = harness.ViewModel.EditorSession!;
        firstSession.SourceText = "# first edited";

        await harness.ViewModel.CreateNewDocumentCommand.ExecuteAsync(null);

        Assert.False(harness.ViewModel.IsDirtyPromptOpen);
        var draft = harness.ViewModel.OpenDocuments.ActiveTab!;
        var draftSession = harness.ViewModel.EditorSession!;
        draftSession.SourceText = "# draft";

        Assert.NotSame(first, draft);
        Assert.NotSame(firstSession, draftSession);
        Assert.Same(firstSession, first.EditorSession);
        Assert.True(first.IsEditMode);
        Assert.True(first.IsDirty);
        Assert.Same(draftSession, draft.EditorSession);
        Assert.True(draft.IsEditMode);

        for (var round = 0; round < 2; round++)
        {
            await harness.ViewModel.OpenDocuments.ActivateCommand.ExecuteAsync(first);

            Assert.True(harness.ViewModel.IsEditMode);
            Assert.Same(firstSession, harness.ViewModel.EditorSession);
            Assert.Equal("# first edited", firstSession.SourceText);
            Assert.True(harness.ViewModel.IsDirty);
            Assert.Equal(@"C:\docs\first.md", harness.ViewModel.CurrentDocumentPath);

            await harness.ViewModel.OpenDocuments.ActivateCommand.ExecuteAsync(draft);

            Assert.True(harness.ViewModel.IsEditMode);
            Assert.Same(draftSession, harness.ViewModel.EditorSession);
            Assert.Equal("# draft", draftSession.SourceText);
            Assert.Null(harness.ViewModel.CurrentDocumentPath);
        }

        Assert.Equal(2, schedulers.Count);
        Assert.All(schedulers, static scheduler => Assert.Equal(0, scheduler.DisposeCount));

        await CloseAllTabsDiscardingChangesAsync(harness);

        Assert.All(schedulers, static scheduler => Assert.Equal(1, scheduler.DisposeCount));
    }

    /// <summary>
    /// Под открытым диалогом ⌘N ничего не делает: черновик стал бы активной вкладкой, ответ
    /// достался бы ему, а вкладка, о которой спросили, закрылась бы вместе с правками.
    /// </summary>
    [Fact]
    public async Task NewDocumentDoesNothingWhileTheDirtyPromptIsOpen()
    {
        var harness = CreateHarness();
        await harness.ViewModel.OpenPathAsync(@"C:\docs\first.md");
        harness.ViewModel.ToggleEditModeCommand.Execute(null);
        harness.ViewModel.EditorSession!.SourceText = "# first edited";
        var first = harness.ViewModel.OpenDocuments.ActiveTab!;

        await harness.ViewModel.CloseActiveTabCommand.ExecuteAsync(null);
        Assert.True(harness.ViewModel.IsDirtyPromptOpen);

        await harness.ViewModel.CreateNewDocumentCommand.ExecuteAsync(null);

        Assert.True(harness.ViewModel.IsDirtyPromptOpen);
        Assert.Same(first, Assert.Single(harness.ViewModel.OpenDocuments.Tabs));
        Assert.Same(first, harness.ViewModel.OpenDocuments.ActiveTab);
        Assert.Equal("# first edited", harness.ViewModel.EditorSession!.SourceText);
    }

    /// <summary>Закрывает все вкладки по одной, отвечая «Не сохранять» на вопрос о правках.</summary>
    private static async Task CloseAllTabsDiscardingChangesAsync(EditorTestHarness harness)
    {
        foreach (var tab in harness.ViewModel.OpenDocuments.Tabs.ToList())
        {
            await harness.ViewModel.OpenDocuments.CloseCommand.ExecuteAsync(tab);
            if (harness.ViewModel.IsDirtyPromptOpen)
            {
                await harness.ViewModel.ConfirmDirtyDiscardCommand.ExecuteAsync(null);
            }
        }

        Assert.Empty(harness.ViewModel.OpenDocuments.Tabs);
    }

    private static async Task<EditorTestHarness> CreateHarnessWithTwoDirtyTabsAsync()
    {
        var harness = CreateHarness();

        await harness.ViewModel.OpenPathAsync(@"C:\docs\first.md");
        harness.ViewModel.ToggleEditModeCommand.Execute(null);
        harness.ViewModel.EditorSession!.SourceText = "# first edited";

        await harness.ViewModel.OpenPathAsync(@"C:\docs\second.md");
        harness.ViewModel.ToggleEditModeCommand.Execute(null);
        harness.ViewModel.EditorSession!.SourceText = "# second edited";

        return harness;
    }

    private static EditorTestHarness CreateHarness(List<DisposalRecordingPreviewScheduler>? schedulers = null)
    {
        var loader = new StubDocumentLoader();
        loader.Sources[@"C:\docs\first.md"] = new MarkdownSource(@"C:\docs\first.md", "first.md", "# first");
        loader.Sources[@"C:\docs\second.md"] = new MarkdownSource(@"C:\docs\second.md", "second.md", "# second");

        var fileSystem = new FakeWorkspaceFileSystem();

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
            new StubUpdateService(),
            new OpenFolderUseCase(fileSystem),
            new ExpandFolderNodeUseCase(fileSystem),
            new SearchWorkspaceFilesUseCase(fileSystem),
            new WorkspaceFileOperationsUseCase(fileSystem, new FakePlatformServices()),
            new FakePlatformServices(),
            static () => new FakeWorkspaceWatcher(),
            new RecordingWindowLauncher(),
            previewSchedulerFactory: schedulers is null
                ? null
                : () =>
                {
                    var scheduler = new DisposalRecordingPreviewScheduler();
                    schedulers.Add(scheduler);
                    return scheduler;
                });

        return new EditorTestHarness(loader, viewModel);
    }

    private sealed record EditorTestHarness(StubDocumentLoader Loader, ShellViewModel ViewModel);
}
