using MarkMello.Application.UseCases;
using MarkMello.Domain;
using MarkMello.Domain.Workspace;
using MarkMello.Presentation.Localization;
using MarkMello.Presentation.ViewModels;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// M2: вкладки на уровне окна — открытие, переключение без перечитывания файла,
/// закрытие и пустое состояние при открытой папке.
/// </summary>
public sealed class DocumentTabsShellTests
{
    private static readonly string Root = TestPaths.At("docs");

    [Fact]
    public async Task OpeningTwoDocumentsKeepsBothAsTabs()
    {
        var harness = CreateHarness();

        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "first.md"));
        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "second.md"));

        Assert.Equal(["first.md", "second.md"], harness.ViewModel.OpenDocuments.Tabs.Select(tab => tab.Title));
        Assert.Equal(TestPaths.At("docs", "second.md"), harness.ViewModel.OpenDocuments.ActiveTab!.Path);
        Assert.True(harness.ViewModel.ShowsTabStrip);
    }

    /// <summary>
    /// Подпись «ещё N» собирает shell, а число вкладок в переполнении считает
    /// OpenDocuments: без уведомления кнопка появлялась с текстом «0 more».
    /// </summary>
    [Fact]
    public async Task OverflowLabelIsNotifiedWhenTabsStopFitting()
    {
        var harness = CreateHarness();
        harness.ViewModel.OpenDocuments.AvailableWidth = 1000;

        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "first.md"));
        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "second.md"));

        var notified = 0;
        harness.ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShellViewModel.TabsOverflowLabel))
            {
                notified++;
            }
        };

        harness.ViewModel.OpenDocuments.AvailableWidth = 120;

        Assert.True(harness.ViewModel.OpenDocuments.HasOverflow);
        Assert.True(notified > 0);
        Assert.Equal(
            $"{harness.ViewModel.OpenDocuments.OverflowTabs.Count} more",
            harness.ViewModel.TabsOverflowLabel);
    }

    [Fact]
    public async Task ReopeningTheSameFileReusesItsTab()
    {
        var harness = CreateHarness();

        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "first.md"));
        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "second.md"));
        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "first.md"));

        Assert.Equal(2, harness.ViewModel.OpenDocuments.Tabs.Count);
        Assert.Equal(TestPaths.At("docs", "first.md"), harness.ViewModel.OpenDocuments.ActiveTab!.Path);
    }

    [Fact]
    public async Task SwitchingTabsRestoresContentWithoutReadingTheFileAgain()
    {
        var harness = CreateHarness();
        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "first.md"));
        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "second.md"));

        var readsBefore = harness.Loader.LoadCount;
        var first = harness.ViewModel.OpenDocuments.Tabs[0];

        await harness.ViewModel.OpenDocuments.ActivateCommand.ExecuteAsync(first);

        Assert.Equal(readsBefore, harness.Loader.LoadCount);
        Assert.Equal(TestPaths.At("docs", "first.md"), harness.ViewModel.CurrentDocumentPath);
        Assert.Equal("first.md — Softmark", harness.ViewModel.WindowTitle);
    }

    [Fact]
    public async Task ScrollOffsetIsRememberedPerTab()
    {
        var harness = CreateHarness();
        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "first.md"));
        harness.ViewModel.ReportScrollOffset(420);

        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "second.md"));
        harness.ViewModel.ReportScrollOffset(80);

        var first = harness.ViewModel.OpenDocuments.Tabs[0];
        await harness.ViewModel.OpenDocuments.ActivateCommand.ExecuteAsync(first);

        Assert.Equal(420, harness.ViewModel.TakePendingScrollOffset());
        Assert.Null(harness.ViewModel.TakePendingScrollOffset());
    }

    [Fact]
    public async Task ClosingTabActivatesNeighbourAndRestoresIt()
    {
        var harness = CreateHarness();
        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "first.md"));
        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "second.md"));

        await harness.ViewModel.CloseActiveTabCommand.ExecuteAsync(null);

        Assert.Single(harness.ViewModel.OpenDocuments.Tabs);
        Assert.Equal(TestPaths.At("docs", "first.md"), harness.ViewModel.CurrentDocumentPath);
        Assert.Equal(ViewState.Viewing, harness.ViewModel.State);
    }

    [Fact]
    public async Task ClosingTheLastTabWithoutFolderReturnsToWelcome()
    {
        var harness = CreateHarness();
        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "first.md"));

        await harness.ViewModel.CloseActiveTabCommand.ExecuteAsync(null);

        Assert.Empty(harness.ViewModel.OpenDocuments.Tabs);
        Assert.False(harness.ViewModel.ShowsTabStrip);
        Assert.True(harness.ViewModel.IsWelcome);
        Assert.False(harness.ViewModel.IsEmptyDocumentSurface);
    }

    [Fact]
    public async Task ClosingTheLastTabInsideFolderShowsEmptySurface()
    {
        var harness = CreateHarness();
        await harness.ViewModel.OpenFolderPathAsync(Root);
        Assert.Single(harness.ViewModel.OpenDocuments.Tabs);

        await harness.ViewModel.CloseActiveTabCommand.ExecuteAsync(null);

        Assert.Empty(harness.ViewModel.OpenDocuments.Tabs);
        Assert.True(harness.ViewModel.IsEmptyDocumentSurface);
        Assert.False(harness.ViewModel.IsWelcome);

        // В папке без вкладок в строке остаётся «+».
        Assert.True(harness.ViewModel.ShowsTabStrip);
    }

    /// <summary>
    /// «+» живёт в полосе вкладок (ADR-0009 Rule 3): на стартовом экране полосы нет,
    /// в папке без открытых вкладок она держит одну «+», с документом — вкладки и «+».
    /// </summary>
    [Fact]
    public async Task NewTabButtonFollowsTheShellState()
    {
        var harness = CreateHarness();

        Assert.True(harness.ViewModel.IsWelcome);
        Assert.False(harness.ViewModel.ShowsTabStrip);
        Assert.Null(harness.ViewModel.TabStripContent);

        var notified = new List<string?>();
        harness.ViewModel.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        await harness.ViewModel.OpenFolderPathAsync(Root);
        await harness.ViewModel.CloseActiveTabCommand.ExecuteAsync(null);

        Assert.Contains(nameof(ShellViewModel.ShowsTabStrip), notified);
        Assert.False(harness.ViewModel.OpenDocuments.HasTabs);
        Assert.True(harness.ViewModel.ShowsTabStrip);
        Assert.Same(harness.ViewModel, harness.ViewModel.TabStripContent);

        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "first.md"));

        Assert.True(harness.ViewModel.OpenDocuments.HasTabs);
        Assert.True(harness.ViewModel.ShowsTabStrip);
    }

    /// <summary>
    /// Закрытая папка без вкладок возвращает стартовый экран — и строка снова пустая.
    /// </summary>
    [Fact]
    public async Task ClosingAnEmptyFolderHidesTheNewTabButton()
    {
        var harness = CreateHarness();
        await harness.ViewModel.OpenFolderPathAsync(Root);
        await harness.ViewModel.CloseActiveTabCommand.ExecuteAsync(null);

        await harness.ViewModel.CloseFolderCommand.ExecuteAsync(null);

        Assert.True(harness.ViewModel.IsWelcome);
        Assert.False(harness.ViewModel.ShowsTabStrip);
        Assert.Null(harness.ViewModel.TabStripContent);
    }

    [Fact]
    public async Task TabsKnowWhetherTheyBelongToTheOpenFolder()
    {
        var harness = CreateHarness();
        await harness.ViewModel.OpenFolderPathAsync(Root);
        await harness.ViewModel.OpenPathAsync(TestPaths.At("outside", "notes.md"));

        var readme = harness.ViewModel.OpenDocuments.Tabs.Single(tab => tab.Title == "README.md");
        var outside = harness.ViewModel.OpenDocuments.Tabs.Single(tab => tab.Title == "notes.md");

        Assert.True(readme.BelongsToWorkspace);
        Assert.False(outside.BelongsToWorkspace);
    }

    /// <summary>
    /// Тултип — полный путь, домашняя папка сокращается до <c>~</c> (ADR-0009 Rule 3).
    /// Раньше у файлов папки был путь от её корня — он не говорил, какая это папка.
    /// </summary>
    [Fact]
    public async Task TabTooltipIsTheFullPathWithHomeAsTilde()
    {
        var harness = CreateHarness(homeDirectory: TestPaths.At("docs"));
        await harness.ViewModel.OpenFolderPathAsync(Root);
        await harness.ViewModel.OpenPathAsync(TestPaths.At("outside", "notes.md"));

        var readme = harness.ViewModel.OpenDocuments.Tabs.Single(tab => tab.Title == "README.md");
        var outside = harness.ViewModel.OpenDocuments.Tabs.Single(tab => tab.Title == "notes.md");

        Assert.Equal($"~{Path.DirectorySeparatorChar}README.md", readme.Tooltip);
        Assert.Equal(TestPaths.At("outside", "notes.md"), outside.Tooltip);
    }

    /// <summary>Папка, чьё имя лишь начинается с имени домашней, домашней не считается.</summary>
    [Fact]
    public async Task TabTooltipDoesNotShortenASiblingThatSharesTheHomePrefix()
    {
        var harness = CreateHarness(homeDirectory: TestPaths.At("doc"));

        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "first.md"));

        Assert.Equal(TestPaths.At("docs", "first.md"), harness.ViewModel.OpenDocuments.ActiveTab!.Tooltip);
    }

    /// <summary>Домашняя папка с разделителем на конце сокращается так же.</summary>
    [Fact]
    public async Task TabTooltipShortensHomeWithATrailingSeparator()
    {
        var harness = CreateHarness(homeDirectory: TestPaths.At("docs") + Path.DirectorySeparatorChar);

        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "first.md"));

        Assert.Equal($"~{Path.DirectorySeparatorChar}first.md", harness.ViewModel.OpenDocuments.ActiveTab!.Tooltip);
    }

    /// <summary>
    /// Вкладка, открытая до папки, после её открытия ведёт себя как открытая из дерева:
    /// раньше дерево не ставило ей точку несохранённого.
    /// </summary>
    [Fact]
    public async Task TabsOpenedBeforeTheFolderJoinItByPath()
    {
        var harness = CreateHarness();
        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "first.md"));
        EditActiveTab(harness, "# first edited");
        await harness.ViewModel.OpenPathAsync(TestPaths.At("outside", "notes.md"));

        await harness.ViewModel.OpenFolderPathAsync(Root);

        var first = harness.ViewModel.OpenDocuments.Tabs.Single(tab => tab.Title == "first.md");
        var outside = harness.ViewModel.OpenDocuments.Tabs.Single(tab => tab.Title == "notes.md");
        Assert.True(first.BelongsToWorkspace);
        Assert.False(outside.BelongsToWorkspace);
        Assert.True(harness.ViewModel.Workspace!.Roots.Single(node => node.Name == "first.md").IsDirty);
    }

    [Fact]
    public async Task CtrlTabWalksTabsInStripOrder()
    {
        var harness = CreateHarness();
        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "first.md"));
        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "second.md"));

        await harness.ViewModel.ActivateNextTabCommand.ExecuteAsync(null);
        Assert.Equal(TestPaths.At("docs", "first.md"), harness.ViewModel.CurrentDocumentPath);

        await harness.ViewModel.ActivatePreviousTabCommand.ExecuteAsync(null);
        Assert.Equal(TestPaths.At("docs", "second.md"), harness.ViewModel.CurrentDocumentPath);
    }

    [Fact]
    public async Task ClosingFolderDropsItsTabsAndKeepsOutsideOnes()
    {
        var harness = CreateHarness();
        await harness.ViewModel.OpenFolderPathAsync(Root);
        await harness.ViewModel.OpenPathAsync(TestPaths.At("outside", "notes.md"));

        await harness.ViewModel.CloseFolderCommand.ExecuteAsync(null);

        Assert.Null(harness.ViewModel.Workspace);
        Assert.Equal(TestPaths.At("outside", "notes.md"), harness.ViewModel.CurrentDocumentPath);
        Assert.Equal(["notes.md"], harness.ViewModel.OpenDocuments.Tabs.Select(tab => tab.Title));
    }

    /// <summary>
    /// Раньше «Закрыть папку» спрашивала только про активную вкладку, а фоновые грязные
    /// вкладки папки закрывались молча. Вкладка вне папки остаётся и не спрашивается.
    /// </summary>
    [Fact]
    public async Task ClosingFolderAsksAboutEveryDirtyTabOfItInTurn()
    {
        var harness = await CreateHarnessWithDirtyFolderTabsAsync();

        await harness.ViewModel.CloseFolderCommand.ExecuteAsync(null);

        Assert.True(harness.ViewModel.IsDirtyPromptOpen);
        Assert.Equal(TestPaths.At("docs", "README.md"), harness.ViewModel.CurrentDocumentPath);

        await harness.ViewModel.ConfirmDirtyDiscardCommand.ExecuteAsync(null);

        Assert.NotNull(harness.ViewModel.Workspace);
        Assert.True(harness.ViewModel.IsDirtyPromptOpen);
        Assert.Equal(TestPaths.At("docs", "first.md"), harness.ViewModel.CurrentDocumentPath);

        await harness.ViewModel.ConfirmDirtyDiscardCommand.ExecuteAsync(null);

        Assert.False(harness.ViewModel.IsDirtyPromptOpen);
        Assert.Null(harness.ViewModel.Workspace);
        var outside = Assert.Single(harness.ViewModel.OpenDocuments.Tabs);
        Assert.Equal("notes.md", outside.Title);
        Assert.True(outside.IsDirty);
    }

    [Fact]
    public async Task CancellingOnAnyFolderTabKeepsTheFolderOpen()
    {
        var harness = await CreateHarnessWithDirtyFolderTabsAsync();

        await harness.ViewModel.CloseFolderCommand.ExecuteAsync(null);
        await harness.ViewModel.ConfirmDirtyDiscardCommand.ExecuteAsync(null);
        harness.ViewModel.CancelDirtyPromptCommand.Execute(null);

        Assert.False(harness.ViewModel.IsDirtyPromptOpen);
        Assert.NotNull(harness.ViewModel.Workspace);
        Assert.Equal(3, harness.ViewModel.OpenDocuments.Tabs.Count);
        Assert.True(harness.ViewModel.OpenDocuments.Tabs.Single(tab => tab.Title == "first.md").IsDirty);
    }

    /// <summary>
    /// Файл из папки открыт и правится до того, как открыли саму папку, а её сессия
    /// помнит этот файл. Раньше восстановление перечитывало его с диска поверх вкладки
    /// и молча сбрасывало несохранённые правки.
    /// </summary>
    [Fact]
    public async Task OpeningFolderKeepsEditsOfATabItsSessionRestores()
    {
        var first = TestPaths.At("docs", "first.md");
        var harness = CreateHarness(new WorkspaceSessionState(Root, [first], first, []));
        await harness.ViewModel.OpenPathAsync(first);
        EditActiveTab(harness, "# first edited");

        await harness.ViewModel.OpenFolderPathAsync(Root);

        Assert.NotNull(harness.ViewModel.Workspace);
        var tab = Assert.Single(harness.ViewModel.OpenDocuments.Tabs);
        Assert.Same(tab, harness.ViewModel.OpenDocuments.ActiveTab);
        Assert.True(tab.IsDirty);
        Assert.True(harness.ViewModel.IsDirty);
        Assert.True(harness.ViewModel.IsEditMode);
        Assert.Equal("# first edited", harness.ViewModel.EditorSession!.SourceText);
        Assert.Equal(1, harness.Loader.LoadCount);
    }

    /// <summary>
    /// То же, когда активной по сессии становится другая вкладка: правки остаются
    /// в своей вкладке и возвращаются при переключении на неё.
    /// </summary>
    [Fact]
    public async Task OpeningFolderKeepsEditsOfATabThatEndsUpInTheBackground()
    {
        var readme = TestPaths.At("docs", "README.md");
        var first = TestPaths.At("docs", "first.md");
        var harness = CreateHarness(new WorkspaceSessionState(Root, [readme, first], readme, []));
        await harness.ViewModel.OpenPathAsync(first);
        EditActiveTab(harness, "# first edited");

        await harness.ViewModel.OpenFolderPathAsync(Root);

        Assert.Equal(["first.md", "README.md"], harness.ViewModel.OpenDocuments.Tabs.Select(tab => tab.Title));
        Assert.Equal(readme, harness.ViewModel.CurrentDocumentPath);

        var firstTab = harness.ViewModel.OpenDocuments.FindByPath(first)!;
        Assert.True(firstTab.IsDirty);

        await harness.ViewModel.OpenDocuments.ActivateCommand.ExecuteAsync(firstTab);

        Assert.True(harness.ViewModel.IsDirty);
        Assert.Equal("# first edited", harness.ViewModel.EditorSession!.SourceText);
    }

    /// <summary>
    /// Черновик ⌘N — такая же вкладка со своими правками (ADR-0009 Rule 3). Раньше его
    /// сессия приписывалась вкладке, активной до ⌘N, а сам черновик оставался без неё:
    /// открытие другого файла выбрасывало набранный текст. ⌘O поверх грязного черновика
    /// пока спрашивает о правках, поэтому файл приходит так, как из Finder.
    /// </summary>
    [Fact]
    public async Task DraftKeepsItsTextWhenAnotherDocumentOpensOverIt()
    {
        var harness = CreateHarness();
        await harness.ViewModel.CreateNewDocumentCommand.ExecuteAsync(null);
        var draft = harness.ViewModel.OpenDocuments.ActiveTab!;
        harness.ViewModel.EditorSession!.SourceText = "# draft";

        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "first.md"));

        Assert.True(draft.IsDirty);

        await harness.ViewModel.OpenDocuments.ActivateCommand.ExecuteAsync(draft);

        Assert.True(harness.ViewModel.IsEditMode);
        Assert.True(harness.ViewModel.IsDirty);
        Assert.Equal("# draft", harness.ViewModel.EditorSession!.SourceText);
    }

    /// <summary>Пустой черновик после ⌘O тоже возвращается редактором, а не пустым экраном.</summary>
    [Fact]
    public async Task DraftStaysInEditModeAfterOpeningAFileOverIt()
    {
        var harness = CreateHarness();
        harness.FilePicker.OpenPath = TestPaths.At("docs", "first.md");
        await harness.ViewModel.CreateNewDocumentCommand.ExecuteAsync(null);
        var draft = harness.ViewModel.OpenDocuments.ActiveTab!;
        var draftSession = harness.ViewModel.EditorSession;

        await harness.ViewModel.OpenFileCommand.ExecuteAsync(null);
        Assert.Equal(TestPaths.At("docs", "first.md"), harness.ViewModel.CurrentDocumentPath);

        await harness.ViewModel.OpenDocuments.ActivateCommand.ExecuteAsync(draft);

        Assert.True(harness.ViewModel.IsEditMode);
        Assert.Same(draftSession, harness.ViewModel.EditorSession);
        Assert.Equal("Untitled.md", harness.ViewModel.FileName);
    }

    /// <summary>
    /// ⌘N из правки документа не забирает у его вкладки сессию: раньше вкладка получала
    /// сессию черновика и при возврате показывала пустой редактор вместо своего текста.
    /// Правки здесь набраны после ⌘N — важно, что они попадают в сессию своей вкладки,
    /// а не черновика.
    /// </summary>
    [Fact]
    public async Task NewDocumentLeavesThePreviousTabItsOwnSession()
    {
        var harness = CreateHarness();
        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "first.md"));
        harness.ViewModel.ToggleEditModeCommand.Execute(null);
        var first = harness.ViewModel.OpenDocuments.ActiveTab!;
        var firstSession = harness.ViewModel.EditorSession!;

        await harness.ViewModel.CreateNewDocumentCommand.ExecuteAsync(null);
        var draft = harness.ViewModel.OpenDocuments.ActiveTab!;
        var draftSession = harness.ViewModel.EditorSession!;

        Assert.NotSame(first, draft);
        Assert.Same(firstSession, first.EditorSession);
        Assert.True(first.IsEditMode);
        Assert.Same(draftSession, draft.EditorSession);
        Assert.True(draft.IsEditMode);

        await harness.ViewModel.OpenDocuments.ActivateCommand.ExecuteAsync(first);
        harness.ViewModel.EditorSession!.SourceText = "# first edited";
        await harness.ViewModel.OpenDocuments.ActivateCommand.ExecuteAsync(draft);
        await harness.ViewModel.OpenDocuments.ActivateCommand.ExecuteAsync(first);

        Assert.Same(firstSession, harness.ViewModel.EditorSession);
        Assert.True(harness.ViewModel.IsEditMode);
        Assert.Equal("# first edited", harness.ViewModel.EditorSession.SourceText);
        Assert.Equal(TestPaths.At("docs", "first.md"), harness.ViewModel.CurrentDocumentPath);
        Assert.Equal(string.Empty, draftSession.SourceText);
    }

    /// <summary>
    /// Окно ищет несохранённое по сессиям вкладок. Раньше у черновика её не было,
    /// и окно с набранным в нём текстом закрывалось без вопроса.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosingWindowAsksAboutADirtyDraft(bool draftInBackground)
    {
        var harness = CreateHarness();
        var closeRequests = 0;
        harness.ViewModel.CloseRequested += (_, _) => closeRequests++;

        await harness.ViewModel.CreateNewDocumentCommand.ExecuteAsync(null);
        var draft = harness.ViewModel.OpenDocuments.ActiveTab!;
        harness.ViewModel.EditorSession!.SourceText = "# draft";

        if (draftInBackground)
        {
            await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "first.md"));
        }

        Assert.True(harness.ViewModel.TryQueueCloseRequest());
        Assert.True(harness.ViewModel.IsDirtyPromptOpen);
        Assert.Same(draft, harness.ViewModel.OpenDocuments.ActiveTab);
        Assert.Equal("# draft", harness.ViewModel.EditorSession!.SourceText);

        await harness.ViewModel.ConfirmDirtyDiscardCommand.ExecuteAsync(null);

        Assert.Equal(1, closeRequests);
    }

    /// <summary>
    /// Неудачное открытие получает активную вкладку с именем файла и экраном ошибки
    /// (A-LoadError), а прежняя вкладка остаётся на месте. Справа в строке — только ⋯.
    /// </summary>
    [Fact]
    public async Task FailedOpenGetsAnActiveErrorTabNamedAfterTheFile()
    {
        var harness = CreateHarness();
        var missing = TestPaths.At("docs", "missing.md");
        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "first.md"));

        await harness.ViewModel.OpenPathAsync(missing);

        Assert.Equal(["first.md", "missing.md"], harness.ViewModel.OpenDocuments.Tabs.Select(tab => tab.Title));
        var errorTab = harness.ViewModel.OpenDocuments.ActiveTab!;
        Assert.Equal(missing, errorTab.Path);
        Assert.True(errorTab.IsLoadError);
        Assert.True(harness.ViewModel.IsError);
        Assert.Equal(LoadErrorKind.NotFound, harness.ViewModel.ErrorKind);
        Assert.Equal("Couldn't find that file", harness.ViewModel.ErrorTitle);
        Assert.Equal("It may have been moved, renamed, or deleted.", harness.ViewModel.ErrorDescription);
        Assert.Equal(missing, harness.ViewModel.ErrorPath);
        Assert.True(harness.ViewModel.ShowsLoadErrorRetry);

        Assert.False(harness.ViewModel.ShowsFindToggle);
        Assert.False(harness.ViewModel.ShowsReadingSettingsToggle);
        Assert.False(harness.ViewModel.ShowsEditToggle);
        Assert.False(harness.ViewModel.ShowsDoneButton);
        Assert.False(harness.ViewModel.ShowsUnsavedIndicator);
    }

    /// <summary>
    /// «Повторить» перечитывает путь своей вкладки, а не документ, открытый до неё, и при
    /// успехе показывает документ в той же вкладке.
    /// </summary>
    [Fact]
    public async Task RetryRereadsTheErrorTabsPathAndShowsTheDocumentInIt()
    {
        var harness = CreateHarness();
        var missing = TestPaths.At("docs", "missing.md");
        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "first.md"));
        await harness.ViewModel.OpenPathAsync(missing);
        var errorTab = harness.ViewModel.OpenDocuments.ActiveTab!;

        harness.Loader.Sources[missing] = new MarkdownSource(missing, "missing.md", "# found");
        await harness.ViewModel.RetryLoadCommand.ExecuteAsync(null);

        Assert.Equal(2, harness.ViewModel.OpenDocuments.Tabs.Count);
        Assert.Same(errorTab, harness.ViewModel.OpenDocuments.ActiveTab);
        Assert.False(errorTab.IsLoadError);
        Assert.True(harness.ViewModel.IsViewer);
        Assert.Equal("# found", harness.ViewModel.Document!.Content);
        Assert.Equal(missing, harness.ViewModel.CurrentDocumentPath);
        Assert.Equal(LoadErrorKind.None, harness.ViewModel.ErrorKind);
    }

    /// <summary>Повтор, который снова не удался, оставляет ту же вкладку и ту же точку возврата.</summary>
    [Fact]
    public async Task RetryThatFailsAgainKeepsTheSameErrorTab()
    {
        var harness = CreateHarness();
        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "first.md"));
        var first = harness.ViewModel.OpenDocuments.ActiveTab!;
        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "missing.md"));
        var errorTab = harness.ViewModel.OpenDocuments.ActiveTab!;

        await harness.ViewModel.RetryLoadCommand.ExecuteAsync(null);

        Assert.Equal(2, harness.ViewModel.OpenDocuments.Tabs.Count);
        Assert.Same(errorTab, harness.ViewModel.OpenDocuments.ActiveTab);
        Assert.True(errorTab.IsLoadError);
        Assert.Same(first, errorTab.ReturnTab);
        Assert.True(harness.ViewModel.IsError);
    }

    /// <summary>
    /// Esc и ✕ закрывают вкладку ошибки и возвращают к вкладке, из которой открывали, —
    /// а не к соседке по полосе.
    /// </summary>
    [Theory]
    [InlineData("escape")]
    [InlineData("close")]
    public async Task ClosingTheErrorTabReturnsToThePreviousTab(string how)
    {
        var harness = CreateHarness();
        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "first.md"));
        var first = harness.ViewModel.OpenDocuments.ActiveTab!;
        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "second.md"));
        await harness.ViewModel.OpenDocuments.ActivateCommand.ExecuteAsync(first);
        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "missing.md"));
        var errorTab = harness.ViewModel.OpenDocuments.ActiveTab!;

        if (how == "escape")
        {
            await harness.ViewModel.ClearErrorCommand.ExecuteAsync(null);
        }
        else
        {
            await harness.ViewModel.OpenDocuments.CloseCommand.ExecuteAsync(errorTab);
        }

        Assert.Equal(["first.md", "second.md"], harness.ViewModel.OpenDocuments.Tabs.Select(tab => tab.Title));
        Assert.Same(first, harness.ViewModel.OpenDocuments.ActiveTab);
        Assert.True(harness.ViewModel.IsViewer);
        Assert.Equal("# first", harness.ViewModel.Document!.Content);
    }

    /// <summary>Единственная вкладка ошибки закрывается на стартовый экран, в папке — на «Документ не выбран».</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosingTheOnlyErrorTabReturnsToTheEmptyScreen(bool inFolder)
    {
        var harness = CreateHarness();
        if (inFolder)
        {
            await harness.ViewModel.OpenFolderPathAsync(Root);
            await harness.ViewModel.OpenDocuments.CloseCommand.ExecuteAsync(harness.ViewModel.OpenDocuments.ActiveTab);
        }

        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "missing.md"));
        Assert.True(harness.ViewModel.IsError);

        await harness.ViewModel.ClearErrorCommand.ExecuteAsync(null);

        Assert.Empty(harness.ViewModel.OpenDocuments.Tabs);
        Assert.False(harness.ViewModel.IsError);
        Assert.Equal(!inFolder, harness.ViewModel.IsWelcome);
        Assert.Equal(inFolder, harness.ViewModel.IsEmptyDocumentSurface);
    }

    /// <summary>Файл не Markdown: своё пояснение и нет «Повторить» — повтор ничего не изменит.</summary>
    [Fact]
    public async Task UnsupportedFileShowsSupportedTypesAndNoRetry()
    {
        var harness = CreateHarness();

        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "report.pdf"));

        Assert.Equal("report.pdf", harness.ViewModel.OpenDocuments.ActiveTab!.Title);
        Assert.Equal(LoadErrorKind.UnsupportedType, harness.ViewModel.ErrorKind);
        Assert.Equal("This isn't Markdown", harness.ViewModel.ErrorTitle);
        Assert.Equal("Softmark opens .md, .markdown and .txt files.", harness.ViewModel.ErrorDescription);
        Assert.False(harness.ViewModel.ShowsLoadErrorRetry);
    }

    [Fact]
    public async Task AccessDeniedAndReadFailureExplainThemselves()
    {
        var harness = CreateHarness();

        harness.Loader.NextException = new UnauthorizedAccessException();
        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "secret.md"));

        Assert.Equal(LoadErrorKind.AccessDenied, harness.ViewModel.ErrorKind);
        Assert.Equal("Access denied", harness.ViewModel.ErrorTitle);
        Assert.Equal("Softmark doesn't have permission to read this file.", harness.ViewModel.ErrorDescription);
        Assert.True(harness.ViewModel.ShowsLoadErrorRetry);

        harness.Loader.NextException = new IOException("The disk is busy.");
        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "busy.md"));

        Assert.Equal(LoadErrorKind.ReadFailure, harness.ViewModel.ErrorKind);
        Assert.Equal("Couldn't read the file", harness.ViewModel.ErrorTitle);
        Assert.Equal("The disk is busy.", harness.ViewModel.ErrorDescription);
        Assert.True(harness.ViewModel.ShowsLoadErrorRetry);
        Assert.Equal(["secret.md", "busy.md"], harness.ViewModel.OpenDocuments.Tabs.Select(tab => tab.Title));
    }

    /// <summary>
    /// Путь на экране ошибки копируют: на macOS и Linux он сокращается до ~, как в тултипе
    /// вкладки, а на Windows остаётся полным — ~ там ни Проводник, ни cmd не поймут.
    /// </summary>
    [Theory]
    [InlineData("macOS", true)]
    [InlineData("Linux", true)]
    [InlineData("Windows", false)]
    public async Task ErrorPathIsShortenedOnlyWhereTildeWorks(string platformName, bool shortened)
    {
        var harness = CreateHarness(homeDirectory: TestPaths.At("docs"), platformName: platformName);
        var missing = TestPaths.At("docs", "missing.md");

        await harness.ViewModel.OpenPathAsync(missing);

        Assert.Equal(
            shortened ? "~" + Path.DirectorySeparatorChar + "missing.md" : missing,
            harness.ViewModel.ErrorPath);
    }

    /// <summary>
    /// Повторное открытие уже открытого файла не удалось: ошибка встаёт поверх его вкладки —
    /// она становится активной, — и «Повторить» перечитывает её, а не вкладку, с которой
    /// открывали. Раньше ошибка вставала над чужой вкладкой, и повтор перечитывал её.
    /// </summary>
    [Fact]
    public async Task FailedReopenOfABackgroundTabShowsTheErrorOverThatTab()
    {
        var harness = CreateHarness();
        var first = TestPaths.At("docs", "first.md");
        await harness.ViewModel.OpenPathAsync(first);
        var firstTab = harness.ViewModel.OpenDocuments.ActiveTab!;
        harness.ViewModel.ToggleEditModeCommand.Execute(null);
        await harness.ViewModel.ToggleEditModeCommand.ExecuteAsync(null);
        Assert.NotNull(firstTab.EditorSession);
        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "second.md"));

        harness.Loader.NextException = new UnauthorizedAccessException();
        await harness.ViewModel.OpenPathAsync(first);

        Assert.Equal(2, harness.ViewModel.OpenDocuments.Tabs.Count);
        Assert.Same(firstTab, harness.ViewModel.OpenDocuments.ActiveTab);
        Assert.False(firstTab.IsLoadError);
        Assert.True(harness.ViewModel.IsError);
        Assert.Equal(LoadErrorKind.AccessDenied, harness.ViewModel.ErrorKind);

        harness.Loader.Sources[first] = new MarkdownSource(first, "first.md", "# first again");
        await harness.ViewModel.RetryLoadCommand.ExecuteAsync(null);

        Assert.True(harness.ViewModel.IsViewer);
        Assert.Same(firstTab, harness.ViewModel.OpenDocuments.ActiveTab);
        Assert.Equal(first, harness.ViewModel.CurrentDocumentPath);
        Assert.Equal("# first again", harness.ViewModel.Document!.Content);
    }

    /// <summary>
    /// Папка, открытая поверх вкладки ошибки, открывает свой README.md рядом с ней: вкладка
    /// ошибки — не документ. Раньше README не открывался, а экран ошибки оставался без текстов.
    /// </summary>
    [Fact]
    public async Task OpeningAFolderOverAnErrorTabOpensItsReadme()
    {
        var harness = CreateHarness();
        await harness.ViewModel.OpenPathAsync(TestPaths.At("outside", "missing.md"));

        await harness.ViewModel.OpenFolderPathAsync(Root);

        Assert.Equal(["missing.md", "README.md"], harness.ViewModel.OpenDocuments.Tabs.Select(tab => tab.Title));
        Assert.True(harness.ViewModel.IsViewer);
        Assert.Equal("# readme", harness.ViewModel.Document!.Content);

        await harness.ViewModel.OpenDocuments.ActivateCommand.ExecuteAsync(harness.ViewModel.OpenDocuments.Tabs[0]);

        Assert.True(harness.ViewModel.IsError);
        Assert.Equal("Couldn't find that file", harness.ViewModel.ErrorTitle);
    }

    /// <summary>
    /// Ошибка папки поверх вкладки ошибки: Esc снимает ошибку папки и возвращает экран
    /// вкладки, а не закрывает вкладку файла.
    /// </summary>
    [Fact]
    public async Task EscapeDismissesAFolderErrorOverAnErrorTab()
    {
        var harness = CreateHarness();
        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "missing.md"));

        await harness.ViewModel.OpenFolderPathAsync(TestPaths.At("gone"));
        Assert.Equal(LoadErrorKind.Folder, harness.ViewModel.ErrorKind);

        await harness.ViewModel.ClearErrorCommand.ExecuteAsync(null);

        Assert.Single(harness.ViewModel.OpenDocuments.Tabs);
        Assert.True(harness.ViewModel.IsError);
        Assert.Equal(LoadErrorKind.NotFound, harness.ViewModel.ErrorKind);
        Assert.Equal("Couldn't find that file", harness.ViewModel.ErrorTitle);
    }

    /// <summary>Вкладка ошибки — след попытки, а не документ: в сессию папки она не пишется.</summary>
    [Fact]
    public async Task ErrorTabIsNotStoredInTheSession()
    {
        var harness = CreateHarness();
        await harness.ViewModel.OpenFolderPathAsync(Root);
        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "first.md"));

        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "missing.md"));

        var session = await WaitForSessionAsync(
            harness.Settings,
            state => state.OpenDocumentPaths.Contains(TestPaths.At("docs", "first.md")));

        Assert.Equal([TestPaths.At("docs", "README.md"), TestPaths.At("docs", "first.md")], session.OpenDocumentPaths);
        Assert.DoesNotContain(TestPaths.At("docs", "missing.md"), session.OpenDocumentPaths);
        Assert.Null(session.ActiveDocumentPath);
    }

    /// <summary>Вторая строка слоя перетаскивания говорит, что случится при отпускании (A-Drop).</summary>
    [Fact]
    public async Task DropTargetDescribesWhereTheItemWillOpen()
    {
        var harness = CreateHarness();
        var notes = TestPaths.At("notes");

        Assert.Equal("release-notes.md will open in a new tab", harness.ViewModel.DescribeDropTarget(TestPaths.At("docs", "release-notes.md"), isDirectory: false));
        Assert.Equal("Folder notes will open in this window", harness.ViewModel.DescribeDropTarget(notes, isDirectory: true));

        await harness.ViewModel.OpenFolderPathAsync(Root);

        Assert.Equal("Folder notes will open in a new window", harness.ViewModel.DescribeDropTarget(notes, isDirectory: true));

        harness.Launcher.OpenFolders.Add(notes);

        Assert.Equal("Folder notes is already open in another window", harness.ViewModel.DescribeDropTarget(notes, isDirectory: true));
    }

    /// <summary>Список файлов недоступен до отпускания: слой есть, а второй строки нет.</summary>
    [Fact]
    public void DropTargetWithoutAKnownItemShowsNoDetails()
    {
        var harness = CreateHarness();

        harness.ViewModel.ShowDropTarget(TestPaths.At("notes"), isDirectory: true);
        Assert.True(harness.ViewModel.IsDragHovering);
        Assert.True(harness.ViewModel.IsDropTargetFolder);
        Assert.True(harness.ViewModel.HasDropTargetDetails);

        harness.ViewModel.HideDropTarget();
        harness.ViewModel.ShowDropTarget(null, isDirectory: false);

        Assert.True(harness.ViewModel.IsDragHovering);
        Assert.False(harness.ViewModel.IsDropTargetFolder);
        Assert.False(harness.ViewModel.HasDropTargetDetails);

        harness.ViewModel.HideDropTarget();

        Assert.False(harness.ViewModel.IsDragHovering);
    }

    private static async Task<WorkspaceSessionState> WaitForSessionAsync(
        InMemorySettingsStore settings,
        Func<WorkspaceSessionState, bool> predicate)
    {
        for (var attempt = 0; attempt < 60 && !predicate(settings.Session); attempt++)
        {
            await Task.Delay(25);
        }

        return settings.Session;
    }

    /// <summary>Две грязные вкладки папки и одна грязная вкладка вне её.</summary>
    private static async Task<TabsTestHarness> CreateHarnessWithDirtyFolderTabsAsync()
    {
        var harness = CreateHarness();
        await harness.ViewModel.OpenFolderPathAsync(Root);
        EditActiveTab(harness, "# readme edited");

        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "first.md"));
        EditActiveTab(harness, "# first edited");

        await harness.ViewModel.OpenPathAsync(TestPaths.At("outside", "notes.md"));
        EditActiveTab(harness, "# notes edited");

        return harness;
    }

    private static void EditActiveTab(TabsTestHarness harness, string text)
    {
        harness.ViewModel.ToggleEditModeCommand.Execute(null);
        harness.ViewModel.EditorSession!.SourceText = text;
    }

    private static TabsTestHarness CreateHarness(
        WorkspaceSessionState? session = null,
        string? homeDirectory = null,
        string platformName = "Windows")
    {
        var platform = homeDirectory is null
            ? new FakePlatformServices { PlatformName = platformName }
            : new FakePlatformServices { HomeDirectory = homeDirectory, PlatformName = platformName };

        var fileSystem = new FakeWorkspaceFileSystem();
        fileSystem.AddDirectory(
            Root,
            WorkspaceEntry.ForFile(TestPaths.At("docs", "README.md"), "README.md"),
            WorkspaceEntry.ForFile(TestPaths.At("docs", "first.md"), "first.md"));

        var loader = new CountingDocumentLoader();
        loader.Sources[TestPaths.At("docs", "README.md")] = new MarkdownSource(TestPaths.At("docs", "README.md"), "README.md", "# readme");
        loader.Sources[TestPaths.At("docs", "first.md")] = new MarkdownSource(TestPaths.At("docs", "first.md"), "first.md", "# first");
        loader.Sources[TestPaths.At("docs", "second.md")] = new MarkdownSource(TestPaths.At("docs", "second.md"), "second.md", "# second");
        loader.Sources[TestPaths.At("outside", "notes.md")] = new MarkdownSource(TestPaths.At("outside", "notes.md"), "notes.md", "# notes");

        var filePicker = new StubFilePicker();
        var launcher = new RecordingWindowLauncher();
        var settings = new InMemorySettingsStore { Session = session ?? WorkspaceSessionState.Empty };

        var viewModel = new ShellViewModel(
            new OpenDocumentUseCase(loader),
            new SaveDocumentUseCase(new RecordingDocumentSaver()),
            filePicker,
            new StubCommandLineActivation(),
            new LocalizationService(AppLanguage.English),
            settings,
            new RecordingThemeService(),
            new RecordingStartupMetrics(),
            new RenderMarkdownDocumentUseCase(new TestMarkdownRenderer(), new FakeDiagramRenderService()),
            TestUpdates.CreateViewModel(),
            new OpenFolderUseCase(fileSystem),
            new ExpandFolderNodeUseCase(fileSystem),
            new SearchWorkspaceFilesUseCase(fileSystem),
            new WorkspaceFileOperationsUseCase(fileSystem, platform),
            platform,
            static () => new FakeWorkspaceWatcher(),
            launcher,
            fileExists: path => fileSystem.Exists(path));

        return new TabsTestHarness(loader, filePicker, launcher, settings, viewModel);
    }

    private sealed record TabsTestHarness(
        CountingDocumentLoader Loader,
        StubFilePicker FilePicker,
        RecordingWindowLauncher Launcher,
        InMemorySettingsStore Settings,
        ShellViewModel ViewModel);
}
