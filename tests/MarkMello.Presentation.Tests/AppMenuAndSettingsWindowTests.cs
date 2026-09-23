using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MarkMello.Application.Abstractions;
using MarkMello.Application.UseCases;
using MarkMello.Domain;
using MarkMello.Domain.Workspace;
using MarkMello.Presentation.Localization;
using MarkMello.Presentation.ViewModels;
using MarkMello.Presentation.Views;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Меню ⋯ (A-Menu, ADR-0009 Rule 4) и окно «Настройки» (A-AppSettings, Rule 7) в живом
/// окне: какие пункты видны, что делает клик по пункту, как окно открывается и закрывается.
/// </summary>
[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class AppMenuAndSettingsWindowTests
{
    private static readonly string Root = TestPaths.At("menu-docs");
    private static readonly string Readme = TestPaths.At("menu-docs", "README.md");

    private readonly AvaloniaHeadlessFixture _fixture;

    public AppMenuAndSettingsWindowTests(AvaloniaHeadlessFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Без папки пунктов папки нет; сохранение, перечитывание и закрытие вкладки гаснут,
    /// когда их не к чему применить. «Закрыть файл» из меню ушёл — его заменила вкладка.
    /// </summary>
    [Fact]
    public Task MenuOnTheWelcomeScreenHidesFolderItemsAndDimsDocumentCommands()
    {
        return _fixture.RunAsync(() =>
        {
            var viewModel = CreateViewModel();
            var window = Show(viewModel);

            viewModel.ToggleAppMenuCommand.Execute(null);
            Render(window);

            // «О MarkMello» и разделитель над ним — только вне macOS: там пункт в системном меню.
            string[] expectedItems = OperatingSystem.IsMacOS()
                ? ["MenuNewDocument", "MenuOpenFile", "MenuOpenFolder", "MenuSave", "MenuSaveAs", "MenuReload", "MenuCloseTab", "MenuSettings"]
                : ["MenuNewDocument", "MenuOpenFile", "MenuOpenFolder", "MenuSave", "MenuSaveAs", "MenuReload", "MenuCloseTab", "MenuSettings", "MenuAbout"];

            Assert.Equal(expectedItems, VisibleMenuItems(window).Select(static button => button.Name));
            Assert.Equal(
                ["MenuSave", "MenuSaveAs", "MenuReload", "MenuCloseTab"],
                VisibleMenuItems(window).Where(static button => !button.IsEffectivelyEnabled).Select(static button => button.Name));
            Assert.Equal(
                OperatingSystem.IsMacOS() ? 3 : 4,
                window.GetVisualDescendants().OfType<Border>().Count(static border => border.Classes.Contains("mm-menu-separator") && border.IsVisible));

            window.Hide();
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// С папкой появляются «Панель файлов» и «Закрыть папку». Клик по пункту выполняет
    /// команду и закрывает меню — даже если команда сама меню не трогает.
    /// </summary>
    [Fact]
    public Task MenuItemRunsItsCommandAndClosesTheMenu()
    {
        return _fixture.RunAsync(async () =>
        {
            var viewModel = await CreateViewModelWithFolderAsync();
            var window = Show(viewModel);

            viewModel.ToggleAppMenuCommand.Execute(null);
            Render(window);

            Assert.Contains("MenuFilesPanel", VisibleMenuItems(window).Select(static button => button.Name));
            Assert.Contains("MenuCloseFolder", VisibleMenuItems(window).Select(static button => button.Name));
            Assert.False(viewModel.IsSidebarCollapsed);

            Click(window, MenuItem(window, "MenuFilesPanel"));

            Assert.True(viewModel.IsSidebarCollapsed);
            Assert.False(viewModel.IsAppMenuOpen);
            Assert.Null(window.GetControl<ContentControl>("AppMenuPanel").Content);

            window.Hide();
        });
    }

    /// <summary>
    /// «Настройки…» открывают окно на рамке диалогов: скрим на всё окно, карточка 480 px.
    /// Меню под ним закрыто, ✕ окно закрывает.
    /// </summary>
    [Fact]
    public Task SettingsItemOpensTheSettingsWindowAndCloseButtonClosesIt()
    {
        return _fixture.RunAsync(() =>
        {
            var viewModel = CreateViewModel();
            var window = Show(viewModel);
            Assert.Empty(window.GetVisualDescendants().OfType<AppSettingsDialogView>());

            viewModel.ToggleAppMenuCommand.Execute(null);
            Render(window);
            Click(window, MenuItem(window, "MenuSettings"));

            Assert.True(viewModel.IsAppSettingsOpen);
            Assert.False(viewModel.IsAppMenuOpen);
            var frame = window.GetVisualDescendants().OfType<AppSettingsDialogView>().Single()
                .GetVisualDescendants().OfType<ModalDialogFrame>().Single();
            Assert.Equal(window.GetControl<Grid>("SidebarLayout").Bounds.Size, frame.Bounds.Size);
            var card = frame.GetVisualDescendants().OfType<Border>().Single(static border => border.Classes.Contains("mm-dialog-card"));
            Assert.Equal(480, card.Bounds.Width);

            // Клик по скриму окно не закрывает, как и у остальных диалогов.
            Click(window, new Point(20, window.Bounds.Height - 20));
            Assert.True(viewModel.IsAppSettingsOpen);

            Click(window, window.GetVisualDescendants().OfType<Button>().Single(static button => button.Name == "CloseSettingsButton"));

            Assert.False(viewModel.IsAppSettingsOpen);
            Assert.Empty(window.GetVisualDescendants().OfType<AppSettingsDialogView>());
            window.Hide();
            return Task.CompletedTask;
        });
    }

    /// <summary>⌘, (Ctrl+,) открывает окно на стартовом экране, Esc закрывает.</summary>
    [Theory]
    [InlineData(RawInputModifiers.Meta)]
    [InlineData(RawInputModifiers.Control)]
    public Task SettingsShortcutOpensTheWindowAndEscapeClosesIt(RawInputModifiers modifier)
    {
        return _fixture.RunAsync(() =>
        {
            var viewModel = CreateViewModel();
            var window = Show(viewModel);

            window.KeyPress(Key.OemComma, modifier, PhysicalKey.Comma, ",");
            Render(window);

            Assert.True(viewModel.IsAppSettingsOpen);
            Assert.Single(window.GetVisualDescendants().OfType<AppSettingsDialogView>());

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Render(window);

            Assert.False(viewModel.IsAppSettingsOpen);
            Assert.Empty(window.GetVisualDescendants().OfType<AppSettingsDialogView>());
            window.Hide();
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Одна страница по A-AppSettings: подсказка про Aa, язык, рамка окна, блок обновлений
    /// с одной кнопкой и строка версии со ссылками — в обеих темах на токенах палитры.
    /// </summary>
    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public Task SettingsWindowShowsOnePageOnThePalette(string theme)
    {
        return _fixture.RunAsync(() =>
        {
            var viewModel = CreateViewModel();
            var window = Show(viewModel, theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light);

            viewModel.OpenAppSettingsCommand.Execute(null);
            Render(window);

            var dialog = window.GetVisualDescendants().OfType<AppSettingsDialogView>().Single();
            var texts = dialog.GetVisualDescendants().OfType<TextBlock>().Select(static block => block.Text).ToList();
            Assert.Contains("Settings", texts);
            Assert.Contains("Language", texts);
            Assert.Contains("Window border", texts);
            Assert.Contains("UPDATES", texts);
            Assert.Contains("Manual checks", texts);
            Assert.Contains(texts, static text => text is not null && text.StartsWith("Softmark ", StringComparison.Ordinal) && text.EndsWith("· GPLv3", StringComparison.Ordinal));
            Assert.Null(dialog.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault());

            var update = dialog.GetVisualDescendants().OfType<Button>().Single(static button => button.Name == "UpdateActionButton");
            Assert.Equal("Check now", update.Content);
            Assert.Contains("mm-action-secondary", update.Classes);
            Assert.Same(Resource(window, "MmTabBrush"), dialog.GetVisualDescendants().OfType<Border>().Single(static border => border.Classes.Contains("mm-app-settings-update")).Background);

            Assert.Equal(
                ["https://github.com/kostyatab/Softmark"],
                dialog.GetVisualDescendants().OfType<Button>().Where(static button => button.Classes.Contains("mm-link")).Select(static button => button.Tag as string));

            window.Hide();
            return Task.CompletedTask;
        });
    }

    private static IEnumerable<Button> VisibleMenuItems(Window window)
        => window.GetControl<ContentControl>("AppMenuPanel")
            .GetVisualDescendants()
            .OfType<Button>()
            .Where(static button => button.Classes.Contains("mm-menu-command") && button.IsVisible);

    private static Button MenuItem(Window window, string name)
        => VisibleMenuItems(window).Single(button => button.Name == name);

    private static void Click(Window window, Control control)
        => Click(window, control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value);

    private static void Click(Window window, Point point)
    {
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Render(window);
    }

    /// <summary>
    /// Попадание мыши считается по отрисованной сцене, а меню закрывается отложенно —
    /// после команды пункта.
    /// </summary>
    private static void Render(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static object? Resource(Window window, string key)
    {
        Assert.True(window.TryFindResource(key, window.ActualThemeVariant, out var value), key);
        return value;
    }

    /// <summary>
    /// Окно из полного конструктора: только он подключает ⌘, и классы, которыми карточки
    /// под строкой проявляются. Тема — как в <see cref="ModalDialogTests"/>: без неё нет
    /// шаблонов Fluent и стилей карточек; меню и диалог создаются уже после неё.
    /// </summary>
    private static MainWindow Show(ShellViewModel viewModel, ThemeVariant? theme = null)
    {
        var window = new MainWindow(
            viewModel,
            StartupSmokeTestOptions.Disabled,
            new InMemorySettingsStore(),
            new RecordingStartupMetrics())
        {
            RequestedThemeVariant = theme ?? ThemeVariant.Light
        };
        window.Styles.Add(new FluentTheme());
        window.Styles.Add(Assert.IsAssignableFrom<IStyle>(LoadTheme("Controls.axaml")));
        window.Resources.MergedDictionaries.Add(Assert.IsAssignableFrom<IResourceProvider>(LoadTheme("Colors.axaml")));
        window.Resources.MergedDictionaries.Add(Assert.IsAssignableFrom<IResourceProvider>(LoadTheme("Icons.axaml")));
        window.Show();
        Render(window);
        return window;
    }

    private static object LoadTheme(string themeFile)
        => AvaloniaXamlLoader.Load(new Uri("avares://MarkMello.Presentation/Themes/" + themeFile));

    private static async Task<ShellViewModel> CreateViewModelWithFolderAsync()
    {
        var fileSystem = new FakeWorkspaceFileSystem();
        fileSystem.AddDirectory(Root, WorkspaceEntry.ForFile(Readme, "README.md"));
        var viewModel = CreateViewModel(fileSystem);
        await viewModel.OpenFolderPathAsync(Root);
        return viewModel;
    }

    private static ShellViewModel CreateViewModel(FakeWorkspaceFileSystem? fileSystem = null)
    {
        var loader = new StubDocumentLoader();
        loader.Sources[Readme] = new MarkdownSource(Readme, "README.md", "# readme");
        fileSystem ??= new FakeWorkspaceFileSystem();
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
}
