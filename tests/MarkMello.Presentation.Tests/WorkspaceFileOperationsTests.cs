using MarkMello.Application.Abstractions;
using MarkMello.Application.UseCases;
using MarkMello.Domain;
using MarkMello.Domain.Workspace;
using MarkMello.Presentation.Localization;
using MarkMello.Presentation.ViewModels;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Файловые операции дерева: создание и переименование инлайн, дублирование,
/// удаление через корзину и реакция открытых вкладок.
/// </summary>
public sealed class WorkspaceFileOperationsTests
{
    private static readonly string Root = TestPaths.At("docs");

    [Fact]
    public async Task CreatingAFileOpensItAsATab()
    {
        var harness = await CreateAsync();

        harness.Workspace.StartNewFileCommand.Execute(null);
        harness.Workspace.EditName = "meeting";
        await harness.Workspace.CommitEditCommand.ExecuteAsync(null);

        Assert.Contains(harness.Workspace.Roots, node => node.Name == "meeting.md");
        Assert.Equal(TestPaths.At("docs", "meeting.md"), harness.ViewModel.CurrentDocumentPath);
        Assert.False(harness.Workspace.IsEditingName);
    }

    /// <summary>
    /// Черновая строка стоит в дереве на месте будущего файла и исчезает вместе с вводом,
    /// а созданный файл встаёт на её место.
    /// </summary>
    [Fact]
    public async Task DraftRowStandsInTheTreeUntilTheNameIsCommitted()
    {
        var harness = await CreateAsync();

        harness.Workspace.StartNewFileCommand.Execute(null);

        var draft = Assert.Single(harness.Workspace.Roots, static node => node.IsDraft);
        Assert.True(draft.IsEditing);
        Assert.DoesNotContain(harness.Workspace.Roots, static node => node.Name == "meeting.md");

        harness.Workspace.EditName = "meeting";
        await harness.Workspace.CommitEditCommand.ExecuteAsync(null);

        Assert.DoesNotContain(harness.Workspace.Roots, static node => node.IsDraft);
        Assert.Contains(harness.Workspace.Roots, static node => node.Name == "meeting.md");
    }

    /// <summary>Переименование правит строку на её месте, а не отдельной панелью над деревом.</summary>
    [Fact]
    public async Task RenameEditsTheRowItself()
    {
        var harness = await CreateAsync();
        var node = harness.Workspace.Roots.Single(static row => row.Name == "first.md");

        harness.Workspace.StartRenameCommand.Execute(node);

        Assert.True(node.IsEditing);
        Assert.Equal("first.md", harness.Workspace.EditName);
        Assert.DoesNotContain(harness.Workspace.Roots, static row => row.IsDraft);

        harness.Workspace.CancelEditCommand.Execute(null);

        Assert.False(node.IsEditing);
    }

    [Fact]
    public async Task CreatingAFolderDoesNotOpenAnything()
    {
        var harness = await CreateAsync();

        harness.Workspace.StartNewFolderCommand.Execute(null);
        harness.Workspace.EditName = "drafts";
        await harness.Workspace.CommitEditCommand.ExecuteAsync(null);

        Assert.Contains(harness.Workspace.Roots, node => node is { Name: "drafts", IsDirectory: true });
        Assert.Null(harness.ViewModel.CurrentDocumentPath);
    }

    [Fact]
    public async Task TakenNameKeepsTheInputOpenWithAnError()
    {
        var harness = await CreateAsync();

        harness.Workspace.StartNewFileCommand.Execute(null);
        harness.Workspace.EditName = "first.md";
        await harness.Workspace.CommitEditCommand.ExecuteAsync(null);

        Assert.True(harness.Workspace.IsEditingName);
        Assert.True(harness.Workspace.HasEditError);
        Assert.Equal("A file with this name already exists", harness.Workspace.EditError);
    }

    [Fact]
    public async Task InvalidCharactersAreReportedUnderTheInput()
    {
        var harness = await CreateAsync();

        harness.Workspace.StartNewFileCommand.Execute(null);
        harness.Workspace.EditName = "a/b.md";
        await harness.Workspace.CommitEditCommand.ExecuteAsync(null);

        Assert.True(harness.Workspace.IsEditingName);
        Assert.Contains("aren't allowed", harness.Workspace.EditError);
    }

