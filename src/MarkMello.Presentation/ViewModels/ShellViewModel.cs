using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkMello.Application.Abstractions;
using MarkMello.Application.Updates;
using MarkMello.Application.UseCases;
using MarkMello.Domain;
using MarkMello.Domain.Diagnostics;
using MarkMello.Presentation.Editing;
using MarkMello.Presentation.Localization;
using MarkMello.Presentation.Services;
using System.ComponentModel;
using System.Windows.Input;

namespace MarkMello.Presentation.ViewModels;

/// <summary>
/// View model главного окна. Отвечает за state machine (NoDocument/Viewing/LoadError),
/// тему, reading preferences, команды open/reload, lazy edit mode и dirty/save flow.
/// </summary>
public partial class ShellViewModel : ObservableObject
{
    private readonly OpenDocumentUseCase _openDocument;
    private readonly SaveDocumentUseCase _saveDocument;
    private readonly IFilePicker _filePicker;
    private readonly ICommandLineActivation _commandLine;
    private readonly ILocalizationService _localization;
    private readonly ISettingsStore _settings;
    private readonly IThemeService _themeService;
    private readonly IStartupMetrics _startupMetrics;
    private readonly RenderMarkdownDocumentUseCase _renderMarkdown;
    private readonly OpenFolderUseCase _openFolder;
    private readonly ExpandFolderNodeUseCase _expandFolderNode;
    private readonly SearchWorkspaceFilesUseCase _searchWorkspaceFiles;
    private readonly WorkspaceFileOperationsUseCase _fileOperations;
    private readonly IPlatformServices _platform;
    private readonly Func<IWorkspaceWatcher> _watcherFactory;
    private readonly IWindowLauncher _windowLauncher;
    private readonly RecentItemsUseCase? _recentItems;

    /// <summary>
    /// Проверка существования пути при восстановлении сессии. Отдельно от файловой системы
    /// дерева, потому что нужна и без открытой папки; в тестах подменяется.
    /// </summary>
    private readonly Func<string, bool> _fileExists;
    private readonly IUpdateService _updateService;
    private readonly IImageSourceResolver? _imageSourceResolver;
    private readonly Func<IEditorPreviewScheduler>? _previewSchedulerFactory;

    private bool _documentModelReadyMarked;
    private bool _readableDocumentMarked;
    private bool _secondaryFeaturesMarked;
    private bool _editorActivationMarked;
    private string? _currentPath;
    private Func<Task>? _pendingDirtyAction;

    /// <summary>
    /// Вкладка, о правках которой спрашивает диалог. Ответ относится к ней, а не к той,
    /// что окажется активной к моменту нажатия кнопки.
    /// </summary>
    private DocumentTabViewModel? _dirtyPromptTab;

    /// <summary>
    /// Файлы, которые ОС попросила открыть под диалогом: открытие сменило бы активную вкладку
    /// под вопросом. Очередь заводится только тогда и живёт в памяти.
    /// </summary>
    private Queue<string>? _deferredActivationPaths;

    private readonly bool _showCustomTitleBar = OperatingSystem.IsWindows();
    private readonly bool _showsAboutMenuItem = !OperatingSystem.IsMacOS();
    private readonly string _aboutVersion;
    private readonly string _aboutLicense = AppProductInfo.License;
    private AppUpdatePackage? _availableUpdatePackage;
    private ReadingPreferences _documentReadingPreferences = GetDocumentRenderingPreferences(ReadingPreferences.Default);
    private WindowBorderMode _windowBorderMode = WindowBorderMode.Auto;
    private bool _isWindowBorderLoaded;

    public event EventHandler? CloseRequested;

    public ShellViewModel(
        OpenDocumentUseCase openDocument,
        SaveDocumentUseCase saveDocument,
        IFilePicker filePicker,
        ICommandLineActivation commandLine,
        ILocalizationService localization,
        ISettingsStore settings,
        IThemeService themeService,
        IStartupMetrics startupMetrics,
        RenderMarkdownDocumentUseCase renderMarkdown,
        IUpdateService updateService,
        OpenFolderUseCase openFolder,
        ExpandFolderNodeUseCase expandFolderNode,
        SearchWorkspaceFilesUseCase searchWorkspaceFiles,
        WorkspaceFileOperationsUseCase fileOperations,
        IPlatformServices platform,
        Func<IWorkspaceWatcher> watcherFactory,
        IWindowLauncher windowLauncher,
        Func<string, bool>? fileExists = null,
        IImageSourceResolver? imageSourceResolver = null,
        Func<IEditorPreviewScheduler>? previewSchedulerFactory = null,
        RecentItemsUseCase? recentItems = null)
    {
        _openDocument = openDocument;
        _saveDocument = saveDocument;
        _filePicker = filePicker;
        _commandLine = commandLine;
        _localization = localization;
        _settings = settings;
        _themeService = themeService;
        _startupMetrics = startupMetrics;
        _renderMarkdown = renderMarkdown;
        _updateService = updateService;
        _openFolder = openFolder;
        _expandFolderNode = expandFolderNode;
        _searchWorkspaceFiles = searchWorkspaceFiles;
        _fileOperations = fileOperations;
        _platform = platform;
        _watcherFactory = watcherFactory;
        _windowLauncher = windowLauncher;
        _fileExists = fileExists ?? (static path => File.Exists(path) || Directory.Exists(path));
        _imageSourceResolver = imageSourceResolver;
        _previewSchedulerFactory = previewSchedulerFactory;
        _recentItems = recentItems;
        _aboutVersion = AppProductInfo.GetVersion();
        InitializeOpenDocuments();
        _localization.PropertyChanged += OnLocalizationChanged;
        _commandLine.FileActivated += OnFileActivated;
        RefreshUpdateStatusTexts();
    }

    /// <summary>
    /// Handler for runtime «open this file» signals emitted by the platform
    /// after startup. On macOS Finder sends an Apple Event to the already-
    /// running process; cold-start activations come back through
    /// <see cref="ICommandLineActivation.GetActivationFilePath"/> instead.
    /// Под модальным диалогом файл ждёт его окончательного закрытия.
    /// </summary>
    private async void OnFileActivated(object? sender, FileActivationEventArgs e)
    {
        if (IsModalDialogOpen)
        {
            (_deferredActivationPaths ??= new Queue<string>()).Enqueue(e.Path);
            return;
        }

        await OpenActivatedFileAsync(e.Path).ConfigureAwait(true);
    }

    private async Task OpenActivatedFileAsync(string path)
    {
        try
        {
            await OpenPathAsync(path).ConfigureAwait(true);
        }
        catch (Exception)
        {
            // The open use-case already surfaces user-visible errors via
            // the view-state machine; the event handler must not throw
            // back into Avalonia's lifetime dispatch loop.
        }
    }

    /// <summary>
    /// Диалог закрыт окончательно: отложенные файлы открываются по порядку, активным остаётся
    /// последний. Если по дороге открылся новый вопрос, оставшиеся ждут уже его.
    /// </summary>
    private async Task OpenDeferredActivationsAsync()
    {
        if (_deferredActivationPaths is not { } paths)
        {
            return;
        }

        while (!IsModalDialogOpen && paths.TryDequeue(out var path))
        {
            await OpenActivatedFileAsync(path).ConfigureAwait(true);
        }
    }

    public IImageSourceResolver? ImageSourceResolver => _imageSourceResolver;

