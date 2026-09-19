using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkMello.Application.Updates;
using MarkMello.Application.UseCases;
using MarkMello.Domain;
using MarkMello.Presentation.Localization;
using System.ComponentModel;

namespace MarkMello.Presentation.ViewModels;

public partial class ShellViewModel
{
    private PendingDirtyActionKind? _dirtyPromptKind;
    private SaveDocumentResult? _dirtyPromptErrorResult;
    private OpenDocumentResult? _loadErrorResult;
    private UpdateStatusSnapshot _updateStatus = UpdateStatusSnapshot.Default;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSystemLanguageSelected))]
    [NotifyPropertyChangedFor(nameof(IsEnglishLanguageSelected))]
    [NotifyPropertyChangedFor(nameof(IsRussianLanguageSelected))]
    private AppLanguage _language = AppLanguage.System;

    public bool IsSystemLanguageSelected => Language == AppLanguage.System;

    public bool IsEnglishLanguageSelected => Language == AppLanguage.English;

    public bool IsRussianLanguageSelected => Language == AppLanguage.Russian;

    private IReadOnlyList<LanguageSelectionItem>? _languageOptions;

    public IReadOnlyList<LanguageSelectionItem> LanguageOptions =>
        _languageOptions ??= CreateLanguageOptions();

    public LanguageSelectionItem? SelectedLanguageOption
    {
        get => LanguageOptions.FirstOrDefault(option => option.Language == Language);
        set
        {
            if (value is not null)
            {
                ApplyLanguageSelection(value.Language);
            }
        }
    }

    private static readonly string[] LocalizedBindingPropertyNames =
    [
        nameof(AppMenuCloseFolderLabel),
        nameof(AppMenuCloseTab),
        nameof(AppMenuFilesPanel),
        nameof(AppMenuNewDocument),
        nameof(AppMenuOpenFile),
        nameof(AppMenuOpenFolder),
        nameof(AppMenuReload),
        nameof(AppMenuSave),
        nameof(AppMenuSaveAs),
        nameof(AppMenuSettings),
        nameof(AppMenuTooltip),
        nameof(AppSettingsHeader),
        nameof(AppSettingsReadingHintPrefix),
        nameof(AppSettingsReadingHintSuffix),
        nameof(AppSettingsWebsite),
        nameof(DirtyPromptCancel),
        nameof(DirtyPromptDiscard),
        nameof(DirtyPromptSave),
        nameof(DragDropHint),
        nameof(EditDoneLabel),
        nameof(EditDoneTooltip),
        nameof(EditToggleTooltip),
        nameof(EditUnsavedLabel),
        nameof(FindCloseTooltip),
        nameof(FindNextTooltip),
        nameof(FindPlaceholder),
        nameof(FindPreviousTooltip),
        nameof(FindToggleTooltip),
        nameof(LanguageHint),
        nameof(LanguageLabel),
        nameof(LoadErrorOpenAnotherFile),
        nameof(LoadErrorDismiss),
        nameof(LoadErrorTryAgain),
        nameof(NewDocumentTooltip),
        nameof(OverlayCloseSettings),
        nameof(ReadingFontLabel),
        nameof(ReadingFontMono),
        nameof(ReadingFontSans),
        nameof(ReadingFontSerif),
        nameof(ReadingLineHeightLabel),
        nameof(WindowBorderAuto),
        nameof(WindowBorderHint),
        nameof(WindowBorderLabel),
        nameof(WindowBorderOff),
        nameof(WindowBorderOn),
        nameof(ReadingMinimapAuto),
        nameof(ReadingMinimapLabel),
        nameof(ReadingMinimapOff),
        nameof(ReadingMinimapOn),
        nameof(ReadingMoreSettingsHint),
        nameof(ReadingMoreSettingsLink),
        nameof(ReadingSettingsTooltip),
        nameof(ReadingSizeDecreaseTooltip),
        nameof(ReadingSizeIncreaseTooltip),
        nameof(ReadingSizeLabel),
        nameof(ReadingThemeAuto),
        nameof(ReadingThemeDark),
        nameof(ReadingThemeLabel),
        nameof(ReadingThemeLight),
        nameof(ReadingWidthLabel),
        nameof(ReadingWidthMedium),
        nameof(ReadingWidthNarrow),
        nameof(ReadingWidthWide),
        nameof(TitleBarClose),
        nameof(TitleBarMaximize),
        nameof(TitleBarMinimize),
        nameof(TitleBarRestore),
        nameof(UpdatesSectionTitle),
        nameof(WelcomeCreateMd),
        nameof(EmptySurfaceHint),
        nameof(EmptySurfaceTitle),
        nameof(ExternalChangeKeep),
        nameof(ExternalChangeMessage),
        nameof(ExternalChangeReload),
        nameof(ExternalChangeReloadTooltip),
        nameof(ExternalChangeTitle),
        nameof(SidebarCreateTooltip),
        nameof(SidebarFooterLabel),
        nameof(SidebarNewFile),
        nameof(SidebarNewFolder),
        nameof(SidebarOpenAnotherFolder),
        nameof(SidebarSearchEmpty),
        nameof(SidebarSearchMatches),
        nameof(SidebarSearchPlaceholder),
        nameof(SidebarSearchReset),
        nameof(SidebarSearchTruncated),
        nameof(SidebarToggleTooltip),
        nameof(SidebarTooltip),
        nameof(TabClose),
        nameof(TreeDelete),
        nameof(TreeDuplicate),
        nameof(TreeOpenInNewTab),
        nameof(TreeRename),
        nameof(TreeRevealInExplorer),
        nameof(TabsCloseOthers),
        nameof(TabsOverflowHeader),
        nameof(TabsOverflowLabel),
        nameof(WelcomeDropHint),
        nameof(WelcomeOpenFile),
        nameof(WelcomeOpenFolder),
        nameof(WelcomeTagline),
    ];

    public string AppMenuCloseFolderLabel => _localization["AppMenuCloseFolderLabel"];

    /// <summary>
    /// Подвал сайдбара. Формулировка без согласования числительных: множественные формы
    /// потребовали бы механизма выбора форм, которого в плоском словаре нет, а пользы
    /// от «14 документов» против «Документов: 14» — ноль.
    /// </summary>
    public string SidebarFooterLabel
    {
        get
        {
            if (Workspace is not { } workspace)
            {
                return string.Empty;
            }

            var documents = _localization.Format("SidebarFooterDocuments", workspace.LoadedDocumentCount);
            var dirty = OpenDocuments.Tabs.Count(static tab => tab is { BelongsToWorkspace: true, IsDirty: true });

            return dirty == 0
                ? documents
                : documents + " · " + _localization.Format("SidebarFooterDirty", dirty);
        }
    }

    public string SidebarNewFile => _localization["SidebarNewFile"];
    public string SidebarNewFolder => _localization["SidebarNewFolder"];
    public string SidebarCreateTooltip => _localization["SidebarCreateTooltip"];
    public string SidebarOpenAnotherFolder => _localization["SidebarOpenAnotherFolder"];

    /// <summary>
    /// Одна кнопка на оба направления: в шапке сайдбара она скрывает панель, в строке
    /// окна при свёрнутой панели — показывает.
    /// </summary>
    public string SidebarToggleTooltip => _localization.Format(
        IsSidebarCollapsed ? "SidebarShowTooltip" : "SidebarHideTooltip",
        ToggleSidebarShortcut);
    public string TreeDelete => _localization["TreeDelete"];
    public string TreeDuplicate => _localization["TreeDuplicate"];
    public string TreeOpenInNewTab => _localization["TreeOpenInNewTab"];
    public string TreeRename => _localization["TreeRename"];

    /// <summary>Название файлового менеджера зависит от платформы, поэтому ключей три.</summary>
    public string TreeRevealInExplorer => _localization[_platform.PlatformName switch
    {
        "macOS" => "TreeRevealInExplorerMacOS",
        "Linux" => "TreeRevealInExplorerLinux",
        _ => "TreeRevealInExplorerWindows"
    }];

    public string SidebarSearchEmpty => _localization["SidebarSearchEmpty"];
    public string SidebarSearchMatches => _localization["SidebarSearchMatches"];
    public string SidebarSearchPlaceholder => _localization["SidebarSearchPlaceholder"];
    public string SidebarSearchReset => _localization["SidebarSearchReset"];
    public string SidebarSearchTruncated => _localization["SidebarSearchTruncated"];
    public string SidebarTooltip => _localization["SidebarTooltip"];
    public string TabClose => _localization["TabClose"];
    public string TabsCloseOthers => _localization["TabsCloseOthers"];
    public string TabsOverflowHeader => _localization["TabsOverflowHeader"];
    public string EmptySurfaceHint => _localization["EmptySurfaceHint"];
    public string EmptySurfaceTitle => _localization["EmptySurfaceTitle"];

    /// <summary>«ещё N» — счётчик приходит из состава вкладок, поэтому свойство пересчитывается.</summary>
    public string TabsOverflowLabel => _localization.Format("TabsOverflow", OpenDocuments.OverflowTabs.Count);
    public string AppMenuNewDocument => _localization["AppMenuNewDocument"];
    public string AppMenuOpenFile => _localization["AppMenuOpenFile"];
    public string AppMenuOpenFolder => _localization["AppMenuOpenFolder"];
    public string AppMenuSave => _localization["AppMenuSave"];
    public string AppMenuSaveAs => _localization["AppMenuSaveAs"];
    public string AppMenuReload => _localization["AppMenuReload"];
    public string AppMenuFilesPanel => _localization["AppMenuFilesPanel"];
    public string AppMenuCloseTab => _localization["AppMenuCloseTab"];
    public string AppMenuSettings => _localization["AppMenuSettings"];
    public string AppMenuTooltip => _localization["AppMenuTooltip"];
    public string AppSettingsHeader => _localization["AppSettingsHeader"];

    /// <summary>
    /// Подсказка вверху окна «Настройки»: между частями стоит значок Aa — та самая кнопка
    /// над документом, поэтому фраза разрезана на две.
    /// </summary>
    public string AppSettingsReadingHintPrefix => _localization["AppSettingsReadingHintPrefix"];
    public string AppSettingsReadingHintSuffix => _localization["AppSettingsReadingHintSuffix"];
    public string AppSettingsWebsite => _localization["AppSettingsWebsite"];
    public string DirtyPromptCancel => _localization["DirtyPromptCancel"];
    public string DirtyPromptDiscard => _localization["DirtyPromptDiscard"];
    public string DirtyPromptSave => _localization["DirtyPromptSave"];

    /// <summary>
    /// Порядок кнопок диалога правок — как у платформы (ADR-0009 Rule 10). Кнопки встают
    /// в колонки ряда: 0 — у левого края, 1 — распорка, 2–4 — у правого по порядку.
    /// На macOS «Не сохранять» стоит слева особняком, справа «Отмена» и крайняя
    /// «Сохранить»; на Windows и Linux все справа, основная первой: «Сохранить»,
    /// «Не сохранять», «Отмена».
    /// </summary>
    public int DirtyPromptDiscardColumn => UsesMacOSDialogOrder ? 0 : 3;

    public int DirtyPromptCancelColumn => UsesMacOSDialogOrder ? 3 : 4;

    public int DirtyPromptSaveColumn => UsesMacOSDialogOrder ? 4 : 2;

    private bool UsesMacOSDialogOrder => string.Equals(_platform.PlatformName, "macOS", StringComparison.Ordinal);
    public string DragDropHint => _localization["DragDropHint"];
    public string EditToggleTooltip => _localization.Format("EditToggleTooltip", CommandShortcut(Key.E));

    /// <summary>«Готово» — та же команда, что карандаш, поэтому и сочетание то же.</summary>
    public string EditDoneLabel => _localization["EditDone"];
    public string EditDoneTooltip => _localization.Format("EditDoneTooltip", CommandShortcut(Key.E));
    public string EditUnsavedLabel => _localization["EditUnsaved"];
    public string FindCloseTooltip => _localization.Format("FindCloseTooltip", KeyShortcut(Key.Escape));
    public string FindNextTooltip => _localization.Format("FindNextTooltip", KeyShortcut(Key.Enter));
    public string FindPlaceholder => _localization["FindPlaceholder"];
    public string FindPreviousTooltip => _localization.Format("FindPreviousTooltip", KeyShortcut(Key.Enter, KeyModifiers.Shift));
    public string FindToggleTooltip => _localization.Format("FindToggleTooltip", CommandShortcut(Key.F));

    /// <summary>«+» после вкладок — то же, что сочетание, поэтому оно и стоит в подсказке.</summary>
    public string NewDocumentTooltip => _localization.Format("NewDocumentTooltip", CommandShortcut(Key.N));
    public string LanguageHint => _localization["LanguageHint"];
    public string LanguageLabel => _localization["LanguageLabel"];
    public string LoadErrorOpenAnotherFile => _localization["LoadErrorOpenAnotherFile"];
    public string LoadErrorDismiss => _localization["LoadErrorDismiss"];
    public string LoadErrorTryAgain => _localization["LoadErrorTryAgain"];
    public string OverlayCloseSettings => _localization["OverlayCloseSettings"];
    public string ReadingFontLabel => _localization["ReadingFontLabel"];
    public string ReadingFontMono => _localization["ReadingFontMono"];
    public string ReadingFontSans => _localization["ReadingFontSans"];
    public string ReadingFontSerif => _localization["ReadingFontSerif"];
    public string ReadingLineHeightLabel => _localization["ReadingLineHeightLabel"];
    public string WindowBorderAuto => _localization["WindowBorderAuto"];
    public string WindowBorderHint => _localization["WindowBorderHint"];
    public string WindowBorderLabel => _localization["WindowBorderLabel"];
    public string WindowBorderOff => _localization["WindowBorderOff"];
    public string WindowBorderOn => _localization["WindowBorderOn"];
    public string ReadingMinimapAuto => _localization["ReadingMinimapAuto"];
    public string ReadingMinimapLabel => _localization["ReadingMinimapLabel"];
    public string ReadingMinimapOff => _localization["ReadingMinimapOff"];
    public string ReadingMinimapOn => _localization["ReadingMinimapOn"];
    public string ReadingMoreSettingsHint => _localization["ReadingMoreSettingsHint"];
    public string ReadingMoreSettingsLink => _localization["ReadingMoreSettingsLink"];
    public string ReadingSettingsTooltip => _localization["ReadingSettingsTooltip"];
    public string ReadingSizeDecreaseTooltip => _localization.Format("ReadingSizeDecreaseTooltip", CommandShortcut(Key.OemMinus));
    public string ReadingSizeIncreaseTooltip => _localization.Format("ReadingSizeIncreaseTooltip", CommandShortcut(Key.OemPlus));
    public string ReadingSizeLabel => _localization["ReadingSizeLabel"];
    public string ReadingThemeAuto => _localization["ReadingThemeAuto"];
    public string ReadingThemeDark => _localization["ReadingThemeDark"];
    public string ReadingThemeLabel => _localization["ReadingThemeLabel"];
    public string ReadingThemeLight => _localization["ReadingThemeLight"];
    public string ReadingWidthLabel => _localization["ReadingWidthLabel"];
    public string ReadingWidthMedium => _localization["ReadingWidthMedium"];
    public string ReadingWidthNarrow => _localization["ReadingWidthNarrow"];
    public string ReadingWidthWide => _localization["ReadingWidthWide"];
    public string TitleBarClose => _localization["TitleBarClose"];
    public string TitleBarMaximize => _localization["TitleBarMaximize"];
    public string TitleBarMinimize => _localization["TitleBarMinimize"];
    public string TitleBarRestore => _localization["TitleBarRestore"];

    /// <summary>Подзаголовок блока обновлений в окне «Настройки» — капителью, как на холсте.</summary>
    public string UpdatesSectionTitle => _localization["UpdatesLabel"].ToUpper(_localization.Culture);
    public string WelcomeCreateMd => _localization["WelcomeCreateMd"];
    public string WelcomeDropHint => _localization["WelcomeDropHint"];
    public string WelcomeOpenFile => _localization["WelcomeOpenFile"];
    public string WelcomeOpenFolder => _localization["WelcomeOpenFolder"];
    public string WelcomeTagline => _localization["WelcomeTagline"];

    /// <summary>
    /// Подписи сочетаний для меню и стартового экрана: ⌘ на macOS, Ctrl на Windows и Linux.
    /// От языка не зависят, поэтому в <see cref="LocalizedBindingPropertyNames"/> их нет.
    /// </summary>
    public string OpenFileShortcut => CommandShortcut(Key.O);

    public string OpenFolderShortcut => CommandShortcut(Key.O, KeyModifiers.Shift);

    public string ToggleSidebarShortcut => CommandShortcut(Key.B);

    public string NewDocumentShortcut => CommandShortcut(Key.N);

    /// <summary>Сочетание сохранения — плашка рядом с «Не сохранено» в строке окна.</summary>
    public string SaveShortcut => CommandShortcut(Key.S);

    public string SaveAsShortcut => CommandShortcut(Key.S, KeyModifiers.Shift);

    public string ReloadShortcut => CommandShortcut(Key.R);

    public string CloseTabShortcut => CommandShortcut(Key.W);

    /// <summary>Сочетание настроек приложения — плашка в нижней строке карточки Aa.</summary>
    public string SettingsShortcut => CommandShortcut(Key.OemComma);

    /// <summary>
    /// Клавиши диалогов — в тултипах их кнопок: Enter подтверждает, Esc отменяет,
    /// ⌘⌫ (Ctrl+Backspace) — «Не сохранять».
    /// </summary>
    public string DialogConfirmShortcut => KeyShortcut(Key.Enter);

    public string DialogCancelShortcut => KeyShortcut(Key.Escape);

    public string DirtyPromptDiscardShortcut => CommandShortcut(Key.Back);

    /// <summary>Клавиши «Открыть файл» по отдельности — на стартовом экране каждая в своей плашке.</summary>
    public IReadOnlyList<string> OpenFileShortcutKeys
        => ShortcutLabel.Keys(ShortcutLabel.Command(Key.O, _platform.PlatformName), _platform.PlatformName);

    private string CommandShortcut(Key key, KeyModifiers extra = KeyModifiers.None)
        => ShortcutLabel.Format(ShortcutLabel.Command(key, _platform.PlatformName, extra), _platform.PlatformName);

    /// <summary>Сочетание без командной клавиши — например, ↵ и ⇧↵ в поле поиска.</summary>
    private string KeyShortcut(Key key, KeyModifiers modifiers = KeyModifiers.None)
        => ShortcutLabel.Format(new KeyGesture(key, modifiers), _platform.PlatformName);

    public string WordCountStatusLabel => _localization.Format("StatusWordCount", WordCount);

    public string ReadTimeStatusLabel => _localization.Format("StatusReadTime", ReadTimeMinutes);

    [RelayCommand]
    private void SelectSystemLanguage() => ApplyLanguageSelection(AppLanguage.System);

    [RelayCommand]
    private void SelectEnglishLanguage() => ApplyLanguageSelection(AppLanguage.English);

    [RelayCommand]
    private void SelectRussianLanguage() => ApplyLanguageSelection(AppLanguage.Russian);

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!IsLocalizationChangeNotification(e.PropertyName))
        {
            return;
        }

        RefreshLocalizedProperties();
    }

    partial void OnLanguageChanged(AppLanguage value)
    {
        OnPropertyChanged(nameof(SelectedLanguageOption));
    }

    private static bool IsLocalizationChangeNotification(string? propertyName)
        => string.IsNullOrEmpty(propertyName)
           || propertyName == nameof(ILocalizationService.SelectedLanguage)
           || propertyName == nameof(ILocalizationService.EffectiveLanguage)
           || propertyName == nameof(ILocalizationService.Culture)
           || propertyName == "Item"
           || propertyName == "Item[]";

    private void ApplyLanguageSelection(AppLanguage language, bool persist = true)
    {
        var normalized = NormalizeLanguage(language);
        if (Language == normalized && _localization.SelectedLanguage == normalized)
        {
            return;
        }

        Language = normalized;
        _localization.SetLanguage(normalized);
        UpdateDraftFileName();

        if (persist)
        {
            PersistLanguage(normalized);
        }
    }

    private void PersistLanguage(AppLanguage language)
    {
        try
        {
            _settings.SaveLanguageAsync(language).AsTask().GetAwaiter().GetResult();
        }
        catch
        {
            // Language persistence remains best-effort for the same reason
            // as the rest of the lightweight app settings.
        }
    }

    private void RefreshLocalizedProperties()
    {
        _languageOptions = CreateLanguageOptions();

        NotifyLocalizedBindingPropertiesChanged();
        EditorSession?.RefreshLocalizedProperties();

        OnPropertyChanged(nameof(FindResultLabel));
        OnPropertyChanged(nameof(UpdateActionLabel));
        OnPropertyChanged(nameof(IsSystemLanguageSelected));
        OnPropertyChanged(nameof(IsEnglishLanguageSelected));
        OnPropertyChanged(nameof(IsRussianLanguageSelected));
        OnPropertyChanged(nameof(LanguageOptions));
        OnPropertyChanged(nameof(SelectedLanguageOption));
        OnPropertyChanged(nameof(WordCountStatusLabel));
        OnPropertyChanged(nameof(ReadTimeStatusLabel));
        OnPropertyChanged(nameof(FontSizeLabel));
        OnPropertyChanged(nameof(LineHeightLabel));

        RefreshDirtyPromptTexts();
        RefreshLoadErrorTexts();
        RefreshUpdateStatusTexts();
    }

    private void NotifyLocalizedBindingPropertiesChanged()
    {
        foreach (var propertyName in LocalizedBindingPropertyNames)
        {
            OnPropertyChanged(propertyName);
        }
    }

    private IReadOnlyList<LanguageSelectionItem> CreateLanguageOptions() =>
    [
        new(AppLanguage.System, _localization["LanguageSystem"]),
        new(AppLanguage.English, _localization["LanguageEnglish"]),
        new(AppLanguage.Russian, _localization["LanguageRussian"])
    ];

    private void SetDirtyPrompt(PendingDirtyActionKind kind)
    {
        _dirtyPromptKind = kind;
        _dirtyPromptErrorResult = null;
        RefreshDirtyPromptTexts();
        IsDirtyPromptOpen = true;
    }

    private void SetDirtyPromptError(SaveDocumentResult? result)
    {
        _dirtyPromptErrorResult = result;
        RefreshDirtyPromptTexts();
    }

    /// <summary>
    /// Заголовок называет файл — грязных вкладок может быть несколько, — а текст говорит,
    /// когда пропадут правки, если их не сохранить.
    /// </summary>
    private void RefreshDirtyPromptTexts()
    {
        DirtyPromptTitle = _dirtyPromptKind is null
            ? string.Empty
            : Format("DirtyPromptTitle", _dirtyPromptTab?.Title ?? FileName);

        DirtyPromptMessage = _dirtyPromptKind switch
        {
            PendingDirtyActionKind.CloseFile => _localization["DirtyPromptCloseFile"],
            PendingDirtyActionKind.CloseFolder => _localization["DirtyPromptCloseFolder"],
            PendingDirtyActionKind.Reload => _localization["DirtyPromptReload"],
            PendingDirtyActionKind.LeaveEditMode => _localization["DirtyPromptLeaveEditMode"],
            PendingDirtyActionKind.CloseWindow => _localization["DirtyPromptCloseWindow"],
            _ => string.Empty
        };

        DirtyPromptErrorMessage = GetSaveFailureMessage(_dirtyPromptErrorResult);
    }

    private void ClearDirtyPromptState()
    {
        _pendingDirtyAction = null;
        _dirtyPromptTab = null;
        _dirtyPromptKind = null;
        _dirtyPromptErrorResult = null;
        IsDirtyPromptOpen = false;
        DirtyPromptTitle = string.Empty;
        DirtyPromptMessage = string.Empty;
        DirtyPromptErrorMessage = string.Empty;
    }

    private void SetLoadError(OpenDocumentResult result)
    {
        _loadErrorResult = result;
        RefreshLoadErrorTexts();
        State = ViewState.LoadError;
    }

    /// <summary>
    /// Экран ошибки по A-LoadError: заголовок говорит, что случилось, пояснение — почему
    /// и что делать, путь отдельной строкой, чтобы его можно было скопировать.
    /// </summary>
    private void RefreshLoadErrorTexts()
    {
        if (_loadErrorResult is null)
        {
            return;
        }

        ErrorKind = _loadErrorResult switch
        {
            OpenDocumentResult.NotFound => LoadErrorKind.NotFound,
            OpenDocumentResult.AccessDenied => LoadErrorKind.AccessDenied,
            OpenDocumentResult.UnsupportedType => LoadErrorKind.UnsupportedType,
            _ => LoadErrorKind.ReadFailure
        };

        ErrorTitle = _loadErrorResult switch
        {
            OpenDocumentResult.NotFound => _localization["ErrorFileNotFoundTitle"],
            OpenDocumentResult.AccessDenied => _localization["ErrorAccessDeniedTitle"],
            OpenDocumentResult.ReadError => _localization["ErrorReadFailureTitle"],
            OpenDocumentResult.UnsupportedType => _localization["ErrorUnsupportedTypeTitle"],
            _ => string.Empty
        };

        ErrorDescription = _loadErrorResult switch
        {
            OpenDocumentResult.NotFound => _localization["ErrorFileNotFoundDetails"],
            OpenDocumentResult.AccessDenied => _localization["ErrorAccessDeniedDetails"],
            OpenDocumentResult.ReadError read => read.Message,
            OpenDocumentResult.UnsupportedType => FormatSupportedExtensions(),
            _ => string.Empty
        };

        ErrorPath = FormatErrorPath(GetFailedPath(_loadErrorResult));
    }

    /// <summary>
    /// Путь на экране ошибки копируют в терминал или файловый менеджер. На macOS и Linux
    /// <c>~</c> там понимают, и путь сокращается, как в тултипе вкладки; в Проводнике и cmd
    /// <c>~\Documents</c> не откроется — там путь полный.
    /// </summary>
    private string FormatErrorPath(string? path)
        => string.Equals(_platform.PlatformName, "Windows", StringComparison.Ordinal)
            ? path ?? string.Empty
            : BuildTabTooltip(path);

    /// <summary>«.md, .markdown и .txt» — последний союзом, по правилам языка.</summary>
    private string FormatSupportedExtensions()
    {
        var extensions = SupportedDocumentTypes.Extensions;
        return extensions.Count == 1
            ? _localization.Format("ErrorUnsupportedTypeSingleDetails", extensions[0])
            : _localization.Format(
                "ErrorUnsupportedTypeDetails",
                string.Join(", ", extensions.Take(extensions.Count - 1)),
                extensions[^1]);
    }

    /// <summary>Ошибка папки — тот же экран, но без пояснения и без «Повторить».</summary>
    private void SetFolderLoadError(string titleKey, string path)
    {
        _loadErrorResult = null;
        ErrorKind = LoadErrorKind.Folder;
        ErrorTitle = _localization[titleKey];
        ErrorDescription = string.Empty;
        ErrorPath = FormatErrorPath(path);
        State = ViewState.LoadError;
    }

    private void ClearLoadError()
    {
        _loadErrorResult = null;
        ErrorKind = LoadErrorKind.None;
        ErrorTitle = string.Empty;
        ErrorDescription = string.Empty;
        ErrorPath = string.Empty;
    }

    private void SetUpdateStatus(UpdateStatusSnapshot status)
    {
        _updateStatus = status;
        RefreshUpdateStatusTexts();
    }

    private void RefreshUpdateStatusTexts()
    {
        UpdateStatusTitle = _updateStatus switch
        {
            UpdateStatusSnapshot.DefaultState => _localization["UpdateDefaultTitle"],
            UpdateStatusSnapshot.CheckingState => _localization["UpdateCheckingTitle"],
            UpdateStatusSnapshot.SourceNotConfiguredState => _localization["UpdateUnavailableTitle"],
            UpdateStatusSnapshot.UnsupportedPlatformState => _localization["UpdateUnsupportedPlatformTitle"],
            UpdateStatusSnapshot.UpToDateState => _localization["UpdateUpToDateTitle"],
            UpdateStatusSnapshot.UpdateAvailableState available => _localization.Format("UpdateAvailableTitle", available.Package.ReleaseVersion),
            UpdateStatusSnapshot.CheckFailedState => _localization["UpdateCheckFailedTitle"],
            UpdateStatusSnapshot.DownloadingState downloading => _localization.Format("UpdateDownloadTitle", downloading.Package.ReleaseVersion),
            UpdateStatusSnapshot.DownloadReadyState => _localization["UpdateReadyTitle"],
            UpdateStatusSnapshot.DownloadFailedState => _localization["UpdateDownloadFailedTitle"],
            UpdateStatusSnapshot.NativeFlowStartedState => _localization["UpdateNativeFlowStartedTitle"],
            UpdateStatusSnapshot.OpenDownloadedFailedState => _localization["UpdateOpenDownloadedFailedTitle"],
            _ => _localization["UpdateDefaultTitle"]
        };

        UpdateStatusMessage = _updateStatus switch
        {
            UpdateStatusSnapshot.DefaultState => _localization["UpdateDefaultMessage"],
            UpdateStatusSnapshot.CheckingState => _localization["UpdateCheckingMessage"],
            UpdateStatusSnapshot.SourceNotConfiguredState => _localization["UpdateUnavailableMessage"],
            UpdateStatusSnapshot.UnsupportedPlatformState unsupported => _localization.Format(
                "UpdateUnsupportedPlatformMessage",
                unsupported.PlatformName,
                unsupported.ArchitectureName),
            UpdateStatusSnapshot.UpToDateState upToDate => _localization.Format(
                "UpdateUpToDateMessage",
                upToDate.CurrentVersion,
                upToDate.LatestVersion),
            UpdateStatusSnapshot.UpdateAvailableState available => _localization.Format(
                "UpdateAvailableMessage",
                available.Package.AssetName,
                available.Package.PlatformName,
                available.Package.ArchitectureName),
            UpdateStatusSnapshot.CheckFailedState failed => failed.Details,
            UpdateStatusSnapshot.DownloadingState downloading => _localization.Format(
                "UpdateDownloadMessage",
                downloading.Package.AssetName),
            UpdateStatusSnapshot.DownloadReadyState ready => GetUpdateReadyMessage(ready.Package, ready.DownloadedFilePath),
            UpdateStatusSnapshot.DownloadFailedState failed => failed.Details,
            UpdateStatusSnapshot.NativeFlowStartedState started => GetNativeFlowStartedMessage(started.Package),
            UpdateStatusSnapshot.OpenDownloadedFailedState failed => failed.Details,
            _ => _localization["UpdateDefaultMessage"]
        };
    }

    private void UpdateDraftFileName()
    {
        if (EditorSession is null || !string.IsNullOrWhiteSpace(EditorSession.CurrentPath))
        {
            return;
        }

        EditorSession.UpdateDraftFileName(GetUntitledFileName());
        RefreshDocumentSummary();
        RefreshWindowTitle();
    }

    private string GetUntitledFileName() => _localization["UntitledFileName"];

    private string GetSaveFailureMessage(SaveDocumentResult? result)
        => result switch
        {
            SaveDocumentResult.InvalidPath invalidPath => _localization.Format("SaveInvalidPath", invalidPath.Path),
            SaveDocumentResult.AccessDenied accessDenied => _localization.Format("SaveAccessDenied", accessDenied.Path),
            SaveDocumentResult.WriteError writeError => _localization.Format("SaveWriteFailure", writeError.Message),
            null => string.Empty,
            _ => _localization["SaveGenericFailure"]
        };

    private string GetUpdateReadyMessage(AppUpdatePackage package, string downloadedFilePath)
    {
        var downloadedFileName = Path.GetFileName(downloadedFilePath);

        return package.InstallAction switch
        {
            AppUpdateInstallAction.LaunchInstaller => _localization.Format("UpdateReadyLaunchInstaller", downloadedFileName),
            AppUpdateInstallAction.OpenDiskImage => _localization.Format("UpdateReadyOpenDmg", downloadedFileName),
            AppUpdateInstallAction.RevealFile => _localization.Format("UpdateReadyRevealAppImage", downloadedFileName),
            _ => _localization.Format("UpdateReadyGeneric", downloadedFileName)
        };
    }

    private string GetNativeFlowStartedMessage(AppUpdatePackage package)
        => package.InstallAction switch
        {
            AppUpdateInstallAction.LaunchInstaller => _localization["UpdateNativeFlowStartedLaunchInstaller"],
            AppUpdateInstallAction.OpenDiskImage => _localization["UpdateNativeFlowStartedOpenDmg"],
            AppUpdateInstallAction.RevealFile => _localization["UpdateNativeFlowStartedRevealAppImage"],
            _ => _localization["UpdateOpenDownloaded"]
        };

    private string NormalizeSuggestedFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return GetUntitledFileName();
        }

        return SupportedDocumentTypes.IsSupportedPath(fileName)
            ? fileName
            : $"{fileName}.md";
    }

    private static AppLanguage NormalizeLanguage(AppLanguage language)
        => language switch
        {
            AppLanguage.English => AppLanguage.English,
            AppLanguage.Russian => AppLanguage.Russian,
            _ => AppLanguage.System
        };

    private abstract record UpdateStatusSnapshot
    {
        public static readonly UpdateStatusSnapshot Default = new DefaultState();

        public sealed record DefaultState : UpdateStatusSnapshot;

        public sealed record CheckingState : UpdateStatusSnapshot;

        public sealed record SourceNotConfiguredState : UpdateStatusSnapshot;

        public sealed record UnsupportedPlatformState(string PlatformName, string ArchitectureName) : UpdateStatusSnapshot;

        public sealed record UpToDateState(string CurrentVersion, string LatestVersion) : UpdateStatusSnapshot;

        public sealed record UpdateAvailableState(AppUpdatePackage Package) : UpdateStatusSnapshot;

        public sealed record CheckFailedState(string Details) : UpdateStatusSnapshot;

        public sealed record DownloadingState(AppUpdatePackage Package) : UpdateStatusSnapshot;

        public sealed record DownloadReadyState(AppUpdatePackage Package, string DownloadedFilePath) : UpdateStatusSnapshot;

        public sealed record DownloadFailedState(string Details) : UpdateStatusSnapshot;

        public sealed record NativeFlowStartedState(AppUpdatePackage Package) : UpdateStatusSnapshot;

        public sealed record OpenDownloadedFailedState(string Details) : UpdateStatusSnapshot;
    }
}

public sealed record LanguageSelectionItem(AppLanguage Language, string Label)
{
    public override string ToString() => Label;
}

