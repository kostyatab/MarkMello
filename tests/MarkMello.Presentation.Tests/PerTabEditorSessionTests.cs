using MarkMello.Application.UseCases;
using MarkMello.Domain;
using MarkMello.Domain.Workspace;
using MarkMello.Presentation.Localization;
using MarkMello.Presentation.ViewModels;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Editor-сессия принадлежит вкладке: уход на соседнюю не должен терять несохранённый
/// текст, а возврат — показывать его же и в том же режиме.
/// </summary>
public sealed class PerTabEditorSessionTests
{
    private static readonly string NotesFolder = TestPaths.At("notes");
    private static readonly string NotesReadme = TestPaths.At("notes", "README.md");
    private static readonly string NotesTodo = TestPaths.At("notes", "todo.md");

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

    /// <summary>
    /// Неудачное открытие из правки получает свою вкладку ошибки, а вкладка с правками уходит
    /// в фон со своей сессией, текстом и режимом. Раньше ошибка снимала сессию с активной
    /// вкладки, так и не выбросив её: правки пропадали, а окно закрывалось, ни о чём не спросив.
    /// </summary>
    [Fact]
    public async Task FailedOpenFromAnEditingTabKeepsItsSessionTextAndMode()
    {
        var schedulers = new List<DisposalRecordingPreviewScheduler>();
        var harness = CreateHarness(schedulers);
        await harness.ViewModel.OpenPathAsync(@"C:\docs\first.md");
        harness.ViewModel.ToggleEditModeCommand.Execute(null);
        var first = harness.ViewModel.OpenDocuments.ActiveTab!;
        var firstSession = harness.ViewModel.EditorSession!;
        firstSession.SourceText = "# first edited";

        await harness.ViewModel.OpenPathAsync(@"C:\docs\missing.md");

        Assert.True(harness.ViewModel.IsError);
        Assert.Equal(2, harness.ViewModel.OpenDocuments.Tabs.Count);
        var errorTab = harness.ViewModel.OpenDocuments.ActiveTab!;
        Assert.True(errorTab.IsLoadError);
        Assert.Null(harness.ViewModel.EditorSession);
        Assert.False(harness.ViewModel.IsEditMode);
        Assert.Same(firstSession, first.EditorSession);
        Assert.True(first.IsEditMode);
        Assert.True(first.IsDirty);
        Assert.Equal("# first edited", firstSession.SourceText);

        // Esc закрывает вкладку ошибки и возвращает вкладку в правку с тем же текстом.
        await harness.ViewModel.ClearErrorCommand.ExecuteAsync(null);

        Assert.Same(first, Assert.Single(harness.ViewModel.OpenDocuments.Tabs));
        Assert.True(harness.ViewModel.IsViewer);
        Assert.Same(firstSession, harness.ViewModel.ActiveDocumentContent);
        Assert.True(harness.ViewModel.IsDirty);

        // Закрытие окна по-прежнему спрашивает о правках.
        Assert.True(harness.ViewModel.TryQueueCloseRequest());
        Assert.True(harness.ViewModel.IsDirtyPromptOpen);
        harness.ViewModel.CancelDirtyPromptCommand.Execute(null);

        var scheduler = Assert.Single(schedulers);
        Assert.Equal(0, scheduler.DisposeCount);

        await CloseAllTabsDiscardingChangesAsync(harness);

        Assert.Equal(1, scheduler.DisposeCount);
    }