    public string this[string key] => _localization[key];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWelcome))]
    [NotifyPropertyChangedFor(nameof(IsViewer))]
    [NotifyPropertyChangedFor(nameof(IsError))]
    [NotifyPropertyChangedFor(nameof(ShowsFindToggle))]
    private ViewState _state = ViewState.NoDocument;

    [ObservableProperty]
    private MarkdownSource? _document;

    [ObservableProperty]
    private string _windowTitle = "MarkMello";

    [ObservableProperty]
    private bool _isDragHovering;

    /// <summary>
    /// Вторая строка слоя перетаскивания (A-Drop): что случится при отпускании. Пустая,
    /// если список файлов до отпускания недоступен — тогда обещать нечего.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDropTargetDetails))]
    private string _dropTargetDetails = string.Empty;

    [ObservableProperty]
    private bool _isDropTargetFolder;

    private string? _describedDropPath;

    public bool HasDropTargetDetails => !string.IsNullOrEmpty(DropTargetDetails);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSettingsOpen))]
    [NotifyPropertyChangedFor(nameof(IsAppMenuOpen))]
    [NotifyPropertyChangedFor(nameof(IsAppSettingsOpen))]
    [NotifyPropertyChangedFor(nameof(HasOpenOverlay))]
    [NotifyPropertyChangedFor(nameof(AppMenuOverlayContent))]
    [NotifyPropertyChangedFor(nameof(AppSettingsContent))]
    [NotifyPropertyChangedFor(nameof(ReadingSettingsOverlayContent))]
    private ShellOverlayKind _shellOverlay = ShellOverlayKind.None;

    [ObservableProperty]
    private double _readingProgress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FindResultLabel))]
    [NotifyPropertyChangedFor(nameof(FindOverlayContent))]
    private bool _isFindBarOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FindResultLabel))]
    private string _findQuery = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FindResultLabel))]
    private int _findMatchIndex = -1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FindResultLabel))]
    private int _findMatchCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSystemThemeSelected))]
    [NotifyPropertyChangedFor(nameof(IsLightThemeSelected))]
    [NotifyPropertyChangedFor(nameof(IsDarkThemeSelected))]
    private ThemeMode _theme = ThemeMode.System;

    [ObservableProperty]
    private ReadingPreferences _readingPreferences = ReadingPreferences.Default;

    [ObservableProperty]
    private RenderedMarkdownDocument _renderedDocument = RenderedMarkdownDocument.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveDocumentContent))]
    private bool _isEditMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveDocumentContent))]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private EditorSessionViewModel? _editorSession;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DirtyPromptContent))]
    private bool _isDirtyPromptOpen;

    [ObservableProperty]
    private string _dirtyPromptTitle = string.Empty;

    [ObservableProperty]
    private string _dirtyPromptMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDirtyPromptError))]
    private string _dirtyPromptErrorMessage = string.Empty;

    [ObservableProperty]
    private string _errorTitle = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrorDescription))]
    private string _errorDescription = string.Empty;

    /// <summary>Путь неудачного открытия — отдельной строкой, чтобы его можно было скопировать.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrorPath))]
    private string _errorPath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotFoundError))]
    [NotifyPropertyChangedFor(nameof(IsAccessDeniedError))]
    [NotifyPropertyChangedFor(nameof(IsUnsupportedTypeError))]
    [NotifyPropertyChangedFor(nameof(IsGenericError))]
    [NotifyPropertyChangedFor(nameof(ShowsLoadErrorRetry))]
    private LoadErrorKind _errorKind;

    public bool HasErrorDescription => !string.IsNullOrEmpty(ErrorDescription);

    public bool HasErrorPath => !string.IsNullOrEmpty(ErrorPath);

    public bool IsNotFoundError => ErrorKind == LoadErrorKind.NotFound;

    public bool IsAccessDeniedError => ErrorKind == LoadErrorKind.AccessDenied;

    public bool IsUnsupportedTypeError => ErrorKind == LoadErrorKind.UnsupportedType;

    public bool IsGenericError => ErrorKind is LoadErrorKind.ReadFailure or LoadErrorKind.Folder;

    /// <summary>
    /// «Повторить» есть у ошибок, которые могут пройти сами: файл вернули, права выдали.
    /// Не-Markdown от повтора Markdown не станет, а ошибка папки повторяется её же открытием.
    /// </summary>
    public bool ShowsLoadErrorRetry => ErrorKind is LoadErrorKind.NotFound or LoadErrorKind.AccessDenied or LoadErrorKind.ReadFailure;

    [ObservableProperty]
    private bool _isCheckingForUpdates;

    [ObservableProperty]
    private bool _isDownloadingUpdate;

    [ObservableProperty]
    private string _updateStatusTitle = string.Empty;

    [ObservableProperty]
    private string _updateStatusMessage = string.Empty;

    [ObservableProperty]
    private string? _downloadedUpdatePath;

    public object ActiveDocumentContent => IsEditMode && EditorSession is not null ? EditorSession : this;

    public string FileName => EditorSession?.FileName ?? Document?.FileName ?? string.Empty;

    public string TitleFileDisplayName => string.IsNullOrWhiteSpace(FileName)
        ? string.Empty
        : FileName + (IsDirty ? " •" : string.Empty);

    public bool IsWelcome => State == ViewState.NoDocument && !ShowsSidebar;

    public bool IsViewer => State == ViewState.Viewing;

    public bool IsError => State == ViewState.LoadError;

    public bool IsDirty => EditorSession?.IsDirty == true;

    public bool ShowCustomTitleBar => _showCustomTitleBar;

    public bool IsSettingsOpen => ShellOverlay == ShellOverlayKind.ReadingSettings;

    // Меню ⋯ в строке окна есть всегда — и в правке, и на стартовом экране (ADR-0009 Rule 2).
    public bool IsAppMenuOpen => ShellOverlay == ShellOverlayKind.AppMenu;

    /// <summary>
    /// Окно «Настройки» — модальная карточка на общей рамке диалогов (ADR-0009 Rule 7).
    /// Это слой оверлея, а не вопрос: его закрывают Esc, ✕ и любая команда, которая
    /// закрывает карточки, поэтому в <see cref="IsModalDialogOpen"/> оно не входит.
    /// </summary>
    public bool IsAppSettingsOpen => ShellOverlay == ShellOverlayKind.Settings;

    public bool HasOpenOverlay => IsSettingsOpen || IsAppMenuOpen || IsAppSettingsOpen;

    public object? AppMenuOverlayContent => IsAppMenuOpen ? this : null;

    /// <summary>Карточка «Настройки» строится по первому открытию, а не на старте (ADR-0009 Rule 12).</summary>
    public object? AppSettingsContent => IsAppSettingsOpen ? this : null;

    /// <summary>
    /// Кнопка Aa и её карточка есть только при чтении: в правке блок справа
    /// показывает «Готово», а размер текста меняется сочетаниями.
    /// </summary>
    public bool ShowsReadingSettingsToggle => IsViewer && !IsEditMode;

    public object? ReadingSettingsOverlayContent => IsSettingsOpen && ShowsReadingSettingsToggle ? this : null;

    public bool ShowsReadingStatus => IsViewer && !IsEditMode;

    /// <summary>
    /// Кнопка поиска в строке окна: искать есть в чём только в открытом документе.
    /// На стартовом экране, в пустой папке и на ошибке справа остаётся одно меню ⋯.
    /// </summary>
    public bool ShowsFindToggle => IsViewer;

    /// <summary>
    /// Карточка поиска под кнопкой поиска: создаётся по ⌘F, а не на старте, и заново при
    /// каждом открытии — поле получает фокус, как только карточка появляется.
    /// </summary>
    public object? FindOverlayContent => IsFindBarOpen ? this : null;

    public string FindResultLabel
    {
        get
        {
            if (FindMatchCount == 0)
            {
                return FindQuery.Length > 0
                    ? _localization["FindNoResults"]
                    : _localization.Format("FindResultCount", 0, 0);
            }

            return _localization.Format("FindResultCount", FindMatchIndex + 1, FindMatchCount);
        }
    }

    public ReadingPreferences DocumentReadingPreferences => _documentReadingPreferences;

    /// <summary>Карандаш — только при чтении: в правке на его месте «Готово».</summary>
    public bool ShowsEditToggle => IsViewer && Document is not null && !IsEditMode;

    /// <summary>
    /// «Готово» возвращает к чтению (ADR-0009 Rule 2). У черновика ⌘N её нет: читать
    /// документ без пути нельзя, пока он не сохранён через «Сохранить как».
    /// </summary>
    public bool ShowsDoneButton => IsViewer && IsEditMode && !IsUnsavedDraft;

    /// <summary>
    /// «Не сохранено ⌘S» рядом с «Готово» — пока есть несохранённые правки. Черновик
    /// не сохранён, даже пока он пуст: у него это единственная подсказка, как выйти.
    /// </summary>
    public bool ShowsUnsavedIndicator => IsViewer && IsEditMode && (IsDirty || IsUnsavedDraft);

    /// <summary>Черновик ⌘N: сессия правки без документа на диске.</summary>
    private bool IsUnsavedDraft => EditorSession is not null && Document is null;

    /// <summary>
    /// Нижняя строка окна «Настройки»: продукт, версия сборки и лицензия; имя автора
    /// идёт за ней ссылкой. Слов для перевода здесь нет.
    /// </summary>
    public string AppSettingsVersionLine => $"{AppProductInfo.Name} {_aboutVersion} · {_aboutLicense} ·";

    public bool HasDirtyPromptError => !string.IsNullOrWhiteSpace(DirtyPromptErrorMessage);

    /// <summary>Карточка несохранённых правок строится в момент вопроса, а не живёт скрытой с запуска.</summary>
    public object? DirtyPromptContent => IsDirtyPromptOpen ? this : null;

    /// <summary>
    /// Открыт модальный диалог — о правках или об удалении. Скрим держит мышь, а эта проверка —
    /// сочетания окна: пока вопрос не отвечен, вкладки не открываются, не закрываются и не
    /// переключаются, а второй диалог не встаёт поверх первого (ADR-0009 Rule 10).
    /// </summary>
    public bool IsModalDialogOpen => IsDirtyPromptOpen || IsDeletePromptOpen || IsRecentRemovePromptOpen;

    public bool CanCheckForUpdates => !IsCheckingForUpdates && !IsDownloadingUpdate;

    public bool CanDownloadAvailableUpdate
        => _availableUpdatePackage is not null
           && string.IsNullOrWhiteSpace(DownloadedUpdatePath)
           && !IsCheckingForUpdates
           && !IsDownloadingUpdate;

    public bool CanOpenDownloadedUpdate
        => _availableUpdatePackage is not null
           && !string.IsNullOrWhiteSpace(DownloadedUpdatePath)
           && !IsCheckingForUpdates
           && !IsDownloadingUpdate;

    /// <summary>
    /// Блок «Обновления» окна «Настройки» — один шаблон на все состояния: заголовок,
    /// пояснение и одна кнопка справа. Кнопка — следующий шаг: проверить, скачать,
    /// открыть скачанное. Пока идёт проверка или скачивание, она гаснет с подписью
    /// процесса; после ошибки проверки «Проверить» служит повтором, после ошибки
    /// скачивания — «Скачать». В сеть ходит только по нажатию (ADR-0003 §5).
    /// </summary>
    public string UpdateActionLabel => GetUpdateAction() switch
    {
        UpdateAction.Checking => _localization["UpdateChecking"],
        UpdateAction.Downloading => _localization["UpdateDownloading"],
        UpdateAction.OpenDownloaded => _availableUpdatePackage?.InstallAction switch
        {
            AppUpdateInstallAction.LaunchInstaller => _localization["UpdateLaunchInstaller"],
            AppUpdateInstallAction.OpenDiskImage => _localization["UpdateOpenDmg"],
            AppUpdateInstallAction.RevealFile => _localization["UpdateRevealAppImage"],
            _ => _localization["UpdateOpenDownloaded"]
        },
        UpdateAction.Download => _localization["UpdateDownload"],
        _ => _localization["UpdateCheckNow"]
    };

    public ICommand UpdateActionCommand => GetUpdateAction() switch
    {
        UpdateAction.Downloading or UpdateAction.Download => DownloadUpdateCommand,
        UpdateAction.OpenDownloaded => OpenDownloadedUpdateCommand,
        _ => CheckForUpdatesCommand
    };

    /// <summary>Основная кнопка — когда есть что скачать или открыть; проверка — обычная.</summary>
    public bool IsUpdateActionPrimary => GetUpdateAction() is UpdateAction.Download or UpdateAction.OpenDownloaded;

    private UpdateAction GetUpdateAction()
        => IsCheckingForUpdates
            ? UpdateAction.Checking
            : IsDownloadingUpdate
                ? UpdateAction.Downloading
                : CanOpenDownloadedUpdate
                    ? UpdateAction.OpenDownloaded
                    : CanDownloadAvailableUpdate
                        ? UpdateAction.Download
                        : UpdateAction.Check;

    public FontFamilyMode SelectedFontFamilyMode
    {
        get => ReadingPreferences.FontFamily;
        set
        {
            if (ReadingPreferences.FontFamily == value)
            {
                return;
            }

            ApplyReadingPreferences(ReadingPreferences with { FontFamily = value });
        }
    }

    public double FontSizeSetting
    {
        get => ReadingPreferences.FontSize;
        set
        {
            var fontSize = (int)Math.Round(value, MidpointRounding.AwayFromZero);
            if (ReadingPreferences.FontSize == fontSize)
            {
                return;
            }

            ApplyReadingPreferences(ReadingPreferences with { FontSize = fontSize });
        }
    }

    /// <summary>
    /// Размер текста сочетаниями ⌘+ / ⌘− / ⌘0 (Ctrl на Windows и Linux) и кнопками
    /// «− / +» карточки Aa. Работают при открытом документе — и в чтении, и в правке;
    /// на границах диапазона команда недоступна, и сочетание ничего не делает.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanIncreaseTextSize))]
    private void IncreaseTextSize() => FontSizeSetting = ReadingPreferences.FontSize + 1;

    private bool CanIncreaseTextSize() => IsViewer && ReadingPreferences.FontSize < ReadingPreferences.MaxFontSize;

    [RelayCommand(CanExecute = nameof(CanDecreaseTextSize))]
    private void DecreaseTextSize() => FontSizeSetting = ReadingPreferences.FontSize - 1;

    private bool CanDecreaseTextSize() => IsViewer && ReadingPreferences.FontSize > ReadingPreferences.MinFontSize;

    [RelayCommand(CanExecute = nameof(CanResetTextSize))]
    private void ResetTextSize() => FontSizeSetting = ReadingPreferences.Default.FontSize;

    private bool CanResetTextSize() => IsViewer && ReadingPreferences.FontSize != ReadingPreferences.Default.FontSize;

    private void UpdateTextSizeCommandStates()
    {
        IncreaseTextSizeCommand.NotifyCanExecuteChanged();
        DecreaseTextSizeCommand.NotifyCanExecuteChanged();
        ResetTextSizeCommand.NotifyCanExecuteChanged();
    }

    public double LineHeightSetting
    {
        get => ReadingPreferences.LineHeight;
        set
        {
            var normalized = Math.Round(
                value / ReadingPreferences.LineHeightStep,
                MidpointRounding.AwayFromZero) * ReadingPreferences.LineHeightStep;

            if (Math.Abs(ReadingPreferences.LineHeight - normalized) < 0.0001)
            {
                return;
            }

            ApplyReadingPreferences(ReadingPreferences with { LineHeight = normalized });
        }
    }

    public double DocumentColumnMaxWidth => ReadingLayoutMetrics.GetDocumentColumnMaxWidth(ReadingPreferences);

    public double ContentWidthSetting
    {
        get => ReadingPreferences.ContentWidth;
        set
        {
            var contentWidth = (int)Math.Round(
                value / ReadingPreferences.ContentWidthStep,
                MidpointRounding.AwayFromZero) * ReadingPreferences.ContentWidthStep;

            if (ReadingPreferences.ContentWidth == contentWidth)
            {
                return;
            }

            ApplyReadingPreferences(ReadingPreferences with { ContentWidth = contentWidth });
        }
    }

    public string FontSizeLabel => _localization.Format("ReadingFontSizeValue", ReadingPreferences.FontSize);

    public string LineHeightLabel => ReadingPreferences.LineHeight.ToString("0.00", _localization.Culture);

    public bool IsSerifFontSelected
    {
        get => ReadingPreferences.FontFamily == FontFamilyMode.Serif;
        set
        {
            if (!value)
            {
                OnPropertyChanged(nameof(IsSerifFontSelected));
                return;
            }

            SelectedFontFamilyMode = FontFamilyMode.Serif;
        }
    }

    public bool IsSansFontSelected
    {
        get => ReadingPreferences.FontFamily == FontFamilyMode.Sans;
        set
        {
            if (!value)
            {
                OnPropertyChanged(nameof(IsSansFontSelected));
                return;
            }

            SelectedFontFamilyMode = FontFamilyMode.Sans;
        }
    }

    public bool IsMonoFontSelected
    {
        get => ReadingPreferences.FontFamily == FontFamilyMode.Mono;
        set
        {
            if (!value)
            {
                OnPropertyChanged(nameof(IsMonoFontSelected));
                return;
            }

            SelectedFontFamilyMode = FontFamilyMode.Mono;
        }
    }

    public bool IsNarrowWidthSelected
    {
        get => ReadingPreferences.ContentWidth == ReadingPreferences.NarrowContentWidth;
        set
        {
            if (!value)
            {
                OnPropertyChanged(nameof(IsNarrowWidthSelected));
                return;
            }

            ContentWidthSetting = ReadingPreferences.NarrowContentWidth;
        }
    }

    public bool IsMediumWidthSelected
    {
        get => ReadingPreferences.ContentWidth == ReadingPreferences.MediumContentWidth;
        set
        {
            if (!value)
            {
                OnPropertyChanged(nameof(IsMediumWidthSelected));
                return;
            }

            ContentWidthSetting = ReadingPreferences.MediumContentWidth;
        }
    }

    public bool IsWideWidthSelected
    {
        get => ReadingPreferences.ContentWidth == ReadingPreferences.WideContentWidth;
        set
        {
            if (!value)
            {
                OnPropertyChanged(nameof(IsWideWidthSelected));
                return;
            }

            ContentWidthSetting = ReadingPreferences.WideContentWidth;
        }
    }


    /// <summary>
    /// Рамка окна. Хранится отдельно от <see cref="ReadingPreferences"/>: это
    /// настройка оболочки, а не чтения документа.
    /// </summary>
    public WindowBorderMode WindowBorderMode
    {
        get => _windowBorderMode;
        set
        {
            if (_windowBorderMode == value)
            {
                return;
            }

            _windowBorderMode = value;
            OnPropertyChanged();
            RaiseWindowBorderSelectionChanged();

            if (_isWindowBorderLoaded)
            {
                _ = _settings.SaveWindowBorderModeAsync(value).AsTask();
            }
        }
    }

    public bool IsWindowBorderAutoSelected
    {
        get => WindowBorderMode == WindowBorderMode.Auto;
        set => SelectWindowBorderMode(value, WindowBorderMode.Auto, nameof(IsWindowBorderAutoSelected));
    }

    public bool IsWindowBorderOnSelected
    {
        get => WindowBorderMode == WindowBorderMode.On;
        set => SelectWindowBorderMode(value, WindowBorderMode.On, nameof(IsWindowBorderOnSelected));
    }

    public bool IsWindowBorderOffSelected
    {
        get => WindowBorderMode == WindowBorderMode.Off;
        set => SelectWindowBorderMode(value, WindowBorderMode.Off, nameof(IsWindowBorderOffSelected));
    }

    private void SelectWindowBorderMode(bool isChecked, WindowBorderMode mode, string propertyName)
    {
        if (!isChecked)
        {
            // Unchecking the active segment would leave the group with no
            // selection; the segmented control only ever moves between options.
            OnPropertyChanged(propertyName);
            return;
        }

        WindowBorderMode = mode;
    }

    private void RaiseWindowBorderSelectionChanged()
    {
        OnPropertyChanged(nameof(IsWindowBorderAutoSelected));
        OnPropertyChanged(nameof(IsWindowBorderOnSelected));
        OnPropertyChanged(nameof(IsWindowBorderOffSelected));
    }

    /// <summary>
    /// Тема «Авто / Светлая / Тёмная» в карточке Aa (ADR-0009). «Авто» — сохраняемый
    /// выбор <see cref="ThemeMode.System"/>: приложение само идёт за темой ОС.
    /// </summary>
    public bool IsSystemThemeSelected
    {
        get => Theme == ThemeMode.System;
        set => SelectTheme(value, ThemeMode.System, nameof(IsSystemThemeSelected));
    }

    public bool IsLightThemeSelected
    {
        get => Theme == ThemeMode.Light;
        set => SelectTheme(value, ThemeMode.Light, nameof(IsLightThemeSelected));
    }

    public bool IsDarkThemeSelected
    {
        get => Theme == ThemeMode.Dark;
        set => SelectTheme(value, ThemeMode.Dark, nameof(IsDarkThemeSelected));
    }

    private void SelectTheme(bool isChecked, ThemeMode mode, string propertyName)
    {
        if (!isChecked)
        {
            // Как и у остальных сегментов: снять отметку с выбранного нельзя.
            OnPropertyChanged(propertyName);
            return;
        }

        if (Theme == mode)
        {
            return;
        }

        ApplyTheme(mode);
        PersistTheme(mode);
    }

    public DocumentMinimapMode SelectedDocumentMinimapMode
    {
        get => ReadingPreferences.DocumentMinimapMode;
        set
        {
            if (ReadingPreferences.DocumentMinimapMode == value)
            {
                return;
            }

            ApplyReadingPreferences(ReadingPreferences with { DocumentMinimapMode = value });
        }
    }

    public bool IsDocumentMinimapAutoSelected
    {
        get => ReadingPreferences.DocumentMinimapMode == DocumentMinimapMode.Auto;
        set
        {
            if (!value)
            {
                OnPropertyChanged(nameof(IsDocumentMinimapAutoSelected));
                return;
            }

            SelectedDocumentMinimapMode = DocumentMinimapMode.Auto;
        }
    }

    public bool IsDocumentMinimapOnSelected
    {
        get => ReadingPreferences.DocumentMinimapMode == DocumentMinimapMode.On;
        set
        {
            if (!value)
            {
                OnPropertyChanged(nameof(IsDocumentMinimapOnSelected));
                return;
            }

            SelectedDocumentMinimapMode = DocumentMinimapMode.On;
        }
    }

    public bool IsDocumentMinimapOffSelected
    {
        get => ReadingPreferences.DocumentMinimapMode == DocumentMinimapMode.Off;
        set
        {
            if (!value)
            {
                OnPropertyChanged(nameof(IsDocumentMinimapOffSelected));
                return;
            }

            SelectedDocumentMinimapMode = DocumentMinimapMode.Off;
        }
    }

    public int WordCount => EditorSession?.WordCount ?? CountWords(Document?.Content);

    /// <summary>
    /// Оценка времени чтения: 200 слов в минуту с округлением вверх — как на холсте,
    /// где 499 слов читаются «3 мин».
    /// </summary>
    public int ReadTimeMinutes => Math.Max(1, (int)Math.Ceiling(WordCount / 200.0));

    private bool _suppressStartupActivation;

    /// <summary>
    /// Окно открыто не запуском процесса, а из уже работающего приложения:
    /// стартовые аргументы к нему не относятся.
    /// </summary>
    public void SuppressStartupActivation() => _suppressStartupActivation = true;

    public async Task InitializeAsync()
    {
        // Стартовый экран появляется только по завершении стартовой активации. Если она
        // упала, окно всё равно должно его получить, а не остаться пустым навсегда.
        try
        {
            await InitializeCoreAsync().ConfigureAwait(true);
        }
        finally
        {
            CompleteStartupActivation();
        }
    }

    private async Task InitializeCoreAsync()
    {
        ReadingPreferences = await _settings.LoadPreferencesAsync().ConfigureAwait(true);

        var savedLanguage = await _settings.LoadLanguageAsync().ConfigureAwait(true);
        ApplyLanguageSelection(savedLanguage, persist: false);

        var savedTheme = await _settings.LoadThemeAsync().ConfigureAwait(true);
        ApplyTheme(savedTheme);

        WindowBorderMode = await _settings.LoadWindowBorderModeAsync().ConfigureAwait(true);
        _isWindowBorderLoaded = true;

        // Аргументы командной строки принадлежат запуску процесса, а не каждому окну:
        // второе окно получает свою папку от launcher'а и стартовую активацию пропускает.
        if (_suppressStartupActivation)
        {
            return;
        }

        // Каталог в аргументах открывает папку, файл — документ. Порядок важен:
        // «MarkMello docs notes.md» должен показать и дерево, и запрошенный документ.
        var folderPath = _commandLine.GetActivationFolderPath();
        if (!string.IsNullOrEmpty(folderPath))
        {
            await OpenFolderPathAsync(folderPath).ConfigureAwait(true);
        }

        var path = _commandLine.GetActivationFilePath();
        if (!string.IsNullOrEmpty(path))
        {
            await OpenPathAsync(path).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Файл открывается в своей вкладке, поэтому о правках активной не спрашивает
    /// (ADR-0009 Rule 3). Под открытым диалогом и выбирать нечего: открыть файл всё равно нельзя.
    /// </summary>
    [RelayCommand]
    private async Task OpenFileAsync()
    {
        CloseOverlayCore();

        if (IsModalDialogOpen)
        {
            return;
        }

        var path = await _filePicker.PickMarkdownFileAsync().ConfigureAwait(true);
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        await OpenAndRememberFileAsync(path).ConfigureAwait(true);
    }

    /// <summary>
    /// Черновик открывается в своей вкладке, а правки активной остаются в её сессии, поэтому
    /// спрашивать о них нечего (ADR-0009 Rule 3): «Не сохранять» стёрло бы их зря. Под открытым
    /// диалогом ⌘N не срабатывает — смена активной вкладки увела бы ответ на черновик.
    /// </summary>
    [RelayCommand]
    private Task CreateNewDocumentAsync()
    {
        CloseOverlayCore();

        if (!IsModalDialogOpen)
        {
            CreateNewDocumentCore();
        }

        return Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(CanReload))]
    private async Task ReloadAsync()
    {
        CloseAppSettingsWindow();

        var path = CurrentDocumentPath;
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var preserveEditMode = IsEditMode;
        await RunWithDirtyCheckAsync(
            PendingDirtyActionKind.Reload,
            () => LoadDocumentAsync(path, preserveEditModeAfterLoad: preserveEditMode))
            .ConfigureAwait(true);
    }

    private bool CanReload() => !string.IsNullOrEmpty(CurrentDocumentPath);

    [RelayCommand(CanExecute = nameof(CanToggleEditMode))]
    private async Task ToggleEditModeAsync()
    {
        CloseAppSettingsWindow();

        if (IsEditMode)
        {
            await RunWithDirtyCheckAsync(
                PendingDirtyActionKind.LeaveEditMode,
                ExitEditModeCoreAsync)
                .ConfigureAwait(true);
            return;
        }

        EnterEditModeCore();
    }

    private bool CanToggleEditMode() => State == ViewState.Viewing && Document is not null;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        var outcome = await SaveEditorAsync(promptForPathWhenMissing: true, forceSaveAs: false).ConfigureAwait(true);
        if (outcome.Cancelled)
        {
            return;
        }

        if (outcome.Result is not SaveDocumentResult.Success success)
        {
            EditorSession?.SetStatusMessage(GetSaveFailureMessage(outcome.Result));
            return;
        }

        ApplySavedDocument(success.Source);
    }

    private bool CanSave() => IsEditMode && EditorSession is not null;

    [RelayCommand(CanExecute = nameof(CanSaveAs))]
    private async Task SaveAsAsync()
    {
        var outcome = await SaveEditorAsync(promptForPathWhenMissing: true, forceSaveAs: true).ConfigureAwait(true);
        if (outcome.Cancelled)
        {
            return;
        }

        if (outcome.Result is not SaveDocumentResult.Success success)
        {
            EditorSession?.SetStatusMessage(GetSaveFailureMessage(outcome.Result));
            return;
        }

        ApplySavedDocument(success.Source);
    }

    private bool CanSaveAs() => IsEditMode && EditorSession is not null;

    [RelayCommand]
    private async Task ConfirmDirtySaveAsync()
    {
        if (_pendingDirtyAction is null)
        {
            return;
        }

        if (!await ShowDirtyPromptTabAsync().ConfigureAwait(true))
        {
            return;
        }

        SetDirtyPromptError(null);

        var outcome = await SaveEditorAsync(promptForPathWhenMissing: true, forceSaveAs: false).ConfigureAwait(true);
        if (outcome.Cancelled)
        {
            return;
        }

        if (outcome.Result is not SaveDocumentResult.Success success)
        {
            SetDirtyPromptError(outcome.Result);
            return;
        }

        ApplySavedDocument(success.Source);
        await ContinuePendingDirtyActionAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ConfirmDirtyDiscardAsync()
    {
        if (!await ShowDirtyPromptTabAsync().ConfigureAwait(true))
        {
            return;
        }

        DiscardEditorChanges();
        await ContinuePendingDirtyActionAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Сохранение и сброс работают с активной сессией, поэтому перед ответом активной
    /// становится вкладка, о которой спросили. Если её уже нет, ответ равен «Отмене»:
    /// сохранять и сбрасывать нечего, а чужие правки трогать нельзя.
    /// </summary>
    private async Task<bool> ShowDirtyPromptTabAsync()
    {
        if (_dirtyPromptTab is not { } tab || !OpenDocuments.Tabs.Contains(tab))
        {
            await CancelDirtyPromptAsync().ConfigureAwait(true);
            return false;
        }

        if (!ReferenceEquals(OpenDocuments.ActiveTab, tab))
        {
            await RestoreTabAsync(tab).ConfigureAwait(true);
        }

        return true;
    }

    [RelayCommand]
    private async Task CancelDirtyPromptAsync()
    {
        ClearDirtyPrompt();
        await OpenDeferredActivationsAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void ToggleSettings()
    {
        // В правке кнопки Aa нет — карточка без кнопки не открывается.
        if (!IsSettingsOpen && IsEditMode)
        {
            return;
        }

        MarkSecondaryFeaturesReady();

        IsFindBarOpen = false;
        ShellOverlay = IsSettingsOpen
            ? ShellOverlayKind.None
            : ShellOverlayKind.ReadingSettings;
    }

    [RelayCommand]
    private void ToggleFindBar()
    {
        if (IsFindBarOpen)
        {
            IsFindBarOpen = false;
            return;
        }

        // Карточка раскрывается под кнопкой поиска, а без документа кнопки нет —
        // и искать не в чем.
        if (!ShowsFindToggle)
        {
            return;
        }

        CloseOverlayCore();
        IsFindBarOpen = true;
    }

    [RelayCommand]
    private void CloseFindBar()
    {
        IsFindBarOpen = false;
    }

    [RelayCommand]
    private void CloseSettings()
    {
        if (IsSettingsOpen)
        {
            ShellOverlay = ShellOverlayKind.None;
        }
    }

    [RelayCommand]
    private void ToggleAppMenu()
    {
        MarkSecondaryFeaturesReady();

        IsFindBarOpen = false;
        ShellOverlay = IsAppMenuOpen
            ? ShellOverlayKind.None
            : ShellOverlayKind.AppMenu;
    }

    /// <summary>
    /// Окно «Настройки» — из меню ⋯, из карточки Aa и по ⌘, — везде одно и то же:
    /// открытая карточка под ним закрывается. Под вопросом о правках или удалении окно
    /// не открывается: оно встало бы под скрим вопроса и забрало бы у него фокус.
    /// </summary>
    [RelayCommand]
    private void OpenAppSettings()
    {
        if (IsModalDialogOpen)
        {
            return;
        }

        MarkSecondaryFeaturesReady();

        IsFindBarOpen = false;
        ShellOverlay = ShellOverlayKind.Settings;
    }

    /// <summary>
    /// «О MarkMello» — своё окно ОС (ADR-0009 Rule 7), а не карточка внутри окна:
    /// на macOS команда приходит из системного меню приложения, где карточку показать
    /// негде. Окно создаётся только здесь, по нажатию, и второй раз не плодится.
    /// </summary>
    [RelayCommand]
    private void ShowAbout()
    {
        MarkSecondaryFeaturesReady();
        _windowLauncher.ShowAbout();
    }

    /// <summary>
    /// ⌘, (Ctrl+, на Windows и Linux) открывает окно «Настройки» везде, включая стартовый
    /// экран и правку (ADR-0009 Rule 7); повторное нажатие его закрывает.
    /// </summary>
    [RelayCommand]
    private void ToggleAppSettings()
    {
        if (IsAppSettingsOpen)
        {
            CloseOverlayCore();
            return;
        }

        OpenAppSettings();
    }

    [RelayCommand]
    private void CloseOverlay()
    {
        CloseOverlayCore();
    }

    [RelayCommand(CanExecute = nameof(CanCheckForUpdates))]
    private async Task CheckForUpdatesAsync()
    {
        IsCheckingForUpdates = true;
        IsDownloadingUpdate = false;
        _availableUpdatePackage = null;
        DownloadedUpdatePath = null;
        SetUpdateStatus(new UpdateStatusSnapshot.CheckingState());
        UpdateCommandStates();

        try
        {
            var result = await _updateService.CheckForUpdatesAsync().ConfigureAwait(true);
            switch (result)
            {
                case UpdateCheckResult.SourceNotConfigured:
                    SetUpdateStatus(new UpdateStatusSnapshot.SourceNotConfiguredState());
                    break;

                case UpdateCheckResult.UnsupportedPlatform unsupportedPlatform:
                    SetUpdateStatus(new UpdateStatusSnapshot.UnsupportedPlatformState(
                        unsupportedPlatform.PlatformName,
                        unsupportedPlatform.ArchitectureName));
                    break;

                case UpdateCheckResult.UpToDate upToDate:
                    SetUpdateStatus(new UpdateStatusSnapshot.UpToDateState(
                        upToDate.CurrentVersion,
                        upToDate.LatestVersion));
                    break;

                case UpdateCheckResult.UpdateAvailable updateAvailable:
                    _availableUpdatePackage = updateAvailable.Package;
                    SetUpdateStatus(new UpdateStatusSnapshot.UpdateAvailableState(updateAvailable.Package));
                    break;

                case UpdateCheckResult.Failed failed:
                    SetUpdateStatus(new UpdateStatusSnapshot.CheckFailedState(failed.Message));
                    break;
            }
        }
        finally
        {
            IsCheckingForUpdates = false;
            UpdateCommandStates();
        }
    }

    [RelayCommand(CanExecute = nameof(CanDownloadAvailableUpdate))]
    private async Task DownloadUpdateAsync()
    {
        if (_availableUpdatePackage is null)
        {
            return;
        }

        IsDownloadingUpdate = true;
        SetUpdateStatus(new UpdateStatusSnapshot.DownloadingState(_availableUpdatePackage));
        UpdateCommandStates();

        try
        {
            var result = await _updateService
                .DownloadUpdateAsync(_availableUpdatePackage)
                .ConfigureAwait(true);

            switch (result)
            {
                case UpdateDownloadResult.Success success:
                    _availableUpdatePackage = success.Package;
                    DownloadedUpdatePath = success.DownloadedFilePath;
                    SetUpdateStatus(new UpdateStatusSnapshot.DownloadReadyState(success.Package, success.DownloadedFilePath));
                    break;

                case UpdateDownloadResult.Failed failed:
                    DownloadedUpdatePath = null;
                    SetUpdateStatus(new UpdateStatusSnapshot.DownloadFailedState(failed.Message));
                    break;
            }
        }
        finally
        {
            IsDownloadingUpdate = false;
            UpdateCommandStates();
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpenDownloadedUpdate))]
    private async Task OpenDownloadedUpdateAsync()
    {
        if (_availableUpdatePackage is null || string.IsNullOrWhiteSpace(DownloadedUpdatePath))
        {
            return;
        }

        var result = await _updateService
            .PrepareDownloadedUpdateAsync(_availableUpdatePackage, DownloadedUpdatePath)
            .ConfigureAwait(true);

        switch (result)
        {
            case UpdatePrepareResult.Success:
                SetUpdateStatus(new UpdateStatusSnapshot.NativeFlowStartedState(_availableUpdatePackage));
                break;

            case UpdatePrepareResult.Failed failed:
                SetUpdateStatus(new UpdateStatusSnapshot.OpenDownloadedFailedState(failed.Message));
                break;
        }

        UpdateCommandStates();
    }

    /// <summary>
    /// Esc закрывает верхний слой: сначала модальный диалог — под ним могут остаться
    /// открытыми поиск или карточка, — потом их, потом экран ошибки. Вкладка ошибки
    /// при этом закрывается целиком: без файла ей нечего показывать.
    /// </summary>
    [RelayCommand]
    private async Task ClearErrorAsync()
    {
        if (IsDirtyPromptOpen)
        {
            _ = CancelDirtyPromptAsync();
            return;
        }

        if (IsDeletePromptOpen)
        {
            CancelDelete();
            return;
        }

        if (IsRecentRemovePromptOpen)
        {
            CloseRecentRemovePrompt();
            return;
        }

        if (IsFindBarOpen)
        {
            IsFindBarOpen = false;
            return;
        }

        if (HasOpenOverlay)
        {
            CloseOverlayCore();
            return;
        }

        if (State != ViewState.LoadError)
        {
            return;
        }

        // Ошибка папки поверх вкладки ошибки снимается сама, а вкладка остаётся.
        if (ErrorKind != LoadErrorKind.Folder && OpenDocuments.ActiveTab is { IsLoadError: true } errorTab)
        {
            await CloseTabAsync(errorTab).ConfigureAwait(true);
            return;
        }

        DismissOverlayError();
    }

    /// <summary>
    /// Снимает ошибку, вставшую поверх вкладки (ошибка папки, неудачное перечитывание):
    /// под ней снова виден документ. Если под ней вкладка ошибки — возвращается её экран.
    /// </summary>
    private void DismissOverlayError()
    {
        if (OpenDocuments.ActiveTab is { LoadError: { } tabError })
        {
            SetLoadError(tabError);
            RefreshWindowTitle();
            return;
        }

        if (State != ViewState.LoadError)
        {
            ClearLoadError();
            return;
        }

        State = Document is null && EditorSession is null ? ViewState.NoDocument : ViewState.Viewing;
        ClearLoadError();
        RefreshWindowTitle();
    }

    public async Task OpenDroppedFileAsync(string path)
        => await OpenAndRememberFileAsync(path).ConfigureAwait(true);

    /// <summary>Файл от ОС или из командной строки — явное открытие, попадает в «Недавние».</summary>
    public async Task OpenPathAsync(string path)
        => await OpenAndRememberFileAsync(path).ConfigureAwait(true);

    /// <summary>
    /// Переход по ссылке из документа — как клик в дереве: навигация внутри уже открытого,
    /// а не выбор файла, поэтому в «Недавние» не пишется.
    /// </summary>
    public async Task OpenLinkedDocumentAsync(string path)
        => await OpenDocumentInTabAsync(path).ConfigureAwait(true);

    /// <summary>
    /// Открытие документа — ⌘O, перетаскивание, клик в дереве, файл от ОС — не спрашивает
    /// о правках активной вкладки: документ встаёт в свою вкладку, правки остаются в своей
    /// (ADR-0009 Rule 3). Файл, который уже открыт с несохранёнными правками, просто
    /// показывается: перечитать его с диска значило бы молча выбросить эти правки.
    /// Под диалогом ничего не открывается — смена активной вкладки увела бы ответ на неё.
    /// Возвращает, показан ли документ.
    /// </summary>
    private async Task<bool> OpenDocumentInTabAsync(string path)
    {
        if (IsModalDialogOpen)
        {
            return false;
        }

        if (OpenDocuments.FindByPath(path) is { EditorSession.IsDirty: true } dirtyTab)
        {
            await ShowTabAsync(dirtyTab).ConfigureAwait(true);
            return true;
        }

        return await LoadDocumentAsync(path, preserveEditModeAfterLoad: false).ConfigureAwait(true);
    }

    public bool TryQueueCloseRequest()
    {
        if (IsModalDialogOpen)
        {
            return true;
        }

        // Грязной может быть любая вкладка, а не только активная. Окно проходит по всем
        // несохранённым вкладкам по очереди и закрывается, только когда вопросов не осталось.
        // Сюда же приходит ⌘Q на macOS: TryShutdown закрывает окна через тот же Closing.
        if (FindFirstDirtyTab() is null)
        {
            return false;
        }

        _ = ResolveDirtyTabsThenAsync(
            PendingDirtyActionKind.CloseWindow,
            static _ => true,
            () =>
            {
                // Окно закрывается — файлы, пришедшие под вопросами, открывать уже некуда.
                _deferredActivationPaths?.Clear();
                CloseRequested?.Invoke(this, EventArgs.Empty);
                return Task.CompletedTask;
            });

        return true;
    }

    partial void OnDocumentChanged(MarkdownSource? value)
    {
        RefreshDocumentSummary();
        RaiseEditActionsChanged();
        RefreshWindowTitle();
        UpdateCommandStates();
    }

    partial void OnStateChanged(ViewState value)
    {
        if (value != ViewState.Viewing)
        {
            IsFindBarOpen = false;
        }

        OnPropertyChanged(nameof(ShowsReadingStatus));
        RaiseEditActionsChanged();
        OnPropertyChanged(nameof(ShowsReadingSettingsToggle));
        OnPropertyChanged(nameof(ReadingSettingsOverlayContent));

        // Пустое состояние зависит от State, а вкладка регистрируется до перехода
        // в Viewing — без этого уведомления заглушка оставалась поверх документа.
        OnPropertyChanged(nameof(IsEmptyDocumentSurface));
        OnPropertyChanged(nameof(IsWelcome));

        RefreshWindowTitle();
        UpdateCommandStates();
    }

    partial void OnIsEditModeChanged(bool value)
    {
        SyncActiveTabEditorState();

        IsFindBarOpen = false;

        // Смена режима меняет содержимое окна под открытыми карточками — закрываем их.
        if (value)
        {
            CloseAppOverlayCore();
            CloseSettings();
        }

        OnPropertyChanged(nameof(ShowsReadingStatus));
        OnPropertyChanged(nameof(ShowsReadingSettingsToggle));
        OnPropertyChanged(nameof(ReadingSettingsOverlayContent));
        OnPropertyChanged(nameof(ActiveDocumentContent));
        RaiseEditActionsChanged();
        UpdateCommandStates();
    }

    /// <summary>Карандаш, «Готово» и «Не сохранено» зависят от режима, документа и правок.</summary>
    private void RaiseEditActionsChanged()
    {
        OnPropertyChanged(nameof(ShowsEditToggle));
        OnPropertyChanged(nameof(ShowsDoneButton));
        OnPropertyChanged(nameof(ShowsUnsavedIndicator));
    }

    partial void OnEditorSessionChanging(EditorSessionViewModel? oldValue, EditorSessionViewModel? newValue)
    {
        if (oldValue is null)
        {
            return;
        }

        oldValue.PropertyChanged -= OnEditorSessionPropertyChanged;

        // Сессия принадлежит вкладке: выбрасываем её только если вкладка её больше не держит.
        // Иначе переключение вкладок убивало бы несохранённые правки соседней.
        if (!OpenDocuments.Tabs.Any(tab => ReferenceEquals(tab.EditorSession, oldValue)))
        {
            oldValue.Dispose();
        }
    }

    partial void OnEditorSessionChanged(EditorSessionViewModel? value)
    {
        if (value is not null)
        {
            value.PropertyChanged += OnEditorSessionPropertyChanged;
            value.UpdateReadingPreferences(ReadingPreferences);
            _currentPath = value.CurrentPath;
        }

        SyncActiveTabEditorState();

        RefreshDocumentSummary();
        RefreshWindowTitle();
        UpdateCommandStates();
    }

    partial void OnReadingPreferencesChanged(ReadingPreferences value)
    {
        var documentRenderingPreferences = GetDocumentRenderingPreferences(value);
        var documentRenderingPreferencesChanged = documentRenderingPreferences != _documentReadingPreferences;
        _documentReadingPreferences = documentRenderingPreferences;

        if (documentRenderingPreferencesChanged)
        {
            EditorSession?.UpdateReadingPreferences(value);
            OnPropertyChanged(nameof(DocumentReadingPreferences));
        }

        OnPropertyChanged(nameof(SelectedFontFamilyMode));
        OnPropertyChanged(nameof(FontSizeSetting));
        OnPropertyChanged(nameof(LineHeightSetting));
        OnPropertyChanged(nameof(ContentWidthSetting));
        OnPropertyChanged(nameof(DocumentColumnMaxWidth));
        OnPropertyChanged(nameof(FontSizeLabel));
        OnPropertyChanged(nameof(LineHeightLabel));
        OnPropertyChanged(nameof(IsSerifFontSelected));
        OnPropertyChanged(nameof(IsSansFontSelected));
        OnPropertyChanged(nameof(IsMonoFontSelected));
        OnPropertyChanged(nameof(IsNarrowWidthSelected));
        OnPropertyChanged(nameof(IsMediumWidthSelected));
        OnPropertyChanged(nameof(IsWideWidthSelected));
        OnPropertyChanged(nameof(SelectedDocumentMinimapMode));
        OnPropertyChanged(nameof(IsDocumentMinimapAutoSelected));
        OnPropertyChanged(nameof(IsDocumentMinimapOnSelected));
        OnPropertyChanged(nameof(IsDocumentMinimapOffSelected));
        UpdateTextSizeCommandStates();
    }

    private void CreateNewDocumentCore()
    {
        Document = null;
        RenderedDocument = RenderedMarkdownDocument.Empty;
        _currentPath = null;
        State = ViewState.Viewing;
        ReadingProgress = 0;
        ClearLoadError();
        CloseOverlayCore();

        // Вкладку черновика заводим до того, как трогаем EditorSession и режим: иначе
        // сессия черновика припишется предыдущей вкладке вместо её собственной, а сам
        // черновик останется без сессии и потеряет текст при первом же переключении.
        TrackNewDocumentTab();

        EditorSession = new EditorSessionViewModel(
            GetUntitledFileName(),
            string.Empty,
            ReadingPreferences,
            _renderMarkdown,
            _imageSourceResolver,
            _localization,
            CreatePreviewScheduler());

        if (!_editorActivationMarked)
        {
            _editorActivationMarked = true;
            _startupMetrics.Mark(StartupStage.EditorActivation);
        }

        EditorSession.UpdateReadingPreferences(ReadingPreferences);
        EditorSession.SetStatusMessage(string.Empty);
        IsEditMode = true;
        RefreshWindowTitle();
        UpdateCommandStates();
    }

    /// <summary>
    /// Полная очистка document surface вместе со всеми вкладками.
    /// Используется там, где уходит весь контекст: закрытие папки, фатальная ошибка.
    /// </summary>
    private void CloseFileCore()
    {
        CloseOverlayCore();
        OpenDocuments.Activate(null);
        OpenDocuments.Tabs.Clear();
        IsEditMode = false;
        EditorSession = null;
        Document = null;
        RenderedDocument = RenderedMarkdownDocument.Empty;
        _currentPath = null;
        State = ViewState.NoDocument;
        ReadingProgress = 0;
        ClearLoadError();
        SyncWorkspaceActiveDocument();
        RefreshWindowTitle();
        UpdateCommandStates();
        RefreshTabState();
    }

    private void EnterEditModeCore()
    {
        if (Document is null)
        {
            return;
        }

        if (EditorSession is null)
        {
            EditorSession = new EditorSessionViewModel(
                Document,
                ReadingPreferences,
                _renderMarkdown,
                _imageSourceResolver,
                _localization,
                CreatePreviewScheduler());
        }

        if (!_editorActivationMarked)
        {
            _editorActivationMarked = true;
            _startupMetrics.Mark(StartupStage.EditorActivation);
        }

        EditorSession.UpdateReadingPreferences(ReadingPreferences);
        EditorSession.SetStatusMessage(string.Empty);
        IsEditMode = true;
    }

    private Task ExitEditModeCoreAsync()
    {
        IsEditMode = false;
        EditorSession?.SetStatusMessage(string.Empty);
        return Task.CompletedTask;
    }

    private async Task<bool> LoadDocumentAsync(string path, bool preserveEditModeAfterLoad)
    {
        var result = await _openDocument.ExecuteAsync(path).ConfigureAwait(true);
        await ApplyOpenResultAsync(result, preserveEditModeAfterLoad).ConfigureAwait(true);
        return result is OpenDocumentResult.Success;
    }

    private async Task ApplyOpenResultAsync(OpenDocumentResult result, bool preserveEditModeAfterLoad)
    {
        switch (result)
        {
            case OpenDocumentResult.Success success:
                ApplyLoadedDocument(success.Source, preserveEditModeAfterLoad);
                break;

            case OpenDocumentResult.NotFound:
            case OpenDocumentResult.AccessDenied:
            case OpenDocumentResult.ReadError:
            case OpenDocumentResult.UnsupportedType:
                await FailOpenResultAsync(result).ConfigureAwait(true);
                break;
        }
    }

    private void ApplyLoadedDocument(MarkdownSource source, bool preserveEditModeAfterLoad)
    {
        var rendered = _renderMarkdown.Execute(
            source.Content,
            baseDirectory: TryGetDirectory(source.Path));

        // Вкладку переключаем до того, как трогаем EditorSession: иначе сброс сессии
        // прилетит в предыдущую вкладку и заберёт с собой её несохранённые правки.
        TrackLoadedDocumentTab(source, rendered);

        Document = source;
        RenderedDocument = rendered;
        _currentPath = source.Path;
        State = ViewState.Viewing;
        ReadingProgress = 0;
        ClearLoadError();

        if (preserveEditModeAfterLoad)
        {
            if (EditorSession is null)
            {
                EditorSession = new EditorSessionViewModel(
                    source,
                    ReadingPreferences,
                    _renderMarkdown,
                    _imageSourceResolver,
                    _localization,
                    CreatePreviewScheduler());
            }
            else
            {
                EditorSession.ApplyLoadedDocument(source);
            }

            IsEditMode = true;
        }
        else
        {
            IsEditMode = false;
            EditorSession = null;
        }

        if (!_documentModelReadyMarked)
        {
            _documentModelReadyMarked = true;
            _startupMetrics.Mark(StartupStage.DocumentModelReady);
        }

        SyncWorkspaceActiveDocument();
        RefreshWindowTitle();
        UpdateCommandStates();
    }

    private void MarkSecondaryFeaturesReady()
    {
        if (_secondaryFeaturesMarked)
        {
            return;
        }

        _secondaryFeaturesMarked = true;
        _startupMetrics.Mark(StartupStage.SecondaryFeatures);
    }

    public void MarkReadableDocumentRendered()
    {
        if (_readableDocumentMarked || State != ViewState.Viewing || RenderedDocument.Blocks.Count == 0)
        {
            return;
        }

        _readableDocumentMarked = true;
        _startupMetrics.Mark(StartupStage.ReadableDocument);
    }

    private void ApplySavedDocument(MarkdownSource source)
    {
        // Сохранение в новый путь — «Сохранить как» или первое сохранение черновика —
        // явный выбор файла, как в диалоге «Открыть».
        if (!PathsMatch(_currentPath, source.Path))
        {
            RememberRecentFile(source.Path);
        }

        Document = source;
        RenderedDocument = _renderMarkdown.Execute(
            source.Content,
            baseDirectory: TryGetDirectory(source.Path));
        _currentPath = source.Path;

        if (EditorSession is null)
        {
            EditorSession = new EditorSessionViewModel(
                source,
                ReadingPreferences,
                _renderMarkdown,
                _imageSourceResolver,
                _localization,
                CreatePreviewScheduler());
        }
        else
        {
            EditorSession.ApplySavedDocument(source);
        }

        RetargetActiveTab(source);
        RefreshWindowTitle();
        UpdateCommandStates();
    }

    /// <summary>
    /// Неудачное открытие получает свою вкладку с экраном ошибки (A-LoadError): у ошибки
    /// есть имя файла, а у пользователя — куда вернуться. Прежняя активная вкладка уходит
    /// в фон вместе с сессией, режимом правки и несохранённым текстом — как при обычном
    /// переключении, — и закрытие вкладки ошибки возвращает к ней.
    ///
    /// Вкладка, в которой документ уже открыт, в ошибку не превращается: не перечиталась
    /// она по F5 или после чужого сохранения, а её снимок, позиция, сессия и место в сессии
    /// папки остаются ценными. Тогда ошибка встаёт поверх самой этой вкладки — она сначала
    /// становится активной, чтобы «Повторить» перечитал именно её, — и `Esc` возвращает
    /// к её тексту.
    /// </summary>
    private async Task FailOpenResultAsync(OpenDocumentResult result)
    {
        CloseOverlayCore();

        var path = GetFailedPath(result);
        var existing = OpenDocuments.FindByPath(path);
        if (string.IsNullOrEmpty(path) || existing is { IsLoadError: false })
        {
            if (existing is not null && !ReferenceEquals(OpenDocuments.ActiveTab, existing))
            {
                // Файл только что не прочитался — перечитывать его при показе вкладки незачем.
                existing.NeedsReload = false;
                await RestoreTabAsync(existing).ConfigureAwait(true);
            }

            SetLoadError(result);
            RefreshWindowTitle();
            UpdateCommandStates();
            return;
        }

        var previous = OpenDocuments.ActiveTab;
        var tab = existing ?? OpenDocuments.Add(new DocumentTabViewModel(path, Path.GetFileName(path)));

        // Повтор, который снова не удался, оставляет вкладке прежнюю точку возврата.
        tab.ApplyLoadError(result, ReferenceEquals(previous, tab) ? tab.ReturnTab : previous);
        ApplyWorkspaceMembership(tab);
        ShowLoadErrorTab(tab, result);
        OpenDocuments.Refresh();
        RefreshTabState();
    }

    /// <summary>
    /// Делает вкладку ошибки активной. Сессия прошлой вкладки остаётся у неё: зеркалирование
    /// shell → вкладка на это время выключено, как при восстановлении любой вкладки.
    /// </summary>
    private void ShowLoadErrorTab(DocumentTabViewModel tab, OpenDocumentResult error)
    {
        _isRestoringTab = true;
        try
        {
            OpenDocuments.Activate(tab);
            IsEditMode = false;
            EditorSession = null;
            Document = null;
            RenderedDocument = RenderedMarkdownDocument.Empty;
            _currentPath = tab.Path;
            ReadingProgress = 0;
            SetLoadError(error);

            SyncWorkspaceActiveDocument();
            RefreshWindowTitle();
            UpdateCommandStates();
            UpdateTabCommandStates();
            SyncExternalChangeBanner();
        }
        finally
        {
            _isRestoringTab = false;
        }
    }

    private static string? GetFailedPath(OpenDocumentResult result) => result switch
    {
        OpenDocumentResult.NotFound notFound => notFound.Path,
        OpenDocumentResult.AccessDenied denied => denied.Path,
        OpenDocumentResult.ReadError read => read.Path,
        OpenDocumentResult.UnsupportedType unsupported => unsupported.Path,
        _ => null
    };

    private async Task RunWithDirtyCheckAsync(PendingDirtyActionKind kind, Func<Task> action)
    {
        if (IsModalDialogOpen)
        {
            return;
        }

        if (!RequiresDirtyResolution)
        {
            await action().ConfigureAwait(true);
            return;
        }

        QueueDirtyAction(kind, action);
    }

    private bool RequiresDirtyResolution => IsEditMode && EditorSession?.IsDirty == true;

    /// <summary>
    /// Каждая editor-сессия получает собственный планировщик preview: отложенный
    /// рендер прошлой сессии не должен долетать до новой. Без фабрики (unit-тесты)
    /// сессия работает синхронно.
    /// </summary>
    private IEditorPreviewScheduler? CreatePreviewScheduler()
        => _previewSchedulerFactory?.Invoke();

    private void QueueDirtyAction(PendingDirtyActionKind kind, Func<Task> action)
    {
        if (IsModalDialogOpen)
        {
            return;
        }

        // Спрашивают всегда о правках активной вкладки: кто закрывает фоновую, сначала её показывает.
        _pendingDirtyAction = action;
        _dirtyPromptTab = OpenDocuments.ActiveTab;
        SetDirtyPrompt(kind);
    }

    private async Task ContinuePendingDirtyActionAsync()
    {
        var pendingAction = _pendingDirtyAction;
        ClearDirtyPrompt();
        if (pendingAction is not null)
        {
            await pendingAction().ConfigureAwait(true);
        }

        // Действие могло задать следующий вопрос (очередь грязных вкладок) — тогда
        // отложенные файлы ждут и его.
        await OpenDeferredActivationsAsync().ConfigureAwait(true);
    }

    private void ClearDirtyPrompt()
    {
        ClearDirtyPromptState();
    }

    private async Task<SaveExecutionOutcome> SaveEditorAsync(bool promptForPathWhenMissing, bool forceSaveAs)
    {
        if (EditorSession is null)
        {
            return new SaveExecutionOutcome(false, new SaveDocumentResult.InvalidPath(string.Empty));
        }

        var targetPath = forceSaveAs ? null : EditorSession.CurrentPath;
        if (string.IsNullOrWhiteSpace(targetPath) && promptForPathWhenMissing)
        {
            targetPath = await PickSavePathAsync(EditorSession.FileName).ConfigureAwait(true);
        }
        else if (forceSaveAs)
        {
            targetPath = await PickSavePathAsync(EditorSession.FileName).ConfigureAwait(true);
        }

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return new SaveExecutionOutcome(true, null);
        }

        var result = await _saveDocument.ExecuteAsync(targetPath, EditorSession.SourceText).ConfigureAwait(true);
        return new SaveExecutionOutcome(false, result);
    }

    private async Task<string?> PickSavePathAsync(string? currentFileName)
        => await _filePicker
            .PickSaveMarkdownFileAsync(NormalizeSuggestedFileName(currentFileName))
            .ConfigureAwait(true);

    private void DiscardEditorChanges()
    {
        if (EditorSession is null)
        {
            return;
        }

        EditorSession.DiscardChanges();
        RefreshDocumentSummary();
        RefreshWindowTitle();
        UpdateCommandStates();
    }

    private void ApplyTheme(ThemeMode mode)
    {
        Theme = mode;
        _themeService.Apply(mode);
    }

    private void PersistTheme(ThemeMode mode)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await _settings.SaveThemeAsync(mode).ConfigureAwait(false);
            }
            catch
            {
                // Как и настройки чтения: несохранённая тема не повод
                // прерывать чтение — выбор уже применён к окну.
            }
        });
    }

    private static ReadingPreferences GetDocumentRenderingPreferences(ReadingPreferences preferences)
    {
        var normalized = ReadingPreferences.Normalize(preferences);
        return normalized with { DocumentMinimapMode = ReadingPreferences.Default.DocumentMinimapMode };
    }

    private void ApplyReadingPreferences(ReadingPreferences preferences)
    {
        var normalized = ReadingPreferences.Normalize(preferences);
        if (normalized == ReadingPreferences)
        {
            return;
        }

        ReadingPreferences = normalized;
        PersistReadingPreferences(normalized);
    }

    private void PersistReadingPreferences(ReadingPreferences preferences)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await _settings.SavePreferencesAsync(preferences).ConfigureAwait(false);
            }
            catch
            {
                // Persistence remains best-effort; failed saving of reading
                // preferences must never interrupt the viewer or editor loop.
            }
        });
    }

    private void OnEditorSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (EditorSession is null)
        {
            return;
        }

        if (e.PropertyName == nameof(EditorSessionViewModel.CurrentPath))
        {
            _currentPath = EditorSession.CurrentPath;
        }

        if (e.PropertyName is nameof(EditorSessionViewModel.SourceText)
            or nameof(EditorSessionViewModel.LastPersistedSource)
            or nameof(EditorSessionViewModel.FileName)
            or nameof(EditorSessionViewModel.CurrentPath))
        {
            RefreshDocumentSummary();
            RefreshWindowTitle();
            UpdateCommandStates();
        }
    }

    private void RefreshDocumentSummary()
    {
        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(TitleFileDisplayName));
        OnPropertyChanged(nameof(WordCount));
        OnPropertyChanged(nameof(ReadTimeMinutes));
        OnPropertyChanged(nameof(ReadingStatusLabel));
        OnPropertyChanged(nameof(IsDirty));
        RaiseEditActionsChanged();
        SyncActiveTabDirtyState();
    }

    private void RefreshWindowTitle()
    {
        var folderSegment = Workspace is { } workspace
            ? $"{workspace.RootDisplayName} — "
            : string.Empty;

        if (State != ViewState.Viewing || string.IsNullOrWhiteSpace(FileName))
        {
            WindowTitle = $"{folderSegment}MarkMello";
            return;
        }

        WindowTitle = $"{TitleFileDisplayName} — {folderSegment}MarkMello";
    }

    private void UpdateCommandStates()
    {
        ReloadCommand.NotifyCanExecuteChanged();
        ToggleEditModeCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        SaveAsCommand.NotifyCanExecuteChanged();
        CheckForUpdatesCommand.NotifyCanExecuteChanged();
        DownloadUpdateCommand.NotifyCanExecuteChanged();
        OpenDownloadedUpdateCommand.NotifyCanExecuteChanged();
        UpdateTextSizeCommandStates();

        OnPropertyChanged(nameof(CanCheckForUpdates));
        OnPropertyChanged(nameof(CanDownloadAvailableUpdate));
        OnPropertyChanged(nameof(CanOpenDownloadedUpdate));
        OnPropertyChanged(nameof(UpdateActionLabel));
        OnPropertyChanged(nameof(UpdateActionCommand));
        OnPropertyChanged(nameof(IsUpdateActionPrimary));
    }

    private void CloseOverlayCore()
    {
        ShellOverlay = ShellOverlayKind.None;
    }

    /// <summary>
    /// Команда, которая меняет документ за окном «Настройки» (⌘W, ⌘E, ⌘R, Ctrl+Tab),
    /// сначала закрывает окно: модальная карточка не остаётся поверх другого содержимого
    /// (ADR-0009 Rule 10). Карточки Aa и поиска эти команды не трогают, как и раньше.
    /// </summary>
    private void CloseAppSettingsWindow()
    {
        if (IsAppSettingsOpen)
        {
            CloseOverlayCore();
        }
    }

    private void CloseAppOverlayCore()
    {
        if (ShellOverlay is ShellOverlayKind.AppMenu or ShellOverlayKind.Settings)
        {
            ShellOverlay = ShellOverlayKind.None;
        }
    }

    /// <summary>Путь активного документа. Публичен: по нему дерево подсвечивает строку.</summary>
    public string? CurrentDocumentPath => EditorSession?.CurrentPath ?? _currentPath ?? Document?.Path;

    private static int CountWords(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var trimmed = text.AsSpan().Trim();
        if (trimmed.IsEmpty)
        {
            return 0;
        }

        var count = 0;
        var inWord = false;
        foreach (var ch in trimmed)
        {
            if (char.IsWhiteSpace(ch))
            {
                inWord = false;
            }
            else if (!inWord)
            {
                inWord = true;
                count++;
            }
        }

        return count;
    }

    private static string? TryGetDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetDirectoryName(path);
        }
        catch
        {
            return null;
        }
    }

    private enum UpdateAction
    {
        Check,
        Checking,
        Download,
        Downloading,
        OpenDownloaded
    }

    private enum PendingDirtyActionKind
    {
        CloseFile,
        CloseFolder,
        Reload,
        LeaveEditMode,
        CloseWindow
    }

    private readonly record struct SaveExecutionOutcome(bool Cancelled, SaveDocumentResult? Result);
}
