using MarkMello.Application.Abstractions;
using MarkMello.Application.UseCases;
using MarkMello.Domain;
using MarkMello.Domain.Recent;
using MarkMello.Domain.Workspace;
using MarkMello.Presentation.Localization;
using MarkMello.Presentation.ViewModels;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// «Недавние» стартового экрана (MM-34, ADR-0009 Rule 8): какие открытия пишутся,
/// как строки выглядят и когда проверяется диск.
/// </summary>
public sealed class RecentItemsTests
{
    private static readonly string Docs = TestPaths.At("docs");
    private static readonly string Notes = TestPaths.At("docs", "notes.md");
    private static readonly string Readme = TestPaths.At("docs", "README.md");
    private static readonly string Other = TestPaths.At("elsewhere", "other.md");
    internal static readonly DateTimeOffset Now = new(2026, 9, 19, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FileFromOpenDialogIsRemembered()
    {
        var harness = CreateHarness();
        harness.FilePicker.OpenPath = Other;

        await harness.ViewModel.OpenFileCommand.ExecuteAsync(null);

        Assert.Equal([Other], await RecentPathsAsync(harness));
    }

    [Fact]
    public async Task FileFromCommandLineIsRemembered()
    {
        var harness = CreateHarness();
        harness.CommandLine.ActivationPath = Other;

        await harness.ViewModel.InitializeAsync();

        Assert.Equal([Other], await RecentPathsAsync(harness));
    }

    [Fact]
    public async Task FileFromOperatingSystemIsRemembered()
    {
        var harness = CreateHarness();
        await harness.ViewModel.InitializeAsync();

        harness.CommandLine.RaiseFileActivated(Other);
        await WaitUntilAsync(() => harness.ViewModel.CurrentDocumentPath == Other);

        Assert.Equal([Other], await RecentPathsAsync(harness));
    }

    [Fact]
    public async Task DroppedFileIsRemembered()
    {
        var harness = CreateHarness();

        await harness.ViewModel.OpenDroppedFileAsync(Other);

        Assert.Equal([Other], await RecentPathsAsync(harness));
    }

    [Fact]
    public async Task SaveAsRemembersTheNewPath()
    {
        var harness = CreateHarness();
        var savedAs = TestPaths.At("elsewhere", "renamed.md");
        harness.FilePicker.SavePath = savedAs;
        await harness.ViewModel.OpenDroppedFileAsync(Other);
        await harness.ViewModel.ToggleEditModeCommand.ExecuteAsync(null);
        harness.ViewModel.EditorSession!.SourceText = "changed";

        await harness.ViewModel.SaveAsCommand.ExecuteAsync(null);

        Assert.Equal([savedAs, Other], await RecentPathsAsync(harness));
    }

    [Fact]
    public async Task PlainSaveDoesNotReorderRecent()
    {
        var harness = CreateHarness();
        harness.Loader.Sources[Notes] = new MarkdownSource(Notes, "notes.md", "# notes");
        await harness.ViewModel.OpenDroppedFileAsync(Other);
        await harness.ViewModel.OpenDroppedFileAsync(Notes);
        await harness.ViewModel.OpenDroppedFileAsync(Other);
        await harness.ViewModel.ToggleEditModeCommand.ExecuteAsync(null);
        harness.ViewModel.EditorSession!.SourceText = "changed";
        var before = await RecentPathsAsync(harness);

        await harness.ViewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(before, await RecentPathsAsync(harness));
    }

    [Fact]
    public async Task FirstSaveOfDraftRemembersChosenPath()
    {
        var harness = CreateHarness();
        var chosen = TestPaths.At("elsewhere", "draft.md");
        harness.FilePicker.SavePath = chosen;
        await harness.ViewModel.CreateNewDocumentCommand.ExecuteAsync(null);
        harness.ViewModel.EditorSession!.SourceText = "# draft";

        await harness.ViewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal([chosen], await RecentPathsAsync(harness));
    }

    [Fact]
    public async Task LinkFromDocumentIsNotRemembered()
    {
        var harness = CreateHarness();

        await harness.ViewModel.OpenLinkedDocumentAsync(Other);

        Assert.Equal(Other, harness.ViewModel.CurrentDocumentPath);
        Assert.Empty(await RecentPathsAsync(harness));
    }

    [Fact]
    public async Task DraftWithoutPathIsNotRemembered()
    {
        var harness = CreateHarness();

        await harness.ViewModel.CreateNewDocumentCommand.ExecuteAsync(null);

        Assert.Empty(await RecentPathsAsync(harness));
    }

    [Fact]
    public async Task FailedOpenIsNotRemembered()
    {
        var harness = CreateHarness();

        await harness.ViewModel.OpenDroppedFileAsync(TestPaths.At("elsewhere", "missing.md"));

        Assert.True(harness.ViewModel.IsError);
        Assert.Empty(await RecentPathsAsync(harness));
    }

    [Fact]
    public async Task OpenedFolderIsRememberedButItsReadmeAndTreeFilesAreNot()
    {
        var harness = CreateHarness();
        harness.Loader.Sources[Notes] = new MarkdownSource(Notes, "notes.md", "# notes");

        await harness.ViewModel.OpenFolderPathAsync(Docs);
        var workspace = harness.ViewModel.Workspace!;
        await workspace.OpenNodeCommand.ExecuteAsync(workspace.Roots.Single(node => node.Name == "notes.md"));

        Assert.Equal(Notes, harness.ViewModel.CurrentDocumentPath);
        var entry = Assert.Single(await harness.Recent.GetEntriesAsync());
        Assert.Equal(Docs, entry.Path);
        Assert.Equal(RecentEntryKind.Folder, entry.Kind);
    }

    [Fact]
    public async Task FolderAlreadyOpenInAnotherWindowIsStillRemembered()
    {
        var harness = CreateHarness();
        await harness.ViewModel.OpenFolderPathAsync(Docs);
        var elsewhere = TestPaths.At("elsewhere");
        harness.WindowLauncher.OpenFolders.Add(elsewhere);

        await harness.ViewModel.OpenFolderPathAsync(elsewhere);

        Assert.Equal([elsewhere, Docs], await RecentPathsAsync(harness));
    }

    [Fact]
    public async Task ReopeningMovesEntryToTop()
    {
        var harness = CreateHarness();
        harness.Loader.Sources[Notes] = new MarkdownSource(Notes, "notes.md", "# notes");

        await harness.ViewModel.OpenDroppedFileAsync(Other);
        await harness.ViewModel.OpenDroppedFileAsync(Notes);
        await harness.ViewModel.OpenDroppedFileAsync(Other);

        Assert.Equal([Other, Notes], await RecentPathsAsync(harness));
    }

    [Fact]
    public async Task RecordsReachSettingsInBackground()
    {
        var harness = CreateHarness();

        await harness.ViewModel.OpenDroppedFileAsync(Other);
        await harness.Recent.FlushAsync();

        Assert.Equal([Other], harness.Settings.Recent.Select(static entry => entry.Path));
    }

    [Fact]
    public async Task StartupWithFileNeitherBuildsRowsNorProbesDisk()
    {
        var harness = CreateHarness();
        harness.Settings.Recent = [new RecentEntry(Notes, RecentEntryKind.File, Now)];
        harness.CommandLine.ActivationPath = Other;

        await harness.ViewModel.InitializeAsync();

        Assert.False(harness.ViewModel.IsWelcome);
        Assert.Empty(harness.ViewModel.RecentItems);
        Assert.Equal(0, harness.Probe.CallCount);
    }

    [Fact]
    public async Task WelcomeShowsRowsFreshFirstWithHomeShortenedAndDates()
    {
        var harness = CreateHarness();
        var home = TestPaths.At("home");
        harness.Settings.Recent =
        [
            new RecentEntry(Path.Combine(home, "Documents", "sample"), RecentEntryKind.Folder, Now.AddDays(-1)),
            new RecentEntry(Path.Combine(home, "Projects", "README.md"), RecentEntryKind.File, Now.AddHours(-2)),
            new RecentEntry(Other, RecentEntryKind.File, new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero)),
            new RecentEntry(Notes, RecentEntryKind.File, new DateTimeOffset(2025, 12, 1, 12, 0, 0, TimeSpan.Zero))
        ];

        await harness.ViewModel.InitializeAsync();
        harness.ViewModel.ProbeRecentItems();
        await WaitUntilAsync(() => harness.Probe.CallCount == 1);

        Assert.True(harness.ViewModel.HasRecentItems);
        var rows = harness.ViewModel.RecentItems;
        Assert.Equal(["README.md", "sample", "other.md", "notes.md"], rows.Select(static row => row.Name));
        Assert.Equal(
            ["~" + Path.DirectorySeparatorChar + "Projects", "~" + Path.DirectorySeparatorChar + "Documents"],
            rows.Take(2).Select(static row => row.Location));
        Assert.Equal(["today", "yesterday", "Sep 15", "Dec 1, 2025"], rows.Select(static row => row.OpenedLabel));
        Assert.True(rows[1].IsFolder);
    }