    /// <summary>
    /// Неудачное открытие из вкладки в чтении: её сессия после «Готово» и режим чтения
    /// остаются, правки фоновой вкладки тоже, а Esc возвращает именно к ней, а не к соседке
    /// вкладки ошибки. Раньше ошибка отвязывала сессию читаемой вкладки, так и не выбросив её.
    /// </summary>
    [Fact]
    public async Task FailedOpenFromAReadingTabKeepsItsSessionAndMode()
    {
        var schedulers = new List<DisposalRecordingPreviewScheduler>();
        var harness = CreateHarness(schedulers);
        await harness.ViewModel.OpenPathAsync(@"C:\docs\first.md");
        harness.ViewModel.ToggleEditModeCommand.Execute(null);
        var first = harness.ViewModel.OpenDocuments.ActiveTab!;
        var firstSession = harness.ViewModel.EditorSession!;
        firstSession.SourceText = "# first edited";

        await harness.ViewModel.OpenPathAsync(@"C:\docs\second.md");
        harness.ViewModel.ToggleEditModeCommand.Execute(null);
        var second = harness.ViewModel.OpenDocuments.ActiveTab!;
        var secondSession = harness.ViewModel.EditorSession!;
        await harness.ViewModel.ToggleEditModeCommand.ExecuteAsync(null);

        await harness.ViewModel.OpenPathAsync(@"C:\docs\missing.md");

        Assert.True(harness.ViewModel.IsError);
        Assert.Equal(3, harness.ViewModel.OpenDocuments.Tabs.Count);
        Assert.True(harness.ViewModel.OpenDocuments.ActiveTab!.IsLoadError);
        Assert.Same(secondSession, second.EditorSession);
        Assert.False(second.IsEditMode);
        Assert.Null(harness.ViewModel.EditorSession);
        Assert.Same(firstSession, first.EditorSession);
        Assert.True(first.IsEditMode);
        Assert.True(first.IsDirty);
        Assert.Equal("# first edited", firstSession.SourceText);

        // Esc закрывает вкладку ошибки и возвращает вкладку в чтение того же документа.
        await harness.ViewModel.ClearErrorCommand.ExecuteAsync(null);

        Assert.Equal(2, harness.ViewModel.OpenDocuments.Tabs.Count);
        Assert.Same(second, harness.ViewModel.OpenDocuments.ActiveTab);
        Assert.Same(secondSession, harness.ViewModel.EditorSession);
        Assert.True(harness.ViewModel.IsViewer);
        Assert.Same(harness.ViewModel, harness.ViewModel.ActiveDocumentContent);
        Assert.Equal("# second", harness.ViewModel.Document!.Content);

        // Закрытие окна по-прежнему спрашивает о правках фоновой вкладки.
        Assert.True(harness.ViewModel.TryQueueCloseRequest());
        Assert.True(harness.ViewModel.IsDirtyPromptOpen);
        harness.ViewModel.CancelDirtyPromptCommand.Execute(null);

        Assert.Equal(2, schedulers.Count);
        Assert.All(schedulers, static scheduler => Assert.Equal(0, scheduler.DisposeCount));

        await CloseAllTabsDiscardingChangesAsync(harness);

        Assert.All(schedulers, static scheduler => Assert.Equal(1, scheduler.DisposeCount));
    }

    /// <summary>
    /// Ctrl+Tab под диалогом переключал вкладку, и ответ доставался ей: «Не сохранять» стирало
    /// правки соседней вкладки, а спрошенная закрывалась вместе со своими — без вопроса.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public async Task SwitchingTabsByKeyboardDoesNothingWhileTheDirtyPromptIsOpen(int direction)
    {
        var harness = await CreateHarnessWithTwoDirtyTabsAsync();
        var first = harness.ViewModel.OpenDocuments.Tabs[0];
        var second = harness.ViewModel.OpenDocuments.Tabs[1];

        await harness.ViewModel.OpenDocuments.CloseCommand.ExecuteAsync(first);
        Assert.True(harness.ViewModel.IsDirtyPromptOpen);

        var switchTab = direction > 0
            ? harness.ViewModel.ActivateNextTabCommand
            : harness.ViewModel.ActivatePreviousTabCommand;
        await switchTab.ExecuteAsync(null);

        Assert.Same(first, harness.ViewModel.OpenDocuments.ActiveTab);
        Assert.Equal("# first edited", harness.ViewModel.EditorSession!.SourceText);

        await harness.ViewModel.ConfirmDirtyDiscardCommand.ExecuteAsync(null);

        AssertOnlySecondTabKeepsItsChanges(harness, second);
    }

