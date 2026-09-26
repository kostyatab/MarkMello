using MarkMello.Application.Updates;
using MarkMello.Presentation.Services;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Одно состояние обновления на приложение (ADR-0004, «Update Model»): фоновая и ручная
/// проверки, загрузка с ходом и отменой, передача скачанного установке ОС.
/// <para>
/// Тесты идут без контекста синхронизации (<see cref="Isolated"/>): в приложении продолжения
/// возвращаются в UI-поток, а здесь выполняются сразу, и порядок событий детерминирован.
/// </para>
/// </summary>
public sealed class UpdateCoordinatorTests
{
    private static readonly TimeSpan ShortDelay = TimeSpan.FromMilliseconds(10);

    /// <summary>
    /// Фоновая проверка нашла новую версию — появляется только кнопка: состояние окна не
    /// меняется. Нажатие на кнопку выводит найденную версию в окно.
    /// </summary>
    [Fact]
    public Task BackgroundCheckShowsANewerVersionOnTheButtonOnly() => Isolated(async () =>
    {
        var service = new StubUpdateService { NextCheckResult = Available() };
        using var coordinator = Create(service, ShortDelay);

        coordinator.StartBackgroundCheck();
        await coordinator.BackgroundCheck!;

        Assert.Equal(UpdateState.Idle, coordinator.State);
        Assert.Equal(UpdateState.Available, coordinator.ButtonState);
        Assert.True(coordinator.HasPendingUpdate);
        Assert.Equal("1.2.3", coordinator.ButtonPackage?.ReleaseVersion);

        coordinator.ShowPendingUpdate();

        Assert.Equal(UpdateState.Available, coordinator.State);
        Assert.Equal("1.2.3", coordinator.Package?.ReleaseVersion);
        Assert.IsType<UpdateCheckResult.UpdateAvailable>(coordinator.LastCheckResult);
    });

    /// <summary>Последняя версия, нет сети, платформа не поддерживается — фоновая проверка молчит.</summary>
    [Theory]
    [InlineData("up-to-date")]
    [InlineData("failed")]
    [InlineData("unsupported")]
    [InlineData("not-configured")]
    public Task BackgroundCheckStaysSilentWithoutANewerVersion(string outcome) => Isolated(async () =>
    {
        UpdateCheckResult result = outcome switch
        {
            "up-to-date" => new UpdateCheckResult.UpToDate("1.0.0", "1.0.0", DateTimeOffset.UnixEpoch, "https://example.test"),
            "failed" => new UpdateCheckResult.Failed("offline"),
            "unsupported" => new UpdateCheckResult.UnsupportedPlatform("FreeBSD", "x64"),
            _ => new UpdateCheckResult.SourceNotConfigured("none")
        };
        var service = new StubUpdateService { NextCheckResult = result };
        using var coordinator = Create(service, ShortDelay);

        coordinator.StartBackgroundCheck();
        await coordinator.BackgroundCheck!;

        Assert.Equal(1, service.CheckCount);
        Assert.Equal(UpdateState.Idle, coordinator.State);
        Assert.False(coordinator.HasPendingUpdate);
    });

    /// <summary>Сколько бы окон ни открылось, фоновая проверка одна за запуск.</summary>
    [Fact]
    public Task BackgroundCheckRunsOncePerRun() => Isolated(async () =>
    {
        var service = new StubUpdateService();
        using var coordinator = Create(service, ShortDelay);

        coordinator.StartBackgroundCheck();
        var first = coordinator.BackgroundCheck;
        coordinator.StartBackgroundCheck();
        await first!;

        Assert.Same(first, coordinator.BackgroundCheck);
        Assert.Equal(1, service.CheckCount);
    });

    /// <summary>До конца паузы в сеть не ходим; выход отменяет проверку до запроса.</summary>
    [Fact]
    public Task ExitBeforeTheDelayCancelsTheBackgroundCheckWithoutANetworkCall() => Isolated(async () =>
    {
        var service = new StubUpdateService { NextCheckResult = Available() };
        var coordinator = Create(service, TimeSpan.FromMinutes(1));

        coordinator.StartBackgroundCheck();
        Assert.Equal(0, service.CheckCount);
        coordinator.Dispose();
        await coordinator.BackgroundCheck!;

        Assert.Equal(0, service.CheckCount);
        Assert.Equal(UpdateState.Idle, coordinator.State);
    });

