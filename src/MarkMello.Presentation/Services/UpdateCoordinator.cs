using CommunityToolkit.Mvvm.ComponentModel;
using MarkMello.Application.Abstractions;
using MarkMello.Application.Updates;

namespace MarkMello.Presentation.Services;

/// <summary>Состояние обновления приложения (ADR-0004, «Update Model»).</summary>
public enum UpdateState
{
    /// <summary>Ещё не проверяли, или фоновая проверка ничего не нашла.</summary>
    Idle,
    Checking,
    Available,
    UpToDate,

    /// <summary>Источник релизов не настроен, или платформы нет в матрице релизов.</summary>
    Unavailable,
    CheckFailed,
    Downloading,
    Downloaded,
    DownloadFailed
}

/// <summary>
/// Одно состояние обновления на всё приложение (ADR-0004, «Update Model»): кнопки в строках
/// всех окон и окно обновления показывают одно и то же, а загрузка идёт один раз. Здесь же
/// фоновая проверка — одна за запуск, через 30 с после открытия первого окна.
/// <para>
/// Методы вызываются из UI-потока, и продолжения возвращаются в него же: подписчики —
/// привязки окон — получают изменения там, где им можно трогать контролы. Создаётся без
/// ввода-вывода: быстрый путь открытия документа не дорожает.
/// </para>
/// </summary>
public sealed class UpdateCoordinator : ObservableObject, IDisposable
{
    /// <summary>Сколько выход ждёт, пока отменённая загрузка удалит недокачанный файл.</summary>
    private static readonly TimeSpan ShutdownDownloadCleanupTimeout = TimeSpan.FromSeconds(1);

    private readonly IUpdateService _updateService;
    private readonly IPlatformServices _platform;
    private readonly DeferredUpdateCheck _deferredCheck;

    private UpdateState _state;
    private UpdateState _stateBeforeCheck;
    private AppUpdatePackage? _package;
    private AppUpdatePackage? _pendingPackage;
    private UpdateCheckResult? _lastCheckResult;
    private string? _downloadedFilePath;
    private UpdateDownloadResult.Failed? _lastDownloadFailure;
    private int? _downloadPercent;
    private bool _openDownloadedFailed;

    private bool _backgroundCheckStarted;
    private bool _isDisposed;
    private CancellationTokenSource? _checkCancellation;
    private CancellationTokenSource? _downloadCancellation;
    private Task? _downloadTask;

    public UpdateCoordinator(IUpdateService updateService, IPlatformServices platform)
        : this(updateService, platform, new DeferredUpdateCheck(updateService))
    {
    }

    internal UpdateCoordinator(IUpdateService updateService, IPlatformServices platform, DeferredUpdateCheck deferredCheck)
    {
        ArgumentNullException.ThrowIfNull(updateService);
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(deferredCheck);

        _updateService = updateService;
        _platform = platform;
        _deferredCheck = deferredCheck;
    }