    /// <summary>Клик по вкладке и выбор из «ещё N» идут одной командой — под диалогом она молчит.</summary>
    [Fact]
    public async Task ActivatingAnotherTabDoesNothingWhileTheDirtyPromptIsOpen()
    {
        var harness = await CreateHarnessWithTwoDirtyTabsAsync();
        var first = harness.ViewModel.OpenDocuments.Tabs[0];
        var second = harness.ViewModel.OpenDocuments.Tabs[1];

        await harness.ViewModel.OpenDocuments.CloseCommand.ExecuteAsync(first);
        await harness.ViewModel.OpenDocuments.ActivateCommand.ExecuteAsync(second);

        Assert.True(harness.ViewModel.IsDirtyPromptOpen);
        Assert.Same(first, harness.ViewModel.OpenDocuments.ActiveTab);
        Assert.Equal("# first edited", harness.ViewModel.EditorSession!.SourceText);

        await harness.ViewModel.ConfirmDirtyDiscardCommand.ExecuteAsync(null);

        AssertOnlySecondTabKeepsItsChanges(harness, second);
    }

    /// <summary>
    /// Под диалогом не закрывается ни одна вкладка: × на грязной фоновой вкладке раньше
    /// показывал её вместо спрошенной, а на чистой — молча закрывал.
    /// </summary>
    [Fact]
    public async Task ClosingTabsDoesNothingWhileTheDirtyPromptIsOpen()
    {
        var harness = await CreateHarnessWithTwoDirtyTabsAsync();
        await harness.ViewModel.OpenPathAsync(@"C:\docs\third.md");
        var tabs = harness.ViewModel.OpenDocuments.Tabs.ToList();
        var first = tabs[0];

        await harness.ViewModel.OpenDocuments.CloseCommand.ExecuteAsync(first);
        Assert.True(harness.ViewModel.IsDirtyPromptOpen);

        await harness.ViewModel.OpenDocuments.CloseCommand.ExecuteAsync(tabs[1]);
        await harness.ViewModel.OpenDocuments.CloseCommand.ExecuteAsync(tabs[2]);
        await harness.ViewModel.CloseActiveTabCommand.ExecuteAsync(null);

        Assert.True(harness.ViewModel.IsDirtyPromptOpen);
        Assert.Equal(tabs, harness.ViewModel.OpenDocuments.Tabs);
        Assert.Same(first, harness.ViewModel.OpenDocuments.ActiveTab);
        Assert.Equal("# first edited", harness.ViewModel.EditorSession!.SourceText);
    }

    /// <summary>Папка в этом окне восстановила бы свои вкладки и сменила активную под вопросом.</summary>
    [Fact]
    public async Task OpeningAFolderInThisWindowDoesNothingWhileTheDirtyPromptIsOpen()
    {
        var harness = await CreateHarnessWithOneDirtyTabAsync();
        await harness.ViewModel.CloseActiveTabCommand.ExecuteAsync(null);

        await harness.ViewModel.OpenFolderPathAsync(NotesFolder);

        Assert.True(harness.ViewModel.IsDirtyPromptOpen);
        Assert.Null(harness.ViewModel.Workspace);
        Assert.Equal(ViewState.Viewing, harness.ViewModel.State);
        Assert.Equal(@"C:\docs\first.md", harness.ViewModel.CurrentDocumentPath);
    }