    /// <summary>
    /// Фоновая проверка нашла версию, пока окно показывает «У вас последняя версия»: окно само
    /// не переключается, меняется только кнопка.
    /// </summary>
    [Fact]
    public Task BackgroundFindingAfterUpToDateChangesOnlyTheButton() => Isolated(async () =>
    {
        var service = new StubUpdateService { NextCheckResult = UpToDate() };
        using var coordinator = Create(service, TimeSpan.FromMilliseconds(50));

        coordinator.StartBackgroundCheck();
        await coordinator.CheckAsync();
        service.NextCheckResult = Available();
        await coordinator.BackgroundCheck!;

        Assert.Equal(2, service.CheckCount);
        Assert.Equal(UpdateState.UpToDate, coordinator.State);
        Assert.Equal(UpdateState.Available, coordinator.ButtonState);
    });

    /// <summary>
    /// Отменённая или не дошедшая до GitHub ручная проверка ничего не узнала — фоновая находка
    /// после неё всё равно показывается кнопкой.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task BackgroundFindingSurvivesACancelledOrFailedManualCheck(bool cancel) => Isolated(async () =>
    {
        var service = new StubUpdateService { NextCheckResult = new UpdateCheckResult.Failed("offline", IsConnectionProblem: true) };
        using var coordinator = Create(service, TimeSpan.FromMilliseconds(50));
        coordinator.StartBackgroundCheck();

        if (cancel)
        {
            service.PendingCheck = new TaskCompletionSource<UpdateCheckResult>();
            var check = coordinator.CheckAsync();
            coordinator.CancelCheck();
            await check;
            service.PendingCheck = null;
        }
        else
        {
            await coordinator.CheckAsync();
        }

        service.NextCheckResult = Available();
        await coordinator.BackgroundCheck!;

        Assert.Equal(cancel ? UpdateState.Idle : UpdateState.CheckFailed, coordinator.State);
        Assert.Equal(UpdateState.Available, coordinator.ButtonState);
    });

    /// <summary>
    /// Ручной ответ важнее того, что было известно раньше: «последняя версия» убирает кнопку,
    /// а неудачная проверка оставляет найденную раньше версию на кнопке.
    /// </summary>
    [Fact]
    public Task ManualAnswerOverridesAnEarlierFinding() => Isolated(async () =>
    {
        var service = new StubUpdateService { NextCheckResult = Available() };
        using var coordinator = Create(service);
        await coordinator.CheckAsync();

        service.NextCheckResult = new UpdateCheckResult.Failed("offline", IsConnectionProblem: true);
        await coordinator.CheckAsync();
        Assert.Equal(UpdateState.CheckFailed, coordinator.State);
        Assert.Equal(UpdateState.Available, coordinator.ButtonState);

        service.NextCheckResult = UpToDate();
        await coordinator.CheckAsync();
        Assert.Equal(UpdateState.UpToDate, coordinator.State);
        Assert.False(coordinator.HasPendingUpdate);
    });

    /// <summary>Ручная проверка показывает и последнюю версию, и «недоступно», и ошибку.</summary>
    [Theory]
    [InlineData("up-to-date", UpdateState.UpToDate)]
    [InlineData("failed", UpdateState.CheckFailed)]
    [InlineData("unsupported", UpdateState.Unavailable)]
    [InlineData("not-configured", UpdateState.Unavailable)]
    [InlineData("available", UpdateState.Available)]
    public Task ManualCheckShowsEveryOutcome(string outcome, UpdateState expected) => Isolated(async () =>
    {
        UpdateCheckResult result = outcome switch
        {
            "up-to-date" => new UpdateCheckResult.UpToDate("1.0.0", "1.0.0", DateTimeOffset.UnixEpoch, "https://example.test"),
            "failed" => new UpdateCheckResult.Failed("offline"),
            "unsupported" => new UpdateCheckResult.UnsupportedPlatform("FreeBSD", "x64"),
            "available" => Available(),
            _ => new UpdateCheckResult.SourceNotConfigured("none")
        };
        using var coordinator = Create(new StubUpdateService { NextCheckResult = result });

        await coordinator.CheckAsync();

        Assert.Equal(expected, coordinator.State);
        Assert.Same(result, coordinator.LastCheckResult);
    });

    /// <summary>Пока ответа нет — «Проверяем…»; отмена возвращает то, что было до проверки.</summary>
    [Fact]
    public Task CancelledCheckReturnsToThePreviousState() => Isolated(async () =>
    {
        var service = new StubUpdateService { NextCheckResult = Available() };
        using var coordinator = Create(service);
        await coordinator.CheckAsync();

        service.PendingCheck = new TaskCompletionSource<UpdateCheckResult>();
        var check = coordinator.CheckAsync();
        Assert.Equal(UpdateState.Checking, coordinator.State);

        coordinator.CancelCheck();
        await check;

        Assert.Equal(UpdateState.Available, coordinator.State);
        Assert.True(coordinator.HasPendingUpdate);
    });

    /// <summary>Во время загрузки и после неё ручная проверка в сеть не ходит.</summary>
    [Fact]
    public Task ManualCheckDoesNothingWhileDownloadingOrAfterDownload() => Isolated(async () =>
    {
        var service = new StubUpdateService { NextCheckResult = Available() };
        using var coordinator = Create(service);
        await coordinator.CheckAsync();

        service.PendingDownload = new TaskCompletionSource<UpdateDownloadResult>();
        var download = coordinator.DownloadAsync();
        await coordinator.CheckAsync();
        Assert.Equal(UpdateState.Downloading, coordinator.State);

        service.PendingDownload.SetResult(new UpdateDownloadResult.Success(Package(), "/tmp/Softmark.dmg"));
        await download;
        await coordinator.CheckAsync();

        Assert.Equal(UpdateState.Downloaded, coordinator.State);
        Assert.Equal(1, service.CheckCount);
    });

    /// <summary>Ход загрузки — в процентах, пока размер известен; без размера — без процентов.</summary>
    [Fact]
    public Task DownloadReportsPercentAndFinishesWithTheFile() => Isolated(async () =>
    {
        var service = new StubUpdateService { NextCheckResult = Available() };
        using var coordinator = Create(service);
        await coordinator.CheckAsync();
        service.PendingDownload = new TaskCompletionSource<UpdateDownloadResult>();

        var download = coordinator.DownloadAsync();
        Assert.Equal(UpdateState.Downloading, coordinator.State);
        Assert.Null(coordinator.DownloadPercent);

        service.LastProgress!.Report(new UpdateDownloadProgress(50, 200));
        Assert.Equal(25, coordinator.DownloadPercent);
        service.LastProgress.Report(new UpdateDownloadProgress(60, null));
        Assert.Null(coordinator.DownloadPercent);

        service.PendingDownload.SetResult(new UpdateDownloadResult.Success(Package(), "/tmp/Softmark.dmg"));
        await download;

        Assert.Equal(UpdateState.Downloaded, coordinator.State);
        Assert.Equal("/tmp/Softmark.dmg", coordinator.DownloadedFilePath);
        Assert.True(coordinator.HasPendingUpdate);
    });

    /// <summary>Загрузка идёт один раз, сколько бы окон и кнопок её ни просили.</summary>
    [Fact]
    public Task DownloadRunsOnce() => Isolated(async () =>
    {
        var service = new StubUpdateService { NextCheckResult = Available() };
        using var coordinator = Create(service);
        await coordinator.CheckAsync();
        service.PendingDownload = new TaskCompletionSource<UpdateDownloadResult>();

        var first = coordinator.DownloadAsync();
        var second = coordinator.DownloadAsync();
        service.PendingDownload.SetResult(new UpdateDownloadResult.Success(Package(), "/tmp/Softmark.dmg"));
        await Task.WhenAll(first, second);

        Assert.Equal(1, service.DownloadCount);
    });

    /// <summary>«Отмена» возвращает к «Доступна версия»: её можно скачать снова.</summary>
    [Fact]
    public Task CancelledDownloadReturnsToAvailable() => Isolated(async () =>
    {
        var service = new StubUpdateService { NextCheckResult = Available() };
        using var coordinator = Create(service);
        await coordinator.CheckAsync();
        service.PendingDownload = new TaskCompletionSource<UpdateDownloadResult>();

        var download = coordinator.DownloadAsync();
        service.LastProgress!.Report(new UpdateDownloadProgress(50, 100));
        coordinator.CancelDownload();
        await download;

        Assert.Equal(UpdateState.Available, coordinator.State);
        Assert.Null(coordinator.DownloadPercent);

        // Отчёт отменённой загрузки, пришедший с опозданием, состояние не трогает.
        service.LastProgress.Report(new UpdateDownloadProgress(90, 100));
        Assert.Null(coordinator.DownloadPercent);
    });

    /// <summary>Обрыв — «Не удалось скачать», и «Повторить» качает заново.</summary>
    [Fact]
    public Task FailedDownloadCanBeRetried() => Isolated(async () =>
    {
        var service = new StubUpdateService
        {
            NextCheckResult = Available(),
            NextDownloadResult = new UpdateDownloadResult.Failed("reset")
        };
        using var coordinator = Create(service);
        await coordinator.CheckAsync();

        await coordinator.DownloadAsync();
        Assert.Equal(UpdateState.DownloadFailed, coordinator.State);
        Assert.Equal("reset", coordinator.LastDownloadFailure?.Message);

        // Найденная версия не пропадает из строки: кнопка ведёт в окно с «Повторить».
        Assert.Equal(UpdateState.Available, coordinator.ButtonState);

        service.NextDownloadResult = new UpdateDownloadResult.Success(Package(), "/tmp/Softmark.dmg");
        await coordinator.DownloadAsync();

        Assert.Equal(UpdateState.Downloaded, coordinator.State);
        Assert.Equal(2, service.DownloadCount);
    });

    /// <summary>Выход молча отменяет идущую загрузку.</summary>
    [Fact]
    public Task ExitCancelsTheRunningDownload() => Isolated(async () =>
    {
        var service = new StubUpdateService { NextCheckResult = Available() };
        var coordinator = Create(service);
        await coordinator.CheckAsync();
        service.PendingDownload = new TaskCompletionSource<UpdateDownloadResult>();
        var download = coordinator.DownloadAsync();

        coordinator.Dispose();
        await download;

        Assert.True(service.LastDownloadToken.IsCancellationRequested);
    });

    /// <summary>Скачанное открывается установкой ОС и показывается в файловом менеджере.</summary>
    [Fact]
    public Task DownloadedFileOpensAndReveals() => Isolated(async () =>
    {
        var service = new StubUpdateService
        {
            NextCheckResult = Available(),
            NextDownloadResult = new UpdateDownloadResult.Success(Package(), "/tmp/Softmark.dmg"),
            NextPrepareResult = new UpdatePrepareResult.Failed("busy")
        };
        var platform = new FakePlatformServices();
        using var coordinator = new UpdateCoordinator(service, platform);
        await coordinator.CheckAsync();
        await coordinator.DownloadAsync();

        Assert.False(await coordinator.OpenDownloadedAsync());
        Assert.Equal(1, service.PrepareCount);
        Assert.True(coordinator.OpenDownloadedFailed);

        service.NextPrepareResult = new UpdatePrepareResult.Success("opened");
        Assert.True(await coordinator.OpenDownloadedAsync());
        Assert.False(coordinator.OpenDownloadedFailed);

        await coordinator.RevealDownloadedAsync();
        Assert.Equal(["/tmp/Softmark.dmg"], platform.RevealedPaths);
    });

    /// <summary>
    /// Скачанного файла больше нет: «Скачано» не становится тупиком — обновление снова
    /// доступно, и его можно скачать заново.
    /// </summary>
    [Fact]
    public Task MissingDownloadedFileMakesTheUpdateAvailableAgain() => Isolated(async () =>
    {
        var service = new StubUpdateService
        {
            NextCheckResult = Available(),
            NextDownloadResult = new UpdateDownloadResult.Success(Package(), "/tmp/Softmark.dmg"),
            NextPrepareResult = new UpdatePrepareResult.FileNotFound("/tmp/Softmark.dmg")
        };
        using var coordinator = Create(service);
        await coordinator.CheckAsync();
        await coordinator.DownloadAsync();

        Assert.False(await coordinator.OpenDownloadedAsync());

        Assert.Equal(UpdateState.Available, coordinator.State);
        Assert.Null(coordinator.DownloadedFilePath);
        Assert.False(coordinator.OpenDownloadedFailed);

        await coordinator.DownloadAsync();
        Assert.Equal(2, service.DownloadCount);
    });

    internal static UpdateCheckResult.UpToDate UpToDate()
        => new("1.0.0", "1.0.0", DateTimeOffset.UnixEpoch, "https://example.test");

    internal static AppUpdatePackage Package(AppUpdateInstallAction action = AppUpdateInstallAction.OpenDiskImage)
        => new(
            CurrentVersion: "1.0.0",
            ReleaseVersion: "1.2.3",
            ReleaseTag: "v1.2.3",
            PublishedAt: DateTimeOffset.UnixEpoch,
            ReleasePageUrl: "https://github.com/kostyatab/Softmark/releases/tag/v1.2.3",
            AssetName: "Softmark-macos-arm64.dmg",
            DownloadUrl: "https://github.com/kostyatab/Softmark/releases/download/v1.2.3/Softmark-macos-arm64.dmg",
            PlatformName: "macOS",
            ArchitectureName: "arm64",
            InstallAction: action);

    internal static UpdateCheckResult.UpdateAvailable Available(AppUpdateInstallAction action = AppUpdateInstallAction.OpenDiskImage)
        => new(Package(action));

    private static UpdateCoordinator Create(StubUpdateService service, TimeSpan? delay = null)
        => new(service, new FakePlatformServices(), new DeferredUpdateCheck(service, delay ?? TimeSpan.FromMinutes(1)));

    /// <summary>
    /// Тело теста — вне контекста синхронизации xUnit: отчёты о ходе загрузки применяются сразу,
    /// а не уходят в очередь контекста.
    /// </summary>
    internal static Task Isolated(Func<Task> test) => Task.Run(test);
}