    [Fact]
    public async Task RussianDatesFollowInterfaceLanguage()
    {
        var harness = CreateHarness(AppLanguage.Russian);
        harness.Settings.Language = AppLanguage.Russian;
        harness.Settings.Recent =
        [
            new RecentEntry(Notes, RecentEntryKind.File, Now.AddMinutes(-5)),
            new RecentEntry(Readme, RecentEntryKind.File, Now.AddDays(-1)),
            new RecentEntry(Other, RecentEntryKind.File, new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero))
        ];

        await harness.ViewModel.InitializeAsync();

        Assert.Equal(["сегодня", "вчера", "15 сент."], harness.ViewModel.RecentItems.Select(static row => row.OpenedLabel));
    }

    /// <summary>
    /// Диск проверяется после первого кадра стартового экрана — его запрашивает вид, —
    /// а не вместе со стартом окна.
    /// </summary>
    [Fact]
    public async Task DiskIsProbedOnlyAfterWelcomeIsShownAndOncePerShow()
    {
        var harness = CreateHarness();
        harness.Settings.Recent = [new RecentEntry(Notes, RecentEntryKind.File, Now)];

        await harness.ViewModel.InitializeAsync();

        Assert.Single(harness.ViewModel.RecentItems);
        Assert.Equal(0, harness.Probe.CallCount);

        harness.ViewModel.ProbeRecentItems();
        harness.ViewModel.ProbeRecentItems();
        await WaitUntilAsync(() => harness.Probe.CallCount == 1);

        await harness.ViewModel.OpenDroppedFileAsync(Other);
        await harness.ViewModel.CloseActiveTabCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => harness.ViewModel.RecentItems.Count == 2);
        harness.ViewModel.ProbeRecentItems();

        await WaitUntilAsync(() => harness.Probe.CallCount == 2);
    }

    /// <summary>
    /// Сбой стартовой активации не оставляет окно без стартового экрана: раньше флаг
    /// ставился только в конце удачного старта, и тело окна оставалось пустым навсегда.
    /// </summary>
    [Fact]
    public async Task FailedStartupActivationStillShowsWelcome()
    {
        var harness = CreateHarness();
        var broken = TestPaths.At("broken");
        harness.FileSystem.FailWith(broken, new InvalidOperationException("inotify limit"));
        harness.CommandLine.ActivationFolderPath = broken;

        await Assert.ThrowsAsync<InvalidOperationException>(harness.ViewModel.InitializeAsync);

        Assert.True(harness.ViewModel.IsWelcome);
        Assert.NotNull(harness.ViewModel.WelcomeContent);
    }

    [Fact]
    public async Task EmptyListHidesTheBlock()
    {
        var harness = CreateHarness();

        await harness.ViewModel.InitializeAsync();

        Assert.True(harness.ViewModel.IsWelcome);
        Assert.False(harness.ViewModel.HasRecentItems);
        Assert.Equal(0, harness.Probe.CallCount);
    }

    [Fact]
    public async Task ClearEmptiesTheListWithoutAsking()
    {
        var harness = CreateHarness();
        harness.Settings.Recent = [new RecentEntry(Notes, RecentEntryKind.File, Now)];
        await harness.ViewModel.InitializeAsync();

        await harness.ViewModel.ClearRecentItemsCommand.ExecuteAsync(null);

        Assert.False(harness.ViewModel.IsModalDialogOpen);
        Assert.False(harness.ViewModel.HasRecentItems);
        Assert.Empty(await harness.Recent.GetEntriesAsync());
    }

    [Fact]
    public async Task MissingEntryIsDimmedAndAsksBeforeRemoving()
    {
        var harness = CreateHarness();
        harness.Settings.Recent =
        [
            new RecentEntry(Notes, RecentEntryKind.File, Now),
            new RecentEntry(Other, RecentEntryKind.File, Now.AddMinutes(-1))
        ];
        harness.Probe.Missing.Add(Notes);
        await harness.ViewModel.InitializeAsync();
        harness.ViewModel.ProbeRecentItems();
        await WaitUntilAsync(() => harness.ViewModel.RecentItems.FirstOrDefault()?.IsMissing == true);

        var missing = harness.ViewModel.RecentItems[0];
        Assert.False(harness.ViewModel.RecentItems[1].IsMissing);

        await missing.OpenCommand.ExecuteAsync(null);

        Assert.True(harness.ViewModel.IsRecentRemovePromptOpen);
        Assert.True(harness.ViewModel.IsModalDialogOpen);
        Assert.Equal("File not found", harness.ViewModel.RecentRemovePromptTitle);
        Assert.Equal("Remove “notes.md” from Recent?", harness.ViewModel.RecentRemovePromptMessage);
        Assert.Null(harness.ViewModel.CurrentDocumentPath);

        await harness.ViewModel.ConfirmRecentRemoveCommand.ExecuteAsync(null);

        Assert.False(harness.ViewModel.IsRecentRemovePromptOpen);
        Assert.Equal(["other.md"], harness.ViewModel.RecentItems.Select(static row => row.Name));
        Assert.Equal([Other], await RecentPathsAsync(harness));
    }

    [Fact]
    public async Task EscapeCancelsRemovePromptAndKeepsEntry()
    {
        var harness = CreateHarness();
        harness.Settings.Recent = [new RecentEntry(Docs, RecentEntryKind.Folder, Now)];
        harness.Probe.Missing.Add(Docs);
        await harness.ViewModel.InitializeAsync();
        harness.ViewModel.ProbeRecentItems();
        await WaitUntilAsync(() => harness.ViewModel.RecentItems.FirstOrDefault()?.IsMissing == true);

        await harness.ViewModel.RecentItems[0].OpenCommand.ExecuteAsync(null);
        Assert.Equal("Folder not found", harness.ViewModel.RecentRemovePromptTitle);

        harness.ViewModel.ClearErrorCommand.Execute(null);

        Assert.False(harness.ViewModel.IsRecentRemovePromptOpen);
        Assert.Single(harness.ViewModel.RecentItems);
        Assert.Equal([Docs], await RecentPathsAsync(harness));
    }

    [Fact]
    public async Task ClickOnFileRowOpensItAndMovesItUp()
    {
        var harness = CreateHarness();
        harness.Loader.Sources[Notes] = new MarkdownSource(Notes, "notes.md", "# notes");
        harness.Settings.Recent =
        [
            new RecentEntry(Other, RecentEntryKind.File, Now),
            new RecentEntry(Notes, RecentEntryKind.File, Now.AddDays(-3))
        ];
        await harness.ViewModel.InitializeAsync();

        await harness.ViewModel.RecentItems.Single(row => row.Name == "notes.md").OpenCommand.ExecuteAsync(null);

        Assert.Equal(Notes, harness.ViewModel.CurrentDocumentPath);
        Assert.False(harness.ViewModel.IsWelcome);
        Assert.Equal([Notes, Other], await RecentPathsAsync(harness));
    }

    [Fact]
    public async Task ClickOnFolderRowOpensFolderInThisWindow()
    {
        var harness = CreateHarness();
        harness.Settings.Recent = [new RecentEntry(Docs, RecentEntryKind.Folder, Now.AddDays(-3))];
        await harness.ViewModel.InitializeAsync();

        await harness.ViewModel.RecentItems[0].OpenCommand.ExecuteAsync(null);

        Assert.NotNull(harness.ViewModel.Workspace);
        Assert.Empty(harness.WindowLauncher.NewWindowFolders);
    }

    [Fact]
    public async Task WelcomeRefreshesWhenShownAgain()
    {
        var harness = CreateHarness();
        await harness.ViewModel.InitializeAsync();
        Assert.False(harness.ViewModel.HasRecentItems);

        await harness.ViewModel.OpenDroppedFileAsync(Other);
        await harness.ViewModel.CloseActiveTabCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => harness.ViewModel.HasRecentItems);

        Assert.True(harness.ViewModel.IsWelcome);
        Assert.Equal(["other.md"], harness.ViewModel.RecentItems.Select(static row => row.Name));
    }

    [Fact]
    public async Task SmokeRunReadsButNeverWritesRecent()
    {
        var settings = new InMemorySettingsStore { Recent = [new RecentEntry(Notes, RecentEntryKind.File, Now)] };
        var recent = new RecentItemsUseCase(
            settings,
            new RecordingPathProbe(),
            new StartupSmokeTestOptions(IsEnabled: true, TimeSpan.FromSeconds(1)));

        await recent.RecordFileAsync(Other);
        await recent.RecordFolderAsync(Docs);
        await recent.ClearAsync();
        await recent.FlushAsync();

        Assert.Equal([Notes], (await recent.GetEntriesAsync()).Select(static entry => entry.Path));
        Assert.Equal([Notes], settings.Recent.Select(static entry => entry.Path));
    }

    [Fact]
    public async Task ExitWritesPendingRecordWithoutWaitingForDelay()
    {
        var settings = new InMemorySettingsStore();
        var recent = new RecentItemsUseCase(
            settings,
            new RecordingPathProbe(),
            TimeProvider.System,
            ignoreCase: false,
            writeDelay: TimeSpan.FromMinutes(5));

        await recent.RecordFileAsync(Other);
        Assert.Empty(settings.Recent);

        recent.FlushBeforeExit(TimeSpan.FromSeconds(5));

        Assert.Equal([Other], settings.Recent.Select(static entry => entry.Path));
    }

    private static async Task<IReadOnlyList<string>> RecentPathsAsync(RecentHarness harness)
        => (await harness.Recent.GetEntriesAsync()).Select(static entry => entry.Path).ToList();

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(5);
        }

        Assert.True(condition());
    }

    internal static RecentHarness CreateHarness(AppLanguage language = AppLanguage.English)
    {
        var fileSystem = new FakeWorkspaceFileSystem();
        fileSystem.AddDirectory(
            Docs,
            WorkspaceEntry.ForFile(Readme, "README.md"),
            WorkspaceEntry.ForFile(Notes, "notes.md"));
        fileSystem.AddDirectory(TestPaths.At("elsewhere"));

        var loader = new StubDocumentLoader();
        loader.Sources[Readme] = new MarkdownSource(Readme, "README.md", "# readme");
        loader.Sources[Other] = new MarkdownSource(Other, "other.md", "# other");

        var settings = new InMemorySettingsStore { Language = language };
        var probe = new RecordingPathProbe();
        var recent = new RecentItemsUseCase(
            settings,
            probe,
            new FixedTimeProvider(Now),
            ignoreCase: false,
            writeDelay: TimeSpan.Zero);
        var picker = new StubFilePicker();
        var commandLine = new StubCommandLineActivation();
        var launcher = new RecordingWindowLauncher();

        var viewModel = new ShellViewModel(
            new OpenDocumentUseCase(loader),
            new SaveDocumentUseCase(new RecordingDocumentSaver()),
            picker,
            commandLine,
            new LocalizationService(language),
            settings,
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
            launcher,
            recentItems: recent);

        return new RecentHarness(loader, picker, commandLine, settings, probe, recent, launcher, fileSystem, viewModel);
    }

    internal sealed record RecentHarness(
        StubDocumentLoader Loader,
        StubFilePicker FilePicker,
        StubCommandLineActivation CommandLine,
        InMemorySettingsStore Settings,
        RecordingPathProbe Probe,
        RecentItemsUseCase Recent,
        RecordingWindowLauncher WindowLauncher,
        FakeWorkspaceFileSystem FileSystem,
        ShellViewModel ViewModel);

    internal sealed class RecordingPathProbe : IPathExistenceProbe
    {
        public HashSet<string> Missing { get; } = [];

        public int CallCount { get; private set; }

        public Task<IReadOnlyList<string>> FindMissingAsync(
            IReadOnlyList<RecentEntry> entries,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            IReadOnlyList<string> missing = entries.Select(static entry => entry.Path).Where(Missing.Contains).ToList();
            return Task.FromResult(missing);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
