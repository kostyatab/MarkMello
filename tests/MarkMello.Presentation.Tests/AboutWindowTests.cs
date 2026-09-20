using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MarkMello.Application.UseCases;
using MarkMello.Domain;
using MarkMello.Presentation.Localization;
using MarkMello.Presentation.ViewModels;
using MarkMello.Presentation.Views;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Окно «О MarkMello» (ADR-0009 Rule 7): что в нём написано, чем оно закрывается и
/// откуда его открывают — пункт меню ⋯ вне macOS и системное меню на macOS.
/// </summary>
[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class AboutWindowTests
{
    private readonly AvaloniaHeadlessFixture _fixture;

    public AboutWindowTests(AvaloniaHeadlessFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Те же сведения, что в нижней строке карточки «Настройки»: версия сборки, лицензия,
    /// автор и ссылки проекта. Версия — из атрибута сборки, без метаданных после «+».
    /// </summary>
    [Fact]
    public void AboutShowsVersionLicenseAuthorAndProjectLinks()
    {
        var viewModel = new AboutViewModel(new LocalizationService(AppLanguage.English));

        Assert.Equal("About MarkMello", viewModel.WindowTitle);
        Assert.Equal("MarkMello", viewModel.ProductName);
        Assert.StartsWith("Version ", viewModel.VersionLine, StringComparison.Ordinal);
        Assert.DoesNotContain("+", viewModel.VersionLine, StringComparison.Ordinal);
        Assert.Equal("GPLv3", viewModel.License);
        Assert.Equal("Andrey Ermolaev", viewModel.Author);
        Assert.Equal("https://ermolaev.tech", viewModel.AuthorUrl);
        Assert.Equal("Website", viewModel.WebsiteLabel);
        Assert.Equal("https://markmello.ru", viewModel.WebsiteUrl);
        Assert.Equal("https://t.me/mark_mello", viewModel.TelegramUrl);
        Assert.Equal("https://github.com/dartdavros/MarkMello", viewModel.GitHubUrl);
    }

    /// <summary>Подписи идут за языком приложения — и в уже открытом окне тоже.</summary>
    [Fact]
    public void AboutFollowsTheApplicationLanguage()
    {
        var localization = new LocalizationService(AppLanguage.English);
        var viewModel = new AboutViewModel(localization);
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        localization.SetLanguage(AppLanguage.Russian);

        Assert.Equal("О MarkMello", viewModel.WindowTitle);
        Assert.StartsWith("Версия ", viewModel.VersionLine, StringComparison.Ordinal);
        Assert.Equal("Сайт", viewModel.WebsiteLabel);
        Assert.Contains(nameof(AboutViewModel.WindowTitle), changed);
        Assert.Contains(nameof(AboutViewModel.VersionLine), changed);
        Assert.Contains(nameof(AboutViewModel.WebsiteLabel), changed);
    }

    /// <summary>Отписка от смены языка: закрытое окно подписи больше не пересчитывает.</summary>
    [Fact]
    public void ClosedAboutStopsListeningToTheLanguage()
    {
        var localization = new LocalizationService(AppLanguage.English);
        var viewModel = new AboutViewModel(localization);
        viewModel.Dispose();
        var changed = 0;
        viewModel.PropertyChanged += (_, _) => changed++;

        localization.SetLanguage(AppLanguage.Russian);

        Assert.Equal(0, changed);
    }

    /// <summary>
    /// Окно ОС, а не оверлей: по центру экрана, размер не меняется, фон — с палитры,
    /// ссылки те же и в том же порядке, что в нижней строке карточки «Настройки».
    /// </summary>
    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public Task AboutWindowIsAFixedCenteredWindowOnThePalette(string theme)
    {
        return _fixture.RunAsync(() =>
        {
            var window = Show(theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light);

            Assert.False(window.CanResize);
            Assert.Equal(WindowStartupLocation.CenterScreen, window.WindowStartupLocation);
            Assert.Equal(SizeToContent.Height, window.SizeToContent);
            Assert.Same(Resource(window, "MmBackgroundBrush"), window.Background);

            Assert.Equal(
                ["https://ermolaev.tech", "https://markmello.ru", "https://t.me/mark_mello", "https://github.com/dartdavros/MarkMello"],
                window.GetVisualDescendants().OfType<Button>()
                    .Where(static button => button.Classes.Contains("mm-inline-link"))
                    .Select(static button => button.Tag as string));

            var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(static block => block.Text).ToList();
            Assert.Contains("GPLv3", texts);
            Assert.Contains(texts, static text => text is not null && text.StartsWith("Version ", StringComparison.Ordinal));

            window.Close();
            return Task.CompletedTask;
        });
    }

    /// <summary>Закрывается `Esc` и `⌘W` / `Ctrl+W` — как карточки внутри окна.</summary>
    [Theory]
    [InlineData(Key.Escape)]
    [InlineData(Key.W)]
    public Task AboutWindowClosesByKeyboard(Key key)
    {
        return _fixture.RunAsync(() =>
        {
            var window = Show(ThemeVariant.Light);
            var closed = false;
            window.Closed += (_, _) => closed = true;

            if (key == Key.Escape)
            {
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            }
            else
            {
                var modifier = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
                window.KeyPress(Key.W, modifier, PhysicalKey.W, "w");
            }

            Dispatcher.UIThread.RunJobs();

            Assert.True(closed);
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Одно нажатие пункта — одна просьба к <c>IWindowLauncher</c>: держать единственный
    /// экземпляр окна — его работа, а не команды.
    /// </summary>
    [Fact]
    public void AboutCommandAsksTheLauncherOncePerInvocation()
    {
        var launcher = new RecordingWindowLauncher();
        var viewModel = CreateShell(launcher);

        viewModel.ShowAboutCommand.Execute(null);

        Assert.Equal(1, launcher.AboutRequests);
    }

    /// <summary>
    /// «О MarkMello» в меню ⋯ — только вне macOS: на macOS пункт живёт в системном меню
    /// приложения (ADR-0009 Rule 4).
    /// </summary>
    [Fact]
    public void AboutMenuItemIsHiddenOnMacOsOnly()
    {
        var viewModel = CreateShell(new RecordingWindowLauncher());

        Assert.Equal(!OperatingSystem.IsMacOS(), viewModel.ShowsAboutMenuItem);
        Assert.Equal("About MarkMello", viewModel.AppMenuAbout);
    }

    /// <summary>
    /// Окно About не держит приложение: с последним главным окном закрывается и оно,
    /// а пока хоть одно главное окно осталось — нет.
    /// </summary>
    [Fact]
    public Task AboutWindowGoesAwayWithTheLastMainWindow()
    {
        return _fixture.RunAsync(() =>
        {
            var closing = new MainWindow();
            var other = new MainWindow();
            var about = new AboutWindow();

            Assert.Empty(MainWindow.AuxiliaryWindowsToCloseWith(closing, [closing, other, about]));
            Assert.Equal([about], MainWindow.AuxiliaryWindowsToCloseWith(closing, [closing, about]));
            return Task.CompletedTask;
        });
    }

    private static ShellViewModel CreateShell(RecordingWindowLauncher launcher)
    {
        var fileSystem = new FakeWorkspaceFileSystem();
        var platform = new FakePlatformServices(fileSystem);

        return new ShellViewModel(
            new OpenDocumentUseCase(new StubDocumentLoader()),
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
            launcher);
    }

    private static AboutWindow Show(ThemeVariant theme)
    {
        var window = new AboutWindow(new AboutViewModel(new LocalizationService(AppLanguage.English)))
        {
            RequestedThemeVariant = theme
        };
        window.Styles.Add(new FluentTheme());
        window.Styles.Add(Assert.IsAssignableFrom<IStyle>(LoadTheme("Controls.axaml")));
        window.Resources.MergedDictionaries.Add(Assert.IsAssignableFrom<IResourceProvider>(LoadTheme("Colors.axaml")));
        window.Resources.MergedDictionaries.Add(Assert.IsAssignableFrom<IResourceProvider>(LoadTheme("Typography.axaml")));
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static object LoadTheme(string themeFile)
        => AvaloniaXamlLoader.Load(new Uri("avares://MarkMello.Presentation/Themes/" + themeFile));

    private static object? Resource(Window window, string key)
    {
        Assert.True(window.TryFindResource(key, window.ActualThemeVariant, out var value), key);
        return value;
    }
}
