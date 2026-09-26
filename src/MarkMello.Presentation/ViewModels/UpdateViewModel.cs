using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkMello.Application.Updates;
using MarkMello.Presentation.Localization;
using MarkMello.Presentation.Services;

namespace MarkMello.Presentation.ViewModels;

/// <summary>
/// Подписи и действия обновления поверх <see cref="UpdateCoordinator"/>: окно обновления и
/// кнопка в строке каждого окна (ADR-0004, «Update Model»). Один на приложение, как и само
/// состояние: окна и кнопки привязаны к нему напрямую, своих копий состояния у них нет.
/// </summary>
public sealed partial class UpdateViewModel : ObservableObject
{
    private readonly UpdateCoordinator _coordinator;
    private readonly ILocalizationService _localization;
    private readonly IWindowLauncher _windowLauncher;

    public UpdateViewModel(UpdateCoordinator coordinator, ILocalizationService localization, IWindowLauncher windowLauncher)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentNullException.ThrowIfNull(windowLauncher);

        _coordinator = coordinator;
        _localization = localization;
        _windowLauncher = windowLauncher;

        // Оба источника — синглтоны, как и этот объект: подписки живут столько же, сколько приложение.
        _coordinator.PropertyChanged += OnSourceChanged;
        _localization.PropertyChanged += OnSourceChanged;
    }

    /// <summary>Действия окна просят его закрыться: «Позже», «OK», «Закрыть», отмена проверки.</summary>
    public event EventHandler? CloseRequested;

    public UpdateCoordinator Coordinator => _coordinator;

    public UpdateState State => _coordinator.State;

    private AppUpdatePackage? Package => _coordinator.Package;

    private string ReleaseVersion => Package?.ReleaseVersion ?? string.Empty;

    /// <summary>Фоновая проверка — из <c>Opened</c> главного окна; сама следит, чтобы быть одной.</summary>
    public void StartBackgroundCheck() => _coordinator.StartBackgroundCheck();

    // ---------- Окно обновления ----------

    public string WindowTitle => _localization["UpdateWindowTitle"];

    public string Title => State switch
    {
        UpdateState.Available => _localization.Format("UpdateAvailableTitle", ReleaseVersion),
        UpdateState.Downloading => _localization.Format("UpdateDownloadingTitle", ReleaseVersion),
        UpdateState.Downloaded => _localization.Format("UpdateDownloadedTitle", ReleaseVersion),
        UpdateState.UpToDate => _localization["UpdateUpToDateTitle"],
        UpdateState.Unavailable => _localization["UpdateUnavailableTitle"],
        UpdateState.CheckFailed => _localization["UpdateCheckFailedTitle"],
        UpdateState.DownloadFailed => _localization["UpdateDownloadFailedTitle"],
        _ => _localization["UpdateCheckingTitle"]
    };

    public string Message => State switch
    {
        UpdateState.Available when Package is { } package => _localization.Format(
            package.InstallAction switch
            {
                AppUpdateInstallAction.OpenDiskImage => "UpdateAvailableMessageDmg",
                AppUpdateInstallAction.LaunchInstaller => "UpdateAvailableMessageInstaller",
                _ => "UpdateAvailableMessageAppImage"
            },
            package.CurrentVersion,
            package.AssetName),
        UpdateState.Downloading => DownloadCaption,
        UpdateState.Downloaded when _coordinator.OpenDownloadedFailed => _localization.Format(
            "UpdateOpenFailedMessage",
            Path.GetFileName(_coordinator.DownloadedFilePath) ?? string.Empty),
        UpdateState.Downloaded => _localization[Package?.InstallAction switch
        {
            AppUpdateInstallAction.OpenDiskImage => "UpdateDownloadedMessageDmg",
            AppUpdateInstallAction.LaunchInstaller => "UpdateDownloadedMessageInstaller",
            _ => "UpdateDownloadedMessageAppImage"
        }],
        UpdateState.UpToDate => _localization.Format(
            "UpdateUpToDateMessage",
            (_coordinator.LastCheckResult as UpdateCheckResult.UpToDate)?.CurrentVersion ?? AppProductInfo.GetVersion()),
        UpdateState.Unavailable => _coordinator.LastCheckResult is UpdateCheckResult.UnsupportedPlatform unsupported
            ? _localization.Format("UpdateUnsupportedPlatformMessage", unsupported.PlatformName, unsupported.ArchitectureName)
            : _localization["UpdateSourceNotConfiguredMessage"],
        UpdateState.CheckFailed => _localization[_coordinator.LastCheckResult is UpdateCheckResult.Failed { IsConnectionProblem: false }
            ? "UpdateCheckFailedServiceMessage"
            : "UpdateCheckFailedMessage"],
        UpdateState.DownloadFailed => _localization[_coordinator.LastDownloadFailure is { IsConnectionProblem: false }
            ? "UpdateDownloadFailedOtherMessage"
            : "UpdateDownloadFailedMessage"],
        _ => _localization["UpdateCheckingMessage"]
    };

    /// <summary>Имя файла и проценты под полосой загрузки: «Softmark-macos-arm64.dmg · 62 %».</summary>
    private string DownloadCaption => _coordinator.DownloadPercent is { } percent
        ? _localization.Format("UpdateDownloadCaption", Package?.AssetName ?? string.Empty, percent)
        : Package?.AssetName ?? string.Empty;

    /// <summary>Кольцо на месте значка, пока идёт проверка.</summary>
    public bool ShowsCheckingRing => State is UpdateState.Checking or UpdateState.Idle;

    public bool ShowsAvailableBadge => State == UpdateState.Available;

    public bool ShowsSuccessBadge => State is UpdateState.Downloaded or UpdateState.UpToDate;

    public bool ShowsAlertBadge => State is UpdateState.CheckFailed or UpdateState.DownloadFailed;

    public bool ShowsInfoBadge => State == UpdateState.Unavailable;

    public bool ShowsProgress => State == UpdateState.Downloading;

    /// <summary>Пока размер неизвестен, полоса бежит, а не заполняется.</summary>
    public bool IsProgressIndeterminate => _coordinator.DownloadPercent is null;

    public double ProgressValue => _coordinator.DownloadPercent ?? 0;

    public bool ShowsWhatsNew => State == UpdateState.Available && Package is not null;

    public string WhatsNewLabel => _localization.Format("UpdateWhatsNew", ReleaseVersion);

    public string WhatsNewUrl => Package?.ReleasePageUrl ?? string.Empty;

    public bool ShowsPrimaryAction => PrimaryAction != UpdateAction.None;

    public string PrimaryActionLabel => ActionLabel(PrimaryAction);

    public bool ShowsSecondaryAction => SecondaryAction != UpdateAction.None;

    public string SecondaryActionLabel => ActionLabel(SecondaryAction);

    /// <summary>Основная кнопка окна — справа, залитая.</summary>
    private UpdateAction PrimaryAction => State switch
    {
        UpdateState.Available => UpdateAction.Download,
        UpdateState.Downloaded => UpdateAction.OpenDownloaded,
        UpdateState.UpToDate or UpdateState.Unavailable => UpdateAction.Ok,
        UpdateState.CheckFailed => UpdateAction.RetryCheck,
        UpdateState.DownloadFailed => UpdateAction.RetryDownload,
        _ => UpdateAction.None
    };

    /// <summary>Вторичная кнопка — слева от основной; у проверки и загрузки она единственная.</summary>
    private UpdateAction SecondaryAction => State switch
    {
        UpdateState.Checking or UpdateState.Idle => UpdateAction.CancelCheck,
        UpdateState.Available => UpdateAction.Later,
        UpdateState.Downloading => UpdateAction.CancelDownload,
        UpdateState.Downloaded when Package?.InstallAction is AppUpdateInstallAction.OpenDiskImage or AppUpdateInstallAction.LaunchInstaller
            => UpdateAction.RevealDownloaded,
        UpdateState.CheckFailed or UpdateState.DownloadFailed => UpdateAction.Close,
        _ => UpdateAction.None
    };

    private string ActionLabel(UpdateAction action) => action switch
    {
        UpdateAction.CancelCheck or UpdateAction.CancelDownload => _localization["UpdateCancel"],
        UpdateAction.Later => _localization["UpdateLater"],
        UpdateAction.Download => _localization["UpdateDownload"],
        UpdateAction.OpenDownloaded => _localization[InstallActionKey],
        UpdateAction.RevealDownloaded => _localization[Package?.InstallAction == AppUpdateInstallAction.OpenDiskImage
            ? "UpdateShowInFinder"
            : "UpdateShowInExplorer"],
        UpdateAction.Ok => _localization["UpdateOk"],
        UpdateAction.Close => _localization["UpdateClose"],
        UpdateAction.RetryCheck or UpdateAction.RetryDownload => _localization["UpdateTryAgain"],
        _ => string.Empty
    };

    private string InstallActionKey => Package?.InstallAction switch
    {
        AppUpdateInstallAction.OpenDiskImage => "UpdateOpenDmg",
        AppUpdateInstallAction.LaunchInstaller => "UpdateRunInstaller",
        _ => "UpdateShowAppImage"
    };

    [RelayCommand]
    private Task PrimaryActionAsync() => RunAsync(PrimaryAction);

    [RelayCommand]
    private Task SecondaryActionAsync() => RunAsync(SecondaryAction);

    private Task RunAsync(UpdateAction action)
    {
        switch (action)
        {
            case UpdateAction.CancelCheck:
                _coordinator.CancelCheck();
                CloseRequested?.Invoke(this, EventArgs.Empty);
                return Task.CompletedTask;

            case UpdateAction.Later or UpdateAction.Ok or UpdateAction.Close:
                CloseRequested?.Invoke(this, EventArgs.Empty);
                return Task.CompletedTask;

            case UpdateAction.Download or UpdateAction.RetryDownload:
                return _coordinator.DownloadAsync();

            case UpdateAction.CancelDownload:
                _coordinator.CancelDownload();
                return Task.CompletedTask;

            case UpdateAction.OpenDownloaded:
                return _coordinator.OpenDownloadedAsync();

            case UpdateAction.RevealDownloaded:
                return _coordinator.RevealDownloadedAsync();

            case UpdateAction.RetryCheck:
                return _coordinator.CheckAsync();

            default:
                return Task.CompletedTask;
        }
    }

    // ---------- Кнопка в строке окна ----------

    /// <summary>
    /// Кнопка видна, пока есть новая версия: найдена, качается или скачана. Содержимое для
    /// <c>ContentControl</c> строки — сам этот объект, пока кнопка нужна, иначе ничего: сама
    /// кнопка создаётся только тогда, а не на старте каждого окна.
    /// </summary>
    public bool IsButtonVisible => _coordinator.HasPendingUpdate;

    public object? ButtonContent => IsButtonVisible ? this : null;

    private UpdateState ButtonState => _coordinator.ButtonState;

    private string ButtonVersion => _coordinator.ButtonPackage?.ReleaseVersion ?? string.Empty;

    public bool IsButtonDownloading => ButtonState == UpdateState.Downloading;

    public bool ShowsButtonArrow => ButtonState == UpdateState.Available;

    public bool ShowsButtonCheck => ButtonState == UpdateState.Downloaded;

    /// <summary>Доля для кольца на кнопке; <c>null</c> — размер неизвестен, кольцо крутится.</summary>
    public double? ButtonProgress => _coordinator.DownloadPercent is { } percent ? percent / 100d : null;

    /// <summary>Надпись, которая выезжает при наведении: «Обновить», «Загрузка · 62 %», «Установить».</summary>
    public string ButtonLabel => ButtonState switch
    {
        UpdateState.Downloading => _coordinator.DownloadPercent is { } percent
            ? _localization.Format("UpdateButtonDownloading", percent)
            : _localization["UpdateButtonDownloadingUnknown"],
        UpdateState.Downloaded => _localization["UpdateButtonInstall"],
        _ => _localization["UpdateButtonUpdate"]
    };

    /// <summary>Имя для экранного диктора — с версией, которой нет в короткой надписи.</summary>
    public string ButtonAutomationName => ButtonState switch
    {
        UpdateState.Downloading => ButtonLabel,
        UpdateState.Downloaded => _localization.Format("UpdateButtonInstallName", ButtonVersion),
        _ => _localization.Format("UpdateButtonUpdateName", ButtonVersion)
    };

    /// <summary>
    /// Скачанное — сразу к установке ОС. Если установка не началась — файл пропал или не
    /// открылся, — открывается окно: оно говорит, что случилось, и даёт скачать заново.
    /// В остальных состояниях кнопка открывает окно обновления без новой проверки, выводя в
    /// него версию, которую она показывает.
    /// </summary>
    [RelayCommand]
    private async Task ButtonAsync()
    {
        if (ButtonState == UpdateState.Downloaded
            && await _coordinator.OpenDownloadedAsync().ConfigureAwait(true))
        {
            return;
        }

        _coordinator.ShowPendingUpdate();
        _windowLauncher.ShowUpdates(startCheck: false);
    }

    /// <summary>
    /// Любое изменение состояния или языка пересчитывает все подписи: их немного, а
    /// перечислять зависимости каждой — место для ошибки.
    /// </summary>
    private void OnSourceChanged(object? sender, PropertyChangedEventArgs e) => OnPropertyChanged(string.Empty);

    private enum UpdateAction
    {
        None,
        CancelCheck,
        Later,
        Download,
        CancelDownload,
        OpenDownloaded,
        RevealDownloaded,
        Ok,
        Close,
        RetryCheck,
        RetryDownload
    }
}