    [Fact]
    public async Task EscapeCancelsTheInput()
    {
        var harness = await CreateAsync();

        harness.Workspace.StartNewFileCommand.Execute(null);
        harness.Workspace.EditName = "draft";
        harness.Workspace.CancelEditCommand.Execute(null);

        Assert.False(harness.Workspace.IsEditingName);
        Assert.DoesNotContain(harness.Workspace.Roots, node => node.Name == "draft.md");
    }

    [Fact]
    public async Task RenamingFollowsTheOpenTab()
    {
        var harness = await CreateAsync();
        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "first.md"));

        var node = harness.Workspace.Roots.Single(candidate => candidate.Name == "first.md");
        harness.Workspace.StartRenameCommand.Execute(node);
        harness.Workspace.EditName = "renamed.md";
        await harness.Workspace.CommitEditCommand.ExecuteAsync(null);

        var tab = Assert.Single(harness.ViewModel.OpenDocuments.Tabs);
        Assert.Equal("renamed.md", tab.Title);
        Assert.Equal(TestPaths.At("docs", "renamed.md"), tab.Path);
        Assert.Equal(TestPaths.At("docs", "renamed.md"), harness.ViewModel.CurrentDocumentPath);
    }

    [Fact]
    public async Task DuplicatingPicksAFreeNameAndDoesNotOpenIt()
    {
        var harness = await CreateAsync();
        var node = harness.Workspace.Roots.Single(candidate => candidate.Name == "first.md");

        await harness.Workspace.DuplicateCommand.ExecuteAsync(node);

        Assert.Contains(harness.Workspace.Roots, candidate => candidate.Name == "first copy.md");
        Assert.Null(harness.ViewModel.CurrentDocumentPath);
    }

    [Fact]
    public async Task DeletingAsksFirstAndThenClosesTheTab()
    {
        var harness = await CreateAsync();
        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "first.md"));
        var node = harness.Workspace.Roots.Single(candidate => candidate.Name == "first.md");

        await harness.Workspace.RequestDeleteCommand.ExecuteAsync(node);

        Assert.True(harness.ViewModel.IsDeletePromptOpen);
        Assert.Equal("Delete \"first.md\"?", harness.ViewModel.DeletePromptTitle);
        Assert.NotEmpty(harness.ViewModel.OpenDocuments.Tabs);

        await harness.ViewModel.ConfirmDeleteCommand.ExecuteAsync(null);

        Assert.False(harness.ViewModel.IsDeletePromptOpen);
        Assert.Equal([TestPaths.At("docs", "first.md")], harness.Platform.TrashedPaths);
        Assert.Empty(harness.ViewModel.OpenDocuments.Tabs);
        Assert.DoesNotContain(harness.Workspace.Roots, candidate => candidate.Name == "first.md");
    }

    [Fact]
    public async Task CancellingDeleteChangesNothing()
    {
        var harness = await CreateAsync();
        var node = harness.Workspace.Roots.Single(candidate => candidate.Name == "first.md");

        await harness.Workspace.RequestDeleteCommand.ExecuteAsync(node);
        harness.ViewModel.CancelDeleteCommand.Execute(null);

        Assert.False(harness.ViewModel.IsDeletePromptOpen);
        Assert.Empty(harness.Platform.TrashedPaths);
        Assert.Contains(harness.Workspace.Roots, candidate => candidate.Name == "first.md");
    }

    /// <summary>
    /// Esc закрывает диалог удаления, как «Отмена». Раньше Esc о нём не знал: карточка
    /// оставалась на экране, пока не нажмёшь кнопку.
    /// </summary>
    [Fact]
    public async Task EscapeClosesTheDeletePrompt()
    {
        var harness = await CreateAsync();
        var node = harness.Workspace.Roots.Single(candidate => candidate.Name == "first.md");

        await harness.Workspace.RequestDeleteCommand.ExecuteAsync(node);
        harness.ViewModel.ClearErrorCommand.Execute(null);

        Assert.False(harness.ViewModel.IsDeletePromptOpen);
        Assert.Null(harness.ViewModel.DeletePromptContent);
        Assert.False(node.IsPendingDelete);
        Assert.Empty(harness.Platform.TrashedPaths);
        Assert.Contains(harness.Workspace.Roots, candidate => candidate.Name == "first.md");
    }

    /// <summary>
    /// Контекстное меню к моменту вопроса уже закрылось, поэтому удаляемая строка держит
    /// подсветку, пока диалог открыт, — и отпускает её при любом ответе.
    /// </summary>
    [Fact]
    public async Task RowStaysHighlightedWhileTheDeletePromptIsOpen()
    {
        var harness = await CreateAsync();
        var node = harness.Workspace.Roots.Single(candidate => candidate.Name == "first.md");
        var folder = harness.Workspace.Roots.Single(candidate => candidate.Name == "adr");

        await harness.Workspace.RequestDeleteCommand.ExecuteAsync(node);

        Assert.True(node.IsPendingDelete);
        Assert.False(folder.IsPendingDelete);

        harness.ViewModel.CancelDeleteCommand.Execute(null);

        Assert.False(node.IsPendingDelete);

        await harness.Workspace.RequestDeleteCommand.ExecuteAsync(folder);
        await harness.ViewModel.ConfirmDeleteCommand.ExecuteAsync(null);

        Assert.False(folder.IsPendingDelete);
        Assert.False(harness.ViewModel.IsDeletePromptOpen);
    }

    /// <summary>Сбой корзины — та же карточка с одной кнопкой «Закрыть» вместо «Отмены».</summary>
    [Fact]
    public async Task FailedDeleteLeavesOnlyAClosingButton()
    {
        var harness = await CreateAsync();
        harness.Platform.TrashResult = TrashResult.Failed;
        var node = harness.Workspace.Roots.Single(candidate => candidate.Name == "first.md");

        await harness.Workspace.RequestDeleteCommand.ExecuteAsync(node);

        Assert.Equal("Cancel", harness.ViewModel.DeleteCancelLabel);

        await harness.ViewModel.ConfirmDeleteCommand.ExecuteAsync(null);

        Assert.True(harness.ViewModel.IsDeleteErrorPrompt);
        Assert.Equal("Couldn't delete \"first.md\"", harness.ViewModel.DeletePromptTitle);
        Assert.Equal("Close", harness.ViewModel.DeleteCancelLabel);

        harness.ViewModel.CancelDeleteCommand.Execute(null);

        Assert.False(harness.ViewModel.IsDeletePromptOpen);
        Assert.False(node.IsPendingDelete);
        Assert.Equal("Cancel", harness.ViewModel.DeleteCancelLabel);
    }

    [Fact]
    public async Task NonEmptyFolderMentionsHowManyItemsGo()
    {
        var harness = await CreateAsync();
        var node = harness.Workspace.Roots.Single(candidate => candidate.Name == "adr");

        await harness.Workspace.RequestDeleteCommand.ExecuteAsync(node);

        Assert.Equal("Delete folder \"adr\" and everything in it?", harness.ViewModel.DeletePromptTitle);
        Assert.Contains("has 1 items", harness.ViewModel.DeletePromptMessage);
    }

    [Fact]
    public async Task WithoutTrashTheDialogAsksAgainAboutPermanentDeletion()
    {
        var harness = await CreateAsync();
        harness.Platform.TrashResult = TrashResult.Unsupported;
        var node = harness.Workspace.Roots.Single(candidate => candidate.Name == "first.md");

        await harness.Workspace.RequestDeleteCommand.ExecuteAsync(node);

        Assert.Equal("Delete", harness.ViewModel.DeleteConfirmLabel);

        await harness.ViewModel.ConfirmDeleteCommand.ExecuteAsync(null);

        // Ничего не удалено: диалог остался и сменил текст на безвозвратное удаление,
        // а кнопка называет потерю прямо.
        Assert.True(harness.ViewModel.IsDeletePromptOpen);
        Assert.True(harness.ViewModel.IsPermanentDeletePrompt);
        Assert.Contains("permanently", harness.ViewModel.DeletePromptMessage);
        Assert.Equal("Delete permanently", harness.ViewModel.DeleteConfirmLabel);
        Assert.Equal("Cancel", harness.ViewModel.DeleteCancelLabel);
        Assert.True(node.IsPendingDelete);
        Assert.Contains(harness.Workspace.Roots, candidate => candidate.Name == "first.md");

        await harness.ViewModel.ConfirmDeleteCommand.ExecuteAsync(null);

        Assert.Equal([TestPaths.At("docs", "first.md")], harness.FileSystem.DeletedPaths);
        Assert.DoesNotContain(harness.Workspace.Roots, candidate => candidate.Name == "first.md");
    }

    /// <summary>
    /// Вкладка удаляемого файла закрывается без диалога «Сохранить», поэтому о потере
    /// правок говорит само подтверждение удаления.
    /// </summary>
    [Fact]
    public async Task DeletingAFileWithUnsavedChangesWarnsAboutThem()
    {
        var harness = await CreateAsync();
        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "first.md"));
        harness.ViewModel.ToggleEditModeCommand.Execute(null);
        harness.ViewModel.EditorSession!.SourceText = "# first edited";
        var node = harness.Workspace.Roots.Single(candidate => candidate.Name == "first.md");

        await harness.Workspace.RequestDeleteCommand.ExecuteAsync(node);

        Assert.Contains("Unsaved changes in \"first.md\" will be lost.", harness.ViewModel.DeletePromptMessage);

        await harness.ViewModel.ConfirmDeleteCommand.ExecuteAsync(null);

        Assert.False(harness.ViewModel.IsDirtyPromptOpen);
        Assert.Empty(harness.ViewModel.OpenDocuments.Tabs);
    }

    [Fact]
    public async Task DeletingAFolderNamesOnlyTheDirtyFilesInsideIt()
    {
        var harness = await CreateAsync();
        harness.Platform.TrashResult = TrashResult.Unsupported;

        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "adr", "adr_0001.md"));
        harness.ViewModel.ToggleEditModeCommand.Execute(null);
        harness.ViewModel.EditorSession!.SourceText = "# adr edited";

        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "first.md"));
        harness.ViewModel.ToggleEditModeCommand.Execute(null);
        harness.ViewModel.EditorSession!.SourceText = "# first edited";

        var node = harness.Workspace.Roots.Single(candidate => candidate.Name == "adr");
        await harness.Workspace.RequestDeleteCommand.ExecuteAsync(node);

        Assert.Contains("Unsaved changes in \"adr_0001.md\" will be lost.", harness.ViewModel.DeletePromptMessage);
        Assert.DoesNotContain("first.md", harness.ViewModel.DeletePromptMessage);

        // Переспрос про безвозвратное удаление не теряет предупреждение.
        await harness.ViewModel.ConfirmDeleteCommand.ExecuteAsync(null);

        Assert.True(harness.ViewModel.IsPermanentDeletePrompt);
        Assert.Contains("Unsaved changes in \"adr_0001.md\" will be lost.", harness.ViewModel.DeletePromptMessage);
    }

    /// <summary>
    /// Под диалогом несохранённых правок дерево не трогает файлы: удаление закрыло бы
    /// спрошенную вкладку, и ответ достался бы соседней.
    /// </summary>
    [Fact]
    public async Task TreeOperationsDoNothingWhileTheDirtyPromptIsOpen()
    {
        var harness = await CreateAsync();
        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "first.md"));
        harness.ViewModel.ToggleEditModeCommand.Execute(null);
        harness.ViewModel.EditorSession!.SourceText = "# first edited";

        // Имя начали вводить до вопроса, а подтверждают уже под ним.
        var folder = harness.Workspace.Roots.Single(static row => row.Name == "adr");
        harness.Workspace.StartRenameCommand.Execute(folder);
        harness.Workspace.EditName = "decisions";

        await harness.ViewModel.CloseActiveTabCommand.ExecuteAsync(null);
        Assert.True(harness.ViewModel.IsDirtyPromptOpen);

        await harness.Workspace.CommitEditCommand.ExecuteAsync(null);

        var file = harness.Workspace.Roots.Single(static row => row.Name == "first.md");
        await harness.Workspace.RequestDeleteCommand.ExecuteAsync(file);
        harness.Workspace.StartRenameCommand.Execute(file);
        harness.Workspace.StartNewFileCommand.Execute(null);
        harness.Workspace.StartNewFolderCommand.Execute(null);
        await harness.Workspace.DuplicateCommand.ExecuteAsync(file);

        Assert.True(harness.ViewModel.IsDirtyPromptOpen);
        Assert.False(harness.ViewModel.IsDeletePromptOpen);
        Assert.False(file.IsEditing);
        Assert.Equal(["adr", "first.md"], harness.Workspace.Roots.Select(static row => row.Name));
        Assert.Empty(harness.Platform.TrashedPaths);
        Assert.Single(harness.ViewModel.OpenDocuments.Tabs);
    }

    /// <summary>
    /// Под диалогом удаления сочетания окна не открывают, не закрывают и не переключают
    /// вкладки, а второй диалог не встаёт поверх первого: скрим держит мышь, а клавиатура
    /// раньше проходила — ⌘O показывал выбор файла поверх вопроса, ⌘W на грязной вкладке
    /// поднимал второй скрим. Здесь же — MM-36: подтверждённое удаление больше не может
    /// закрыть вкладку, о которой в этот момент спрашивает диалог правок.
    /// </summary>
    [Theory]
    [InlineData("open")]
    [InlineData("new")]
    [InlineData("drop")]
    [InlineData("close-tab")]
    [InlineData("next-tab")]
    [InlineData("close-folder")]
    public async Task WindowShortcutsDoNothingWhileTheDeletePromptIsOpen(string shortcut)
    {
        var harness = await CreateAsync();
        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "adr", "adr_0001.md"));
        await harness.ViewModel.OpenPathAsync(TestPaths.At("docs", "first.md"));
        harness.ViewModel.ToggleEditModeCommand.Execute(null);
        harness.ViewModel.EditorSession!.SourceText = "# first edited";
        var tabs = harness.ViewModel.OpenDocuments.Tabs.ToList();
        var first = harness.ViewModel.OpenDocuments.ActiveTab!;

        var file = harness.Workspace.Roots.Single(static row => row.Name == "first.md");
        await harness.Workspace.RequestDeleteCommand.ExecuteAsync(file);
        Assert.True(harness.ViewModel.IsModalDialogOpen);

        switch (shortcut)
        {
            case "open":
                harness.Picker.OpenPath = TestPaths.At("docs", "meeting.md");
                await harness.ViewModel.OpenFileCommand.ExecuteAsync(null);
                break;
            case "new":
                await harness.ViewModel.CreateNewDocumentCommand.ExecuteAsync(null);
                break;
            case "drop":
                await harness.ViewModel.OpenDroppedFileAsync(TestPaths.At("docs", "meeting.md"));
                break;
            case "close-tab":
                await harness.ViewModel.CloseActiveTabCommand.ExecuteAsync(null);
                break;
            case "next-tab":
                await harness.ViewModel.ActivateNextTabCommand.ExecuteAsync(null);
                break;
            case "close-folder":
                await harness.ViewModel.CloseFolderCommand.ExecuteAsync(null);
                break;
        }

        Assert.True(harness.ViewModel.IsDeletePromptOpen);
        Assert.False(harness.ViewModel.IsDirtyPromptOpen);
        Assert.Equal(0, harness.Picker.PickMarkdownFileCallCount);
        Assert.Equal(tabs, harness.ViewModel.OpenDocuments.Tabs);
        Assert.Same(first, harness.ViewModel.OpenDocuments.ActiveTab);
        Assert.NotNull(harness.ViewModel.Workspace);

        // Сам диалог удаления по-прежнему отвечает: файл удалён, его вкладка закрылась.
        await harness.ViewModel.ConfirmDeleteCommand.ExecuteAsync(null);

        Assert.False(harness.ViewModel.IsModalDialogOpen);
        Assert.Single(harness.ViewModel.OpenDocuments.Tabs);
    }

    /// <summary>Окно не закрывается, пока открыт диалог удаления, — как и под вопросом о правках.</summary>
    [Fact]
    public async Task WindowStaysOpenWhileTheDeletePromptIsOpen()
    {
        var harness = await CreateAsync();
        var closeRequests = 0;
        harness.ViewModel.CloseRequested += (_, _) => closeRequests++;
        await harness.Workspace.RequestDeleteCommand.ExecuteAsync(harness.Workspace.Roots.Single(static row => row.Name == "first.md"));

        Assert.True(harness.ViewModel.TryQueueCloseRequest());

        Assert.Equal(0, closeRequests);
        Assert.True(harness.ViewModel.IsDeletePromptOpen);
    }

    /// <summary>
    /// Файл, который ОС прислала под диалогом удаления, ждёт ответа и открывается после него —
    /// а не теряется и не открывается под скримом.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FilesFromTheSystemOpenAfterTheDeletePromptIsAnswered(bool confirm)
    {
        var harness = await CreateAsync();
        var file = harness.Workspace.Roots.Single(static row => row.Name == "first.md");
        await harness.Workspace.RequestDeleteCommand.ExecuteAsync(file);

        harness.Activation.RaiseFileActivated(TestPaths.At("docs", "meeting.md"));

        Assert.Empty(harness.ViewModel.OpenDocuments.Tabs);

        if (confirm)
        {
            await harness.ViewModel.ConfirmDeleteCommand.ExecuteAsync(null);
        }
        else
        {
            harness.ViewModel.ClearErrorCommand.Execute(null);
        }

        Assert.False(harness.ViewModel.IsDeletePromptOpen);
        Assert.Equal(TestPaths.At("docs", "meeting.md"), harness.ViewModel.CurrentDocumentPath);
    }

    [Fact]
    public async Task RevealAsksThePlatformForTheRealPath()
    {
        var harness = await CreateAsync();
        var node = harness.Workspace.Roots.Single(candidate => candidate.Name == "first.md");

        await harness.Workspace.RevealCommand.ExecuteAsync(node);

        Assert.Equal([TestPaths.At("docs", "first.md")], harness.Platform.RevealedPaths);
    }

    private static async Task<OperationsHarness> CreateAsync()
    {
        var fileSystem = new FakeWorkspaceFileSystem();
        fileSystem.AddDirectory(
            Root,
            WorkspaceEntry.ForDirectory(TestPaths.At("docs", "adr"), "adr"),
            WorkspaceEntry.ForFile(TestPaths.At("docs", "first.md"), "first.md"));
        fileSystem.AddDirectory(
            TestPaths.At("docs", "adr"),
            WorkspaceEntry.ForFile(TestPaths.At("docs", "adr", "adr_0001.md"), "adr_0001.md"));

        var platform = new FakePlatformServices(fileSystem);

        var loader = new StubDocumentLoader();
        loader.Sources[TestPaths.At("docs", "first.md")] = new MarkdownSource(TestPaths.At("docs", "first.md"), "first.md", "# first");
        loader.Sources[TestPaths.At("docs", "adr", "adr_0001.md")] = new MarkdownSource(TestPaths.At("docs", "adr", "adr_0001.md"), "adr_0001.md", "# adr");
        loader.Sources[TestPaths.At("docs", "meeting.md")] = new MarkdownSource(TestPaths.At("docs", "meeting.md"), "meeting.md", string.Empty);
        loader.Sources[TestPaths.At("docs", "renamed.md")] = new MarkdownSource(TestPaths.At("docs", "renamed.md"), "renamed.md", "# first");

        var saver = new RecordingDocumentSaver();

        var picker = new StubFilePicker();
        var activation = new StubCommandLineActivation();

        var viewModel = new ShellViewModel(
            new OpenDocumentUseCase(loader),
            new SaveDocumentUseCase(saver),
            picker,
            activation,
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
            static () => new FakeWorkspaceWatcher(),
            new RecordingWindowLauncher());

        await viewModel.OpenFolderPathAsync(Root);

        return new OperationsHarness(fileSystem, platform, saver, picker, activation, viewModel, viewModel.Workspace!);
    }

    private sealed record OperationsHarness(
        FakeWorkspaceFileSystem FileSystem,
        FakePlatformServices Platform,
        RecordingDocumentSaver Saver,
        StubFilePicker Picker,
        StubCommandLineActivation Activation,
        ShellViewModel ViewModel,
        WorkspaceViewModel Workspace);
}