    /// <summary>
    /// Файл из Finder, пришедший под диалогом, раньше открывался сразу и становился активным:
    /// ответ доставался ему. Теперь такие файлы ждут ответа и открываются по порядку.
    /// </summary>
    [Fact]
    public async Task FilesFromTheSystemOpenAfterTheDirtyPromptIsAnswered()
    {
        var harness = await CreateHarnessWithOneDirtyTabAsync();
        var first = harness.ViewModel.OpenDocuments.ActiveTab!;
        await harness.ViewModel.CloseActiveTabCommand.ExecuteAsync(null);

        harness.Activation.RaiseFileActivated(@"C:\docs\second.md");
        harness.Activation.RaiseFileActivated(@"C:\docs\third.md");

        Assert.True(harness.ViewModel.IsDirtyPromptOpen);
        Assert.Same(first, Assert.Single(harness.ViewModel.OpenDocuments.Tabs));
        Assert.Same(first, harness.ViewModel.OpenDocuments.ActiveTab);

        await harness.ViewModel.ConfirmDirtyDiscardCommand.ExecuteAsync(null);

        Assert.False(harness.ViewModel.IsDirtyPromptOpen);
        Assert.Equal(
            [@"C:\docs\second.md", @"C:\docs\third.md"],
            harness.ViewModel.OpenDocuments.Tabs.Select(static tab => tab.Path));
        Assert.Equal(@"C:\docs\third.md", harness.ViewModel.CurrentDocumentPath);
    }

    [Fact]
    public async Task FilesFromTheSystemOpenAfterTheDirtyPromptIsCancelled()
    {
        var harness = await CreateHarnessWithOneDirtyTabAsync();
        var first = harness.ViewModel.OpenDocuments.ActiveTab!;
        await harness.ViewModel.CloseActiveTabCommand.ExecuteAsync(null);

        harness.Activation.RaiseFileActivated(@"C:\docs\second.md");
        Assert.Same(first, harness.ViewModel.OpenDocuments.ActiveTab);

        await harness.ViewModel.CancelDirtyPromptCommand.ExecuteAsync(null);

        Assert.False(harness.ViewModel.IsDirtyPromptOpen);
        Assert.Equal(@"C:\docs\second.md", harness.ViewModel.CurrentDocumentPath);
        Assert.Equal(2, harness.ViewModel.OpenDocuments.Tabs.Count);
        Assert.True(first.IsDirty);
        Assert.Equal("# first edited", first.EditorSession!.SourceText);
    }

    /// <summary>
    /// Файл, пришедший, пока окно спрашивает о правках перед закрытием, ждёт вместе со всеми:
    /// на первой вкладке он увёл бы ответ на себя, а после последней окну уже не до него.
    /// </summary>
    [Fact]
    public async Task FilesFromTheSystemDoNotOpenWhenTheWindowCloses()
    {
        var harness = await CreateHarnessWithTwoDirtyTabsAsync();
        var closeRequests = 0;
        harness.ViewModel.CloseRequested += (_, _) => closeRequests++;

        Assert.True(harness.ViewModel.TryQueueCloseRequest());
        harness.Activation.RaiseFileActivated(@"C:\docs\third.md");

        Assert.Equal(@"C:\docs\first.md", harness.ViewModel.CurrentDocumentPath);

        await harness.ViewModel.ConfirmDirtyDiscardCommand.ExecuteAsync(null);

        // Очередь грязных вкладок продолжается — файл всё ещё ждёт.
        Assert.True(harness.ViewModel.IsDirtyPromptOpen);
        Assert.Equal(@"C:\docs\second.md", harness.ViewModel.CurrentDocumentPath);

        await harness.ViewModel.ConfirmDirtyDiscardCommand.ExecuteAsync(null);

        Assert.Equal(1, closeRequests);
        Assert.DoesNotContain(
            harness.ViewModel.OpenDocuments.Tabs,
            static tab => tab.Path == @"C:\docs\third.md");
    }

