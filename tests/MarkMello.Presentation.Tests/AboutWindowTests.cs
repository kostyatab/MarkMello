using Avalonia.Controls;
using Avalonia.Controls.Documents;
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
/// Окно «О Softmark» (ADR-0009 Rule 7, ADR-0011): что в нём написано, чем оно закрывается и
/// откуда его открывают — пункт меню ⋯ вне macOS и системное меню на macOS.
/// </summary>
[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class AboutWindowTests
{
    private readonly AvaloniaHeadlessFixture _fixture;

    public AboutWindowTests(AvaloniaHeadlessFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Те же сведения, что в нижней строке карточки «Настройки»: имя, версия сборки,
    /// лицензия и ссылка на репозиторий форка, плюс атрибуция оригинала по GPLv3. Версия —
    /// из атрибута сборки, без метаданных после «+».
    /// </summary>
    [Fact]
    public void AboutShowsVersionLicenseAttributionAndRepositoryLink()
    {
        var viewModel = new AboutViewModel(new LocalizationService(AppLanguage.English));

        Assert.Equal("About Softmark", viewModel.WindowTitle);
        Assert.Equal("Softmark", viewModel.ProductName);
        Assert.StartsWith("Version ", viewModel.VersionLine, StringComparison.Ordinal);
        Assert.DoesNotContain("+", viewModel.VersionLine, StringComparison.Ordinal);
        Assert.Equal("GPLv3", viewModel.License);
        Assert.Equal("Fork of MarkMello © 2026 MarkMello contributors", viewModel.ForkAttribution);
        Assert.Equal("https://github.com/kostyatab/Softmark", viewModel.GitHubUrl);
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

        Assert.Equal("О Softmark", viewModel.WindowTitle);
        Assert.StartsWith("Версия ", viewModel.VersionLine, StringComparison.Ordinal);
        Assert.Equal("Форк MarkMello © 2026 MarkMello contributors", viewModel.ForkAttribution);
        Assert.Contains(nameof(AboutViewModel.WindowTitle), changed);
        Assert.Contains(nameof(AboutViewModel.VersionLine), changed);
        Assert.Contains(nameof(AboutViewModel.ForkAttribution), changed);
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
    /// Окно ОС, а не оверлей: по центру экрана, размер не меняется, фон — с палитры.
    /// Ссылка одна — на репозиторий форка, как в нижней строке карточки «Настройки»;
    /// ни сайта, ни телеграма оригинала. Вордмарк — одно слово «Softmark».
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
                ["https://github.com/kostyatab/Softmark"],
                window.GetVisualDescendants().OfType<Button>()
                    .Where(static button => button.Classes.Contains("mm-link"))
                    .Select(static button => button.Tag as string));

            var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(static block => block.Text).ToList();
            Assert.Contains("GPLv3", texts);
            Assert.Contains("Fork of MarkMello © 2026 MarkMello contributors", texts);
            Assert.Contains(texts, static text => text is not null && text.StartsWith("Version ", StringComparison.Ordinal));
            Assert.Equal("Softmark", WordmarkText(window.GetVisualDescendants().OfType<TextBlock>().Single(static block => block.Name == "AboutWordmark")));

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
    /// «О Softmark» в меню ⋯ — только вне macOS: на macOS пункт живёт в системном меню
    /// приложения (ADR-0009 Rule 4).
    /// </summary>
    [Fact]
    public void AboutMenuItemIsHiddenOnMacOsOnly()
    {
        var viewModel = CreateShell(new RecordingWindowLauncher());

        Assert.Equal(!OperatingSystem.IsMacOS(), viewModel.ShowsAboutMenuItem);
        Assert.Equal("About Softmark", viewModel.AppMenuAbout);
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

    /// <summary>Текст вордмарка из его Run: пробел между ними разбил бы слово надвое.</summary>
    internal static string WordmarkText(TextBlock wordmark)
        => string.Concat(Assert.IsAssignableFrom<InlineCollection>(wordmark.Inlines).Select(static inline => Assert.IsType<Run>(inline).Text));

    private static object LoadTheme(string themeFile)
        => AvaloniaXamlLoader.Load(new Uri("avares://MarkMello.Presentation/Themes/" + themeFile));

    private static object? Resource(Window window, string key)
    {
        Assert.True(window.TryFindResource(key, window.ActualThemeVariant, out var value), key);
        return value;
    }
}
