using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MarkMello.Application.Updates;
using MarkMello.Domain;
using MarkMello.Presentation.Localization;
using MarkMello.Presentation.Services;
using MarkMello.Presentation.ViewModels;
using MarkMello.Presentation.Views;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Окно обновления (ADR-0004, «Update Model»): что оно говорит и какие кнопки показывает в
/// каждом состоянии, чем закрывается и что окно одно.
/// </summary>
[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class UpdateWindowTests
{
    private readonly AvaloniaHeadlessFixture _fixture;

    public UpdateWindowTests(AvaloniaHeadlessFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Каждое состояние — свой заголовок и свои кнопки: вторичная слева, основная справа.
    /// У проверки и загрузки кнопка одна — «Отмена».
    /// </summary>
    [Theory]
    [InlineData("checking", "Checking for updates…", "", "Cancel")]
    [InlineData("available", "Version 1.2.3 is available", "Download", "Later")]
    [InlineData("downloading", "Downloading 1.2.3", "", "Cancel")]
    [InlineData("downloaded", "Version 1.2.3 downloaded", "Open DMG", "Show in Finder")]
    [InlineData("up-to-date", "You're up to date", "OK", "")]
    [InlineData("unavailable", "Updates aren't available for this build", "OK", "")]
    [InlineData("check-failed", "Couldn't check for updates", "Try Again", "Close")]
    [InlineData("download-failed", "Couldn't download the update", "Try Again", "Close")]
    public Task EachStateHasItsTitleAndActions(string state, string title, string primary, string secondary) => UpdateCoordinatorTests.Isolated(async () =>
    {
        var (viewModel, _, _) = await CreateInStateAsync(state);

        Assert.Equal(title, viewModel.Title);
        Assert.Equal(primary.Length > 0, viewModel.ShowsPrimaryAction);
        Assert.Equal(primary, viewModel.ShowsPrimaryAction ? viewModel.PrimaryActionLabel : string.Empty);
        Assert.Equal(secondary.Length > 0, viewModel.ShowsSecondaryAction);
        Assert.Equal(secondary, viewModel.ShowsSecondaryAction ? viewModel.SecondaryActionLabel : string.Empty);
    });

    /// <summary>Установщик и AppImage — свои подписи; «Показать в папке» на Linux нет.</summary>
    [Theory]
    [InlineData(AppUpdateInstallAction.LaunchInstaller, "Run Installer", "Show in Explorer")]
    [InlineData(AppUpdateInstallAction.RevealFile, "Show AppImage", "")]
    public Task DownloadedActionsFollowThePlatform(AppUpdateInstallAction action, string primary, string secondary) => UpdateCoordinatorTests.Isolated(async () =>
    {
        var (viewModel, _, _) = await CreateInStateAsync("downloaded", action);

        Assert.Equal(primary, viewModel.PrimaryActionLabel);
        Assert.Equal(secondary.Length > 0, viewModel.ShowsSecondaryAction);
        if (secondary.Length > 0)
        {
            Assert.Equal(secondary, viewModel.SecondaryActionLabel);
        }
    });

    /// <summary>
    /// «Доступна версия» называет текущую версию и файл, «Что нового» ведёт на страницу
    /// релиза; загрузка показывает файл и проценты.
    /// </summary>
    [Fact]
    public Task AvailableAndDownloadingDescribeTheRelease() => UpdateCoordinatorTests.Isolated(async () =>
    {
        var (viewModel, service, coordinator) = await CreateInStateAsync("available");

        Assert.Equal("You have Softmark 1.0.0. We'll download the installer for this Mac — Softmark-macos-arm64.dmg.", viewModel.Message);
        Assert.True(viewModel.ShowsWhatsNew);
        Assert.Equal("What's new in 1.2.3 ↗", viewModel.WhatsNewLabel);
        Assert.Equal("https://github.com/kostyatab/Softmark/releases/tag/v1.2.3", viewModel.WhatsNewUrl);

        service.PendingDownload = new TaskCompletionSource<UpdateDownloadResult>();
        _ = coordinator.DownloadAsync();
        Assert.True(viewModel.IsProgressIndeterminate);
        Assert.Equal("Softmark-macos-arm64.dmg", viewModel.Message);

        service.LastProgress!.Report(new UpdateDownloadProgress(62, 100));
        Assert.False(viewModel.IsProgressIndeterminate);
        Assert.Equal(62, viewModel.ProgressValue);
        Assert.Equal("Softmark-macos-arm64.dmg · 62%", viewModel.Message);
        coordinator.CancelDownload();
    });

    /// <summary>
    /// «Проверьте интернет» — только когда до GitHub не дошли. Лимит запросов, нет файла для
    /// платформы, битый ответ или сбой записи — другой текст.
    /// </summary>
    [Theory]
    [InlineData("check", true, "No connection to GitHub. Check your internet connection and try again.")]
    [InlineData("check", false, "GitHub didn't return a usable release. Try again later.")]
    [InlineData("download", true, "The download was interrupted. Check your internet connection and try again.")]
    [InlineData("download", false, "The update couldn't be downloaded. Try again later.")]
    public Task FailureMessageBlamesTheNetworkOnlyForConnectionProblems(string stage, bool connection, string expected) => UpdateCoordinatorTests.Isolated(async () =>
    {
        var service = new StubUpdateService
        {
            NextCheckResult = stage == "check"
                ? new UpdateCheckResult.Failed("x", connection)
                : UpdateCoordinatorTests.Available(),
            NextDownloadResult = new UpdateDownloadResult.Failed("x", connection)
        };
        var coordinator = new UpdateCoordinator(service, new FakePlatformServices());
        var viewModel = new UpdateViewModel(coordinator, new LocalizationService(AppLanguage.English), new RecordingWindowLauncher());

        await coordinator.CheckAsync();
        if (stage == "download")
        {
            await coordinator.DownloadAsync();
        }

        Assert.Equal(expected, viewModel.Message);
    });

    /// <summary>«Позже», «OK» и «Закрыть» закрывают окно; «Отмена» проверки ещё и отменяет её.</summary>
    [Theory]
    [InlineData("available", false)]
    [InlineData("up-to-date", true)]
    [InlineData("check-failed", false)]
    public Task ClosingActionsAskTheWindowToClose(string state, bool primary) => UpdateCoordinatorTests.Isolated(async () =>
    {
        var (viewModel, _, _) = await CreateInStateAsync(state);
        var closeRequests = 0;
        viewModel.CloseRequested += (_, _) => closeRequests++;

        await (primary ? viewModel.PrimaryActionCommand : viewModel.SecondaryActionCommand).ExecuteAsync(null);

        Assert.Equal(1, closeRequests);
    });

    [Fact]
    public Task CancellingTheCheckClosesTheWindowAndStopsTheCheck() => UpdateCoordinatorTests.Isolated(async () =>
    {
        var (viewModel, service, coordinator) = await CreateInStateAsync("checking");
        var closeRequests = 0;
        viewModel.CloseRequested += (_, _) => closeRequests++;

        await viewModel.SecondaryActionCommand.ExecuteAsync(null);

        Assert.Equal(1, closeRequests);
        Assert.Equal(UpdateState.Idle, coordinator.State);
        Assert.Equal(1, service.CheckCount);
    });

    /// <summary>«Скачать» начинает загрузку, «Повторить» после ошибки проверки — проверку.</summary>
    [Fact]
    public Task PrimaryActionsDriveTheCoordinator() => UpdateCoordinatorTests.Isolated(async () =>
    {
        var (viewModel, service, coordinator) = await CreateInStateAsync("check-failed");

        service.NextCheckResult = UpdateCoordinatorTests.Available();
        await viewModel.PrimaryActionCommand.ExecuteAsync(null);
        Assert.Equal(UpdateState.Available, coordinator.State);

        service.NextDownloadResult = new UpdateDownloadResult.Success(UpdateCoordinatorTests.Package(), "/tmp/Softmark.dmg");
        await viewModel.PrimaryActionCommand.ExecuteAsync(null);
        Assert.Equal(UpdateState.Downloaded, coordinator.State);

        await viewModel.PrimaryActionCommand.ExecuteAsync(null);
        Assert.Equal(1, service.PrepareCount);
    });

    /// <summary>Подписи окна идут за языком приложения — и в открытом окне тоже.</summary>
    [Fact]
    public Task UpdateTextsFollowTheApplicationLanguage() => UpdateCoordinatorTests.Isolated(async () =>
    {
        var localization = new LocalizationService(AppLanguage.English);
        var (viewModel, _, _) = await CreateInStateAsync("available", localization: localization);
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        localization.SetLanguage(AppLanguage.Russian);

        Assert.NotEmpty(changed);
        Assert.Equal("Обновление Softmark", viewModel.WindowTitle);
        Assert.Equal("Доступна версия 1.2.3", viewModel.Title);
        Assert.Equal("Скачать", viewModel.PrimaryActionLabel);
        Assert.Equal("Позже", viewModel.SecondaryActionLabel);
        Assert.Equal("Обновить", viewModel.ButtonLabel);
    });

    /// <summary>
    /// Окно ОС, как «О Softmark»: по центру, размер не меняется, фон с палитры, значок
    /// приложения на месте. В каждой теме — те же кнопки, что у модели.
    /// </summary>
    [Theory]
    [InlineData("Light", "available")]
    [InlineData("Dark", "available")]
    [InlineData("Light", "downloaded")]
    [InlineData("Dark", "check-failed")]
    public Task UpdateWindowIsAFixedCenteredWindowOnThePalette(string theme, string state)
    {
        return _fixture.RunAsync(async () =>
        {
            var (viewModel, _, _) = await CreateInStateAsync(state);
            var window = Show(viewModel, theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light);

            Assert.False(window.CanResize);
            Assert.Equal(WindowStartupLocation.CenterScreen, window.WindowStartupLocation);
            Assert.Equal(400, window.Width);
            Assert.Same(Resource(window, "MmBackgroundBrush"), window.Background);
            Assert.NotNull(window.GetControl<Image>("UpdateAppIcon").Source);
            Assert.Equal(viewModel.Title, window.GetControl<TextBlock>("UpdateTitle").Text);

            var primary = window.GetControl<Button>("UpdatePrimaryAction");
            var secondary = window.GetControl<Button>("UpdateSecondaryAction");
            Assert.Equal(viewModel.ShowsPrimaryAction, primary.IsVisible);
            Assert.Equal(viewModel.PrimaryActionLabel, primary.Content);
            Assert.Equal(viewModel.ShowsSecondaryAction, secondary.IsVisible);
            Assert.Equal(viewModel.SecondaryActionLabel, secondary.Content);
            Assert.Contains("mm-action-primary", primary.Classes);
            Assert.Contains("mm-action-secondary", secondary.Classes);
            Assert.Equal(state == "available", window.GetControl<Button>("UpdateWhatsNewLink").IsVisible);

            window.Close();
        });
    }

    /// <summary>Окно показывает общее состояние: переход модели сразу виден в открытом окне.</summary>
    [Fact]
    public Task OpenWindowFollowsTheSharedState()
    {
        return _fixture.RunAsync(async () =>
        {
            var (viewModel, service, coordinator) = await CreateInStateAsync("available");
            var window = Show(viewModel, ThemeVariant.Light);
            service.PendingDownload = new TaskCompletionSource<UpdateDownloadResult>();

            Click(window, window.GetControl<Button>("UpdatePrimaryAction"));

            Assert.Equal(UpdateState.Downloading, coordinator.State);
            Assert.True(window.GetControl<ProgressBar>("UpdateProgress").IsVisible);
            Assert.False(window.GetControl<Button>("UpdatePrimaryAction").IsVisible);
            Assert.Equal("Cancel", window.GetControl<Button>("UpdateSecondaryAction").Content);

            // Закрытие окна загрузку не прерывает.
            window.Close();
            Assert.Equal(UpdateState.Downloading, coordinator.State);
            coordinator.CancelDownload();
        });
    }

    /// <summary>Закрывается `Esc` и `⌘W` / `Ctrl+W`, как «О Softmark».</summary>
    [Theory]
    [InlineData(Key.Escape)]
    [InlineData(Key.W)]
    public Task UpdateWindowClosesByKeyboard(Key key)
    {
        return _fixture.RunAsync(async () =>
        {
            var (viewModel, _, _) = await CreateInStateAsync("up-to-date");
            var window = Show(viewModel, ThemeVariant.Light);
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
        });
    }

    /// <summary>
    /// Окно одно: повторный вызов выводит вперёд открытое, а после закрытия создаётся новое.
    /// Меню запускает проверку, кнопка в строке — нет.
    /// </summary>
    [Fact]
    public Task LauncherKeepsASingleUpdateWindow()
    {
        return _fixture.RunAsync(() =>
        {
            var service = new StubUpdateService { NextCheckResult = UpdateCoordinatorTests.Available() };
            var localization = new LocalizationService(AppLanguage.English);
            UpdateViewModel? updates = null;
            var launcher = new WindowLauncher(new SingleServiceProvider(() => updates));
            updates = new UpdateViewModel(new UpdateCoordinator(service, new FakePlatformServices()), localization, launcher);

            launcher.ShowUpdates(startCheck: true);
            var first = Assert.IsType<UpdateWindow>(launcher.UpdateWindow);
            Assert.Equal(1, service.CheckCount);

            launcher.ShowUpdates(startCheck: false);
            Assert.Same(first, launcher.UpdateWindow);
            Assert.Equal(1, service.CheckCount);

            first.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.Null(launcher.UpdateWindow);

            launcher.ShowUpdates(startCheck: false);
            Assert.NotSame(first, launcher.UpdateWindow);
            launcher.UpdateWindow!.Close();
            return Task.CompletedTask;
        });
    }

    /// <summary>Окно обновления не держит приложение: уходит вместе с последним главным окном.</summary>
    [Fact]
    public Task UpdateWindowGoesAwayWithTheLastMainWindow()
    {
        return _fixture.RunAsync(() =>
        {
            var closing = new MainWindow();
            var update = new UpdateWindow();

            Assert.Equal([update], MainWindow.AuxiliaryWindowsToCloseWith(closing, [closing, update]));
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Модель в нужном состоянии — через те же шаги, что у пользователя: проверка, загрузка.
    /// </summary>
    internal static async Task<(UpdateViewModel ViewModel, StubUpdateService Service, UpdateCoordinator Coordinator)> CreateInStateAsync(
        string state,
        AppUpdateInstallAction action = AppUpdateInstallAction.OpenDiskImage,
        ILocalizationService? localization = null,
        RecordingWindowLauncher? launcher = null)
    {
        var service = new StubUpdateService
        {
            NextCheckResult = state switch
            {
                "up-to-date" => new UpdateCheckResult.UpToDate("1.0.0", "1.0.0", DateTimeOffset.UnixEpoch, "https://example.test"),
                "unavailable" => new UpdateCheckResult.SourceNotConfigured("none"),
                "check-failed" => new UpdateCheckResult.Failed("offline", IsConnectionProblem: true),
                _ => UpdateCoordinatorTests.Available(action)
            }
        };
        var coordinator = new UpdateCoordinator(service, new FakePlatformServices());
        var viewModel = new UpdateViewModel(
            coordinator,
            localization ?? new LocalizationService(AppLanguage.English),
            launcher ?? new RecordingWindowLauncher());

        if (state == "checking")
        {
            service.PendingCheck = new TaskCompletionSource<UpdateCheckResult>();
            _ = coordinator.CheckAsync();
            return (viewModel, service, coordinator);
        }

        await coordinator.CheckAsync().ConfigureAwait(true);

        switch (state)
        {
            case "downloading":
                service.PendingDownload = new TaskCompletionSource<UpdateDownloadResult>();
                _ = coordinator.DownloadAsync();
                break;

            case "downloaded":
                service.NextDownloadResult = new UpdateDownloadResult.Success(UpdateCoordinatorTests.Package(action), "/tmp/Softmark.dmg");
                await coordinator.DownloadAsync().ConfigureAwait(true);
                break;

            case "download-failed":
                service.NextDownloadResult = new UpdateDownloadResult.Failed("reset");
                await coordinator.DownloadAsync().ConfigureAwait(true);
                break;
        }

        return (viewModel, service, coordinator);
    }

    private static UpdateWindow Show(UpdateViewModel viewModel, ThemeVariant theme)
    {
        var window = new UpdateWindow(viewModel)
        {
            RequestedThemeVariant = theme
        };
        window.Styles.Add(new FluentTheme());
        window.Styles.Add(Assert.IsAssignableFrom<IStyle>(LoadTheme("Controls.axaml")));
        window.Resources.MergedDictionaries.Add(Assert.IsAssignableFrom<IResourceProvider>(LoadTheme("Colors.axaml")));
        window.Resources.MergedDictionaries.Add(Assert.IsAssignableFrom<IResourceProvider>(LoadTheme("Typography.axaml")));
        window.Resources.MergedDictionaries.Add(Assert.IsAssignableFrom<IResourceProvider>(LoadTheme("Icons.axaml")));
        window.Show();
        Render(window);
        return window;
    }

    /// <summary>Контейнер на один сервис: запуску окон нужна только общая модель обновления.</summary>
    private sealed class SingleServiceProvider(Func<UpdateViewModel?> updates) : IServiceProvider
    {
        public object? GetService(Type serviceType)
            => serviceType == typeof(UpdateViewModel) ? updates() : null;
    }

    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Render(window);
    }

    private static void Render(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static object LoadTheme(string themeFile)
        => AvaloniaXamlLoader.Load(new Uri("avares://MarkMello.Presentation/Themes/" + themeFile));

    private static object? Resource(Window window, string key)
    {
        Assert.True(window.TryFindResource(key, window.ActualThemeVariant, out var value), key);
        return value;
    }
}