    /// <summary>
    /// ⌘O, ⌘N, перетаскивание и клик в дереве открывают документ в своей вкладке и ни о чём
    /// не спрашивают: правки активной остаются в её сессии (ADR-0009 Rule 3). Раньше
    /// поднимался диалог, и «Не сохранять» стирало правки, которым ничего не грозило.
    /// </summary>
    [Theory]
    [InlineData("open")]
    [InlineData("new")]
    [InlineData("drop")]
    [InlineData("tree")]
    public async Task OpeningADocumentDoesNotAskAboutTheActiveTabsChanges(string entryPoint)
    {
        var harness = await CreateHarnessWithDirtyFolderReadmeAsync();
        var readme = harness.ViewModel.OpenDocuments.ActiveTab!;

        await OpenAsync(harness, entryPoint, NotesTodo);

        Assert.False(harness.ViewModel.IsDirtyPromptOpen);
        Assert.Equal(2, harness.ViewModel.OpenDocuments.Tabs.Count);
        Assert.NotSame(readme, harness.ViewModel.OpenDocuments.ActiveTab);
        Assert.True(readme.IsDirty);
        Assert.True(readme.IsEditMode);
        Assert.Equal("# readme edited", readme.EditorSession!.SourceText);
    }

    /// <summary>
    /// Файл, который уже открыт с несохранёнными правками, не перечитывается с диска, а просто
    /// показывается: без вопроса перечитывание молча выбросило бы правки.
    /// </summary>
    [Theory]
    [InlineData("open")]
    [InlineData("drop")]
    [InlineData("tree")]
    [InlineData("system")]
    public async Task OpeningAFileWithUnsavedChangesShowsItsTabWithoutRereading(string entryPoint)
    {
        var harness = await CreateHarnessWithDirtyFolderReadmeAsync();
        var readme = harness.ViewModel.OpenDocuments.ActiveTab!;
        await harness.ViewModel.OpenPathAsync(NotesTodo);
        harness.Loader.Sources[NotesReadme] = new MarkdownSource(NotesReadme, "README.md", "# readme changed on disk");

        await OpenAsync(harness, entryPoint, NotesReadme);

        Assert.False(harness.ViewModel.IsDirtyPromptOpen);
        Assert.Equal(2, harness.ViewModel.OpenDocuments.Tabs.Count);
        Assert.Same(readme, harness.ViewModel.OpenDocuments.ActiveTab);
        Assert.True(harness.ViewModel.IsEditMode);
        Assert.Equal("# readme edited", harness.ViewModel.EditorSession!.SourceText);
        Assert.Equal("# readme", harness.ViewModel.Document!.Content);
    }

    /// <summary>Неудачное открытие оставило ошибку поверх вкладки с правками — её показ убирает ошибку.</summary>
    [Fact]
    public async Task OpeningTheActiveFileWithUnsavedChangesDismissesALoadError()
    {
        var harness = await CreateHarnessWithDirtyFolderReadmeAsync();
        await harness.ViewModel.OpenDroppedFileAsync(TestPaths.At("notes", "missing.md"));
        Assert.Equal(ViewState.LoadError, harness.ViewModel.State);

        await harness.ViewModel.OpenDroppedFileAsync(NotesReadme);

        Assert.Equal(ViewState.Viewing, harness.ViewModel.State);
        Assert.Equal("# readme edited", harness.ViewModel.EditorSession!.SourceText);
    }

    /// <summary>
    /// Под диалогом о правках ничего не открывается: новая вкладка стала бы активной, и
    /// ответ достался бы ей (MM-36). Окно выбора файла при этом даже не показывается.
    /// </summary>
    [Theory]
    [InlineData("open")]
    [InlineData("drop")]
    [InlineData("tree")]
    public async Task OpeningADocumentDoesNothingWhileTheDirtyPromptIsOpen(string entryPoint)
    {
        var harness = await CreateHarnessWithDirtyFolderReadmeAsync();
        var readme = harness.ViewModel.OpenDocuments.ActiveTab!;
        await harness.ViewModel.CloseActiveTabCommand.ExecuteAsync(null);
        Assert.True(harness.ViewModel.IsDirtyPromptOpen);

        await OpenAsync(harness, entryPoint, NotesTodo);

        Assert.True(harness.ViewModel.IsDirtyPromptOpen);
        Assert.Same(readme, Assert.Single(harness.ViewModel.OpenDocuments.Tabs));
        Assert.Same(readme, harness.ViewModel.OpenDocuments.ActiveTab);
        Assert.Equal(0, harness.Picker.PickMarkdownFileCallCount);
        Assert.Equal("# readme edited", harness.ViewModel.EditorSession!.SourceText);
    }