    /// <summary>Состояние окна обновления. Его меняют только действия пользователя.</summary>
    public UpdateState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                NotifyButtonChanged();
            }
        }
    }

    /// <summary>Найденная новая версия, которую показывает окно; остаётся на время загрузки и после неё.</summary>
    public AppUpdatePackage? Package
    {
        get => _package;
        private set
        {
            if (SetProperty(ref _package, value))
            {
                NotifyButtonChanged();
            }
        }
    }

    /// <summary>
    /// Новая версия, известная помимо окна: её нашла фоновая проверка, или она была найдена
    /// до ручной проверки, которая не дошла до GitHub. Окно само на неё не переключается —
    /// её показывает кнопка в строке, а нажатие на кнопку выводит её в окно.
    /// </summary>
    private AppUpdatePackage? PendingPackage
    {
        get => _pendingPackage;
        set
        {
            if (!ReferenceEquals(_pendingPackage, value))
            {
                _pendingPackage = value;
                NotifyButtonChanged();
            }
        }
    }

    /// <summary>
    /// Что показывает кнопка в строке: <see cref="UpdateState.Available"/> — стрелку,
    /// <see cref="UpdateState.Downloading"/> — кольцо, <see cref="UpdateState.Downloaded"/> —
    /// галочку, <see cref="UpdateState.Idle"/> — кнопки нет. Кнопка остаётся, пока известна
    /// новая версия: и после «Позже», и после неудачной загрузки, и когда её нашла фоновая
    /// проверка, а окно показывает другое.
    /// </summary>
    public UpdateState ButtonState => State switch
    {
        UpdateState.Downloading or UpdateState.Downloaded => State,
        UpdateState.Available or UpdateState.DownloadFailed => UpdateState.Available,
        _ when ButtonPackage is not null => UpdateState.Available,
        _ => UpdateState.Idle
    };

    public bool HasPendingUpdate => ButtonState != UpdateState.Idle;

    /// <summary>Версия, о которой говорит кнопка.</summary>
    public AppUpdatePackage? ButtonPackage => State switch
    {
        UpdateState.Available or UpdateState.Downloading or UpdateState.Downloaded or UpdateState.DownloadFailed => Package,
        // Во время ручной проверки кнопка не мигает: остаётся версия, известная до неё.
        UpdateState.Checking => PendingPackage ?? Package,
        _ => PendingPackage
    };

    /// <summary>
    /// Ответ последней проверки: из него окно берёт версии для «последней версии» и причину
    /// для «обновления недоступны».
    /// </summary>
    public UpdateCheckResult? LastCheckResult
    {
        get => _lastCheckResult;
        private set => SetProperty(ref _lastCheckResult, value);
    }

    public string? DownloadedFilePath
    {
        get => _downloadedFilePath;
        private set => SetProperty(ref _downloadedFilePath, value);
    }

    /// <summary>Почему не скачалось в последний раз: окно отличает обрыв связи от прочего.</summary>
    public UpdateDownloadResult.Failed? LastDownloadFailure
    {
        get => _lastDownloadFailure;
        private set => SetProperty(ref _lastDownloadFailure, value);
    }

    /// <summary>Проценты загрузки или <c>null</c>, пока размер неизвестен.</summary>
    public int? DownloadPercent
    {
        get => _downloadPercent;
        private set => SetProperty(ref _downloadPercent, value);
    }

    /// <summary>Скачанный файл не открылся — окно говорит об этом вместо подсказки.</summary>
    public bool OpenDownloadedFailed
    {
        get => _openDownloadedFailed;
        private set => SetProperty(ref _openDownloadedFailed, value);
    }

    /// <summary>
    /// Запускает фоновую проверку — один раз за запуск, сколько бы окон ни открылось.
    /// Показывает только найденную новую версию, и только кнопкой: окно на неё само не
    /// переключается. Последняя версия, нет сети или платформа не поддерживается — молча.
    /// </summary>
    public void StartBackgroundCheck()
    {
        if (_backgroundCheckStarted || _isDisposed)
        {
            return;
        }

        _backgroundCheckStarted = true;
        BackgroundCheck = RunBackgroundCheckAsync();
    }

    /// <summary>Фоновая проверка, если уже запущена; тесты ждут её завершения.</summary>
    internal Task? BackgroundCheck { get; private set; }

    private async Task RunBackgroundCheckAsync()
    {
        UpdateCheckResult result;
        try
        {
            result = await _deferredCheck.WaitAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (_isDisposed
            || result is not UpdateCheckResult.UpdateAvailable available
            || State is UpdateState.Available or UpdateState.Downloading or UpdateState.Downloaded or UpdateState.DownloadFailed)
        {
            return;
        }

        // Окно — ответ на действие пользователя, поэтому состояние не трогаем: кнопка появится,
        // а окно, если открыто, продолжит показывать своё до нажатия на кнопку.
        PendingPackage = available.Package;
    }

    /// <summary>
    /// Нажатие на кнопку, когда окно показывает не найденную версию: выводит её в окно,
    /// чтобы из него можно было скачать. В остальных состояниях ничего не меняет.
    /// </summary>
    public void ShowPendingUpdate()
    {
        if (PendingPackage is not { } package
            || State is not (UpdateState.Idle or UpdateState.UpToDate or UpdateState.Unavailable or UpdateState.CheckFailed))
        {
            return;
        }

        LastCheckResult = new UpdateCheckResult.UpdateAvailable(package);
        Package = package;
        PendingPackage = null;
        State = UpdateState.Available;
    }

    /// <summary>
    /// Ручная проверка из меню. Пока идёт загрузка или файл уже скачан, новой проверки нет:
    /// окно показывает текущее состояние.
    /// </summary>
    public async Task CheckAsync()
    {
        if (_isDisposed || State is UpdateState.Checking or UpdateState.Downloading or UpdateState.Downloaded)
        {
            return;
        }

        _stateBeforeCheck = State;

        using var cancellation = new CancellationTokenSource();
        _checkCancellation = cancellation;
        State = UpdateState.Checking;

        UpdateCheckResult result;
        try
        {
            result = await _updateService.CheckForUpdatesAsync(cancellation.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Отменённая проверка ничего не меняет: кнопка и окно возвращаются к тому, что было.
            if (!_isDisposed)
            {
                State = _stateBeforeCheck;
            }

            return;
        }
        catch (Exception exception)
        {
            result = new UpdateCheckResult.Failed(exception.Message, exception is HttpRequestException or TimeoutException);
        }
        finally
        {
            _checkCancellation = null;
        }

        if (_isDisposed)
        {
            return;
        }

        ApplyCheckResult(result);
    }

    public void CancelCheck() => _checkCancellation?.Cancel();

    /// <summary>
    /// Ответ ручной проверки важнее всего, что было известно до него: «последняя версия» и
    /// «недоступно» убирают и кнопку. Неудачная проверка ничего нового не узнала — найденная
    /// раньше версия остаётся на кнопке.
    /// </summary>
    private void ApplyCheckResult(UpdateCheckResult result)
    {
        LastCheckResult = result;
        switch (result)
        {
            case UpdateCheckResult.UpdateAvailable available:
                PendingPackage = null;
                Package = available.Package;
                State = UpdateState.Available;
                break;

            case UpdateCheckResult.UpToDate:
                PendingPackage = null;
                Package = null;
                State = UpdateState.UpToDate;
                break;

            case UpdateCheckResult.SourceNotConfigured or UpdateCheckResult.UnsupportedPlatform:
                PendingPackage = null;
                Package = null;
                State = UpdateState.Unavailable;
                break;

            default:
                PendingPackage = Package ?? PendingPackage;
                Package = null;
                State = UpdateState.CheckFailed;
                break;
        }
    }

    /// <summary>
    /// Скачивает найденную версию. Закрытие окна загрузку не прерывает — её отменяет только
    /// «Отмена» или выход из приложения.
    /// </summary>
    public async Task DownloadAsync()
    {
        if (_isDisposed
            || Package is not { } package
            || State is not (UpdateState.Available or UpdateState.DownloadFailed))
        {
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _downloadCancellation = cancellation;
        DownloadPercent = null;
        OpenDownloadedFailed = false;
        State = UpdateState.Downloading;

        UpdateDownloadResult result;
        try
        {
            var progress = new DispatchedProgress(this, cancellation.Token);
            var download = _updateService.DownloadUpdateAsync(package, progress, cancellation.Token);
            _downloadTask = download;
            result = await download.ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Недокачанный файл удалил сервис; снова предлагаем скачать.
            if (!_isDisposed)
            {
                DownloadPercent = null;
                State = UpdateState.Available;
            }

            return;
        }
        catch (Exception exception)
        {
            result = new UpdateDownloadResult.Failed(exception.Message, exception is HttpRequestException or TimeoutException);
        }
        finally
        {
            _downloadCancellation = null;
            _downloadTask = null;
        }

        if (_isDisposed)
        {
            return;
        }

        switch (result)
        {
            case UpdateDownloadResult.Success success:
                Package = success.Package;
                DownloadedFilePath = success.DownloadedFilePath;
                State = UpdateState.Downloaded;
                break;

            case UpdateDownloadResult.Failed failed:
                DownloadPercent = null;
                LastDownloadFailure = failed;
                State = UpdateState.DownloadFailed;
                break;
        }
    }

    public void CancelDownload() => _downloadCancellation?.Cancel();

    /// <summary>
    /// Передаёт скачанный файл установке ОС: открыть DMG, запустить установщик, показать
    /// AppImage. Возвращает, удалось ли. Если файла больше нет — его удалили или перенесли, —
    /// обновление снова «доступно»: его можно скачать заново, а не застрять в «Скачано».
    /// </summary>
    public async Task<bool> OpenDownloadedAsync()
    {
        if (State != UpdateState.Downloaded || Package is not { } package || DownloadedFilePath is not { } path)
        {
            return false;
        }

        var result = await _updateService.PrepareDownloadedUpdateAsync(package, path).ConfigureAwait(true);
        switch (result)
        {
            case UpdatePrepareResult.Success:
                OpenDownloadedFailed = false;
                return true;

            case UpdatePrepareResult.FileNotFound:
                OpenDownloadedFailed = false;
                DownloadedFilePath = null;
                State = UpdateState.Available;
                return false;

            default:
                OpenDownloadedFailed = true;
                return false;
        }
    }

    /// <summary>Показывает скачанный файл в Finder или Проводнике.</summary>
    public async Task RevealDownloadedAsync()
    {
        if (State != UpdateState.Downloaded || DownloadedFilePath is not { } path)
        {
            return;
        }

        await _platform.RevealInFileManagerAsync(path).ConfigureAwait(true);
    }

    private void NotifyButtonChanged()
    {
        OnPropertyChanged(nameof(ButtonState));
        OnPropertyChanged(nameof(ButtonPackage));
        OnPropertyChanged(nameof(HasPendingUpdate));
    }

    private void ReportDownloadProgress(UpdateDownloadProgress progress)
    {
        if (State != UpdateState.Downloading)
        {
            return;
        }

        DownloadPercent = progress.Fraction is { } fraction ? (int)Math.Floor(fraction * 100) : null;
    }

    /// <summary>
    /// Выход молча отменяет фоновую проверку, ручную проверку и загрузку. Отменённую загрузку
    /// ждём недолго: за это время сервис удаляет недокачанный файл, иначе он остался бы
    /// в «Загрузках».
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _deferredCheck.Dispose();
        _checkCancellation?.Cancel();

        if (_downloadCancellation is { } downloadCancellation && _downloadTask is { } download)
        {
            downloadCancellation.Cancel();
            try
            {
                download.Wait(ShutdownDownloadCleanupTimeout);
            }
            catch (AggregateException)
            {
                // Отмена приходит исключением — это и есть ожидаемый конец загрузки.
            }
        }
    }

    /// <summary>
    /// Ход загрузки из потока сети переносится в поток, где загрузку начали, — в UI.
    /// Без контекста синхронизации (тесты) отчёт применяется сразу. Отчёты после отмены
    /// отбрасываются: загрузки уже нет.
    /// </summary>
    private sealed class DispatchedProgress : IProgress<UpdateDownloadProgress>
    {
        private readonly UpdateCoordinator _owner;
        private readonly CancellationToken _cancellation;
        private readonly SynchronizationContext? _context = SynchronizationContext.Current;

        public DispatchedProgress(UpdateCoordinator owner, CancellationToken cancellation)
        {
            _owner = owner;
            _cancellation = cancellation;
        }

        public void Report(UpdateDownloadProgress value)
        {
            if (_context is null)
            {
                Apply(value);
                return;
            }

            _context.Post(static state =>
            {
                var (progress, value) = ((DispatchedProgress, UpdateDownloadProgress))state!;
                progress.Apply(value);
            }, (this, value));
        }

        private void Apply(UpdateDownloadProgress value)
        {
            if (!_cancellation.IsCancellationRequested)
            {
                _owner.ReportDownloadProgress(value);
            }
        }
    }
}