    private static async Task OpenAsync(EditorTestHarness harness, string entryPoint, string path)
    {
        switch (entryPoint)
        {
            case "open":
                harness.Picker.OpenPath = path;
                await harness.ViewModel.OpenFileCommand.ExecuteAsync(null);
                break;

            case "new":
                await harness.ViewModel.CreateNewDocumentCommand.ExecuteAsync(null);
                break;

            case "drop":
                await harness.ViewModel.OpenDroppedFileAsync(path);
                break;

            case "tree":
                var workspace = harness.ViewModel.Workspace!;
                await workspace.OpenNodeCommand.ExecuteAsync(workspace.Roots.Single(row => row.Path == path));
                break;

            case "system":
                harness.Activation.RaiseFileActivated(path);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(entryPoint), entryPoint, null);
        }
    }

    /// <summary>Папка notes: README.md открывается сам (ADR-0007 Rule 2) и правится.</summary>
    private static async Task<EditorTestHarness> CreateHarnessWithDirtyFolderReadmeAsync()
    {
        var harness = CreateHarness();

        await harness.ViewModel.OpenFolderPathAsync(NotesFolder);
        Assert.Equal(NotesReadme, harness.ViewModel.CurrentDocumentPath);

        harness.ViewModel.ToggleEditModeCommand.Execute(null);
        harness.ViewModel.EditorSession!.SourceText = "# readme edited";

        return harness;
    }

    private static void AssertOnlySecondTabKeepsItsChanges(EditorTestHarness harness, DocumentTabViewModel second)
    {
        Assert.False(harness.ViewModel.IsDirtyPromptOpen);
        Assert.Same(second, Assert.Single(harness.ViewModel.OpenDocuments.Tabs));
        Assert.True(second.IsDirty);
        Assert.Equal("# second edited", second.EditorSession!.SourceText);
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

    private static async Task<EditorTestHarness> CreateHarnessWithOneDirtyTabAsync()
    {
        var harness = CreateHarness();

        await harness.ViewModel.OpenPathAsync(@"C:\docs\first.md");
        harness.ViewModel.ToggleEditModeCommand.Execute(null);
        harness.ViewModel.EditorSession!.SourceText = "# first edited";

        return harness;
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
        loader.Sources[@"C:\docs\third.md"] = new MarkdownSource(@"C:\docs\third.md", "third.md", "# third");
        loader.Sources[NotesReadme] = new MarkdownSource(NotesReadme, "README.md", "# readme");
        loader.Sources[NotesTodo] = new MarkdownSource(NotesTodo, "todo.md", "# todo");

        var fileSystem = new FakeWorkspaceFileSystem();
        fileSystem.AddDirectory(
            NotesFolder,
            WorkspaceEntry.ForFile(NotesReadme, "README.md"),
            WorkspaceEntry.ForFile(NotesTodo, "todo.md"));
        var activation = new StubCommandLineActivation();
        var picker = new StubFilePicker();

        var viewModel = new ShellViewModel(
            new OpenDocumentUseCase(loader),
            new SaveDocumentUseCase(new RecordingDocumentSaver()),
            picker,
            activation,
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

        return new EditorTestHarness(loader, activation, picker, viewModel);
    }

    private sealed record EditorTestHarness(
        StubDocumentLoader Loader,
        StubCommandLineActivation Activation,
        StubFilePicker Picker,
        ShellViewModel ViewModel);
}
