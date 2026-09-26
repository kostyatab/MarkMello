using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using MarkMello.Domain;
using MarkMello.Presentation.ViewModels;

namespace MarkMello.Presentation.Localization;

public sealed class LocalizationService : ObservableObject, ILocalizationService
{
    private static readonly Dictionary<string, string> English = new(StringComparer.Ordinal)
    {
        ["WelcomeTagline"] = "A quiet place to read Markdown.",
        ["WelcomeNewDocument"] = "New Document",
        ["WelcomeOpenFile"] = "Open File…",
        ["RecentTitle"] = "RECENT",
        ["RecentClear"] = "Clear",
        ["RecentToday"] = "today",
        ["RecentYesterday"] = "yesterday",
        ["RecentDateFormat"] = "MMM d",
        ["RecentDateWithYearFormat"] = "MMM d, yyyy",
        ["RecentFileNotFoundTitle"] = "File not found",
        ["RecentFolderNotFoundTitle"] = "Folder not found",
        ["RecentRemoveBody"] = "Remove “{0}” from Recent?",
        ["RecentRemoveConfirm"] = "Remove",
        ["RecentRemoveCancel"] = "Cancel",
        ["WelcomeDropHint"] = "or drop a .md file or a folder here",
        ["TitleBarMinimize"] = "Minimize",
        ["TitleBarMaximize"] = "Maximize",
        ["TitleBarRestore"] = "Restore",
        ["TitleBarClose"] = "Close",
        ["AppMenuTooltip"] = "Menu",
        ["EditToggleTooltip"] = "Toggle edit mode ({0})",
        ["EditDone"] = "Done",
        ["EditDoneTooltip"] = "Finish editing ({0})",
        ["EditUnsaved"] = "Unsaved",
        ["ReadingSettingsTooltip"] = "View: theme, font, size",
        ["OverlayCloseSettings"] = "Close settings",
        ["AppSettingsHeader"] = "Settings",
        ["AppMenuNewDocument"] = "New Document",
        ["AppMenuOpenFile"] = "Open File…",
        ["AppMenuOpenFolder"] = "Open Folder…",
        ["AppMenuSave"] = "Save",
        ["AppMenuSaveAs"] = "Save As…",
        ["AppMenuReload"] = "Reload from Disk",
        ["AppMenuFilesPanel"] = "Files Panel",
        ["AppMenuCloseTab"] = "Close Tab",
        ["AppMenuSettings"] = "Settings…",
        ["AppMenuAbout"] = $"About {AppProductInfo.Name}",
        ["AboutWindowTitle"] = $"About {AppProductInfo.Name}",
        ["AboutVersion"] = "Version {0}",
        ["AboutForkAttribution"] = "Fork of MarkMello © 2026 MarkMello contributors",
        ["AppSettingsReadingHintPrefix"] = "Theme, font and text size live in the",
        ["AppSettingsReadingHintSuffix"] = "card above the document.",
        ["LanguageLabel"] = "Language",
        ["LanguageHint"] = "Shell and dialogs",
        ["LanguageSystem"] = "System",
        ["LanguageEnglish"] = "English",
        ["LanguageRussian"] = "Russian",
        ["UpdatesLabel"] = "Updates",
        ["ReadingThemeLabel"] = "Theme",
        ["ReadingThemeAuto"] = "Auto",
        ["ReadingThemeLight"] = "Light",
        ["ReadingThemeDark"] = "Dark",
        ["ReadingFontLabel"] = "Font",
        ["ReadingFontSerif"] = "Serif",
        ["ReadingFontSans"] = "Sans",
        ["ReadingFontMono"] = "Mono",
        ["ReadingSizeLabel"] = "Text size",
        ["ReadingFontSizeValue"] = "{0} px",
        ["ReadingSizeDecreaseTooltip"] = "Smaller ({0})",
        ["ReadingSizeIncreaseTooltip"] = "Larger ({0})",
        ["ReadingLineHeightLabel"] = "Line height",
        ["ReadingWidthLabel"] = "Line width",
        ["ReadingWidthNarrow"] = "Narrow",
        ["ReadingWidthMedium"] = "Medium",
        ["ReadingWidthWide"] = "Wide",
        ["WindowBorderLabel"] = "Window border",
        ["WindowBorderHint"] = "Outline the window edge",
        ["WindowBorderAuto"] = "Auto",
        ["WindowBorderOn"] = "On",
        ["WindowBorderOff"] = "Off",
        ["ReadingMoreSettingsHint"] = "Language, updates, version",
        ["ReadingMoreSettingsLink"] = "Settings…",
        ["StatusWordsOne"] = "{0:N0} word",
        ["StatusWordsFew"] = "{0:N0} words",
        ["StatusWordsMany"] = "{0:N0} words",
        ["StatusReadMinutes"] = "{0} min",
        ["DragDropHint"] = "Release to open",
        ["DropFileDetails"] = "{0} will open in a new tab",
        ["DropFolderNewWindowDetails"] = "Folder {0} will open in a new window",
        ["DropFolderOpenElsewhereDetails"] = "Folder {0} is already open in another window",
        ["DropFolderThisWindowDetails"] = "Folder {0} will open in this window",
        ["DirtyPromptCancel"] = "Cancel",
        ["DirtyPromptDiscard"] = "Discard",
        ["DirtyPromptSave"] = "Save",
        ["LoadErrorOpenAnotherFile"] = "Open another file…",
        ["LoadErrorTryAgain"] = "Try again",
        ["LoadErrorDismiss"] = "close",
        ["EditorBoldTooltip"] = "Bold",
        ["EditorItalicTooltip"] = "Italic",
        ["EditorCodeTooltip"] = "Code",
        ["EditorLinkTooltip"] = "Link",
        ["EditorListTooltip"] = "List",
        ["EditorQuoteTooltip"] = "Quote",
        ["EditorProtectedImageDataMessage"] = "Embedded image data can only be edited as a whole line.",
        ["ContextCopy"] = "Copy",
        ["ContextSelectAll"] = "Select all",
        ["ContextCopyLink"] = "Copy link",
        ["ContextCopyLinks"] = "Copy links",
        ["ContextCopyTelegramMarkdown"] = "Copy selection as Telegram Markdown",
        ["CodeCopyTooltip"] = "Copy code",
        ["CodeCopiedStatus"] = "Code copied",
        ["AlertNote"] = "Note",
        ["AlertTip"] = "Tip",
        ["AlertImportant"] = "Important",
        ["AlertWarning"] = "Warning",
        ["AlertCaution"] = "Caution",
        ["ImageLoading"] = "Loading…",
        ["DiagramRenderFailed"] = "{0} diagram could not be rendered",
        ["DiagramSvgUnsupported"] = "{0} diagram rendered, but its SVG is not yet supported by the built-in viewer",
        ["DiagramEmpty"] = "The diagram came out empty. Check its syntax.",
        ["EditorSourceLabel"] = "Markdown",
        ["EditorPreviewLabel"] = "Preview",
        ["FindPlaceholder"] = "Find in document",
        ["FindPreviousTooltip"] = "Previous match ({0})",
        ["FindToggleTooltip"] = "Find in document ({0})",
        ["FindNextTooltip"] = "Next match ({0})",
        ["FindCloseTooltip"] = "Close search ({0})",
        ["FindResultCount"] = "{0} of {1}",
        ["FindNoResults"] = "No results",
        ["UpdateCheckNow"] = "Check now",
        ["UpdateChecking"] = "Checking...",
        ["UpdateDownload"] = "Download",
        ["UpdateDownloading"] = "Downloading...",
        ["UpdateOpenDownloaded"] = "Open update",
        ["UpdateLaunchInstaller"] = "Launch installer",
        ["UpdateOpenDmg"] = "Open DMG",
        ["UpdateRevealAppImage"] = "Reveal AppImage",
        ["UpdateDefaultTitle"] = "Manual checks",
        ["UpdateDefaultMessage"] = $"{AppProductInfo.Name} doesn't go online at startup.",
        ["UpdateCheckingTitle"] = "Checking GitHub Releases",
        ["UpdateCheckingMessage"] = "Looking for a newer packaged build for this device.",
        ["UpdateUnavailableTitle"] = "Updates unavailable",
        ["UpdateUnavailableMessage"] = "This build has no GitHub Releases source configured yet.",
        ["UpdateUnsupportedPlatformTitle"] = "No packaged update for this runtime",
        ["UpdateUnsupportedPlatformMessage"] = "{0} {1} is not in the current release matrix.",
        ["UpdateUpToDateTitle"] = "You're up to date",
        ["UpdateUpToDateMessage"] = "Current build {0} already matches the latest published release ({1}).",
        ["UpdateAvailableTitle"] = "Update {0} available",
        ["UpdateAvailableMessage"] = "{0} is ready for {1} {2}.",
        ["UpdateCheckFailedTitle"] = "Couldn't check for updates",
        ["UpdateDownloadTitle"] = "Downloading {0}",
        ["UpdateDownloadMessage"] = "Saving {0} from GitHub Releases.",
        ["UpdateReadyTitle"] = "Update ready",
        ["UpdateReadyLaunchInstaller"] = "{0} downloaded. Launch the installer to continue the native Windows upgrade flow.",
        ["UpdateReadyOpenDmg"] = "{0} downloaded. Open the DMG to continue with the native macOS install flow.",
        ["UpdateReadyRevealAppImage"] = "{0} downloaded. Reveal the AppImage, then replace your previous binary when you're ready.",
        ["UpdateReadyGeneric"] = "{0} downloaded.",
        ["UpdateDownloadFailedTitle"] = "Download failed",
        ["UpdateNativeFlowStartedTitle"] = "Native update flow started",
        ["UpdateNativeFlowStartedLaunchInstaller"] = "Installer launched. Follow the native upgrade flow.",
        ["UpdateNativeFlowStartedOpenDmg"] = "DMG opened. Continue with the native macOS install flow.",
        ["UpdateNativeFlowStartedRevealAppImage"] = "The AppImage was revealed in your file manager.",
        ["UpdateOpenDownloadedFailedTitle"] = "Couldn't open the downloaded update",
        ["ErrorFileNotFoundTitle"] = "Couldn't find that file",
        ["ErrorAccessDeniedTitle"] = "Access denied",
        ["ErrorReadFailureTitle"] = "Couldn't read the file",
        ["ErrorUnsupportedTypeTitle"] = "This isn't Markdown",
        ["ErrorFileNotFoundDetails"] = "It may have been moved, renamed, or deleted.",
        ["ErrorAccessDeniedDetails"] = $"{AppProductInfo.Name} doesn't have permission to read this file.",
        ["ErrorUnsupportedTypeDetails"] = $"{AppProductInfo.Name} opens {{0}} and {{1}} files.",
        ["ErrorUnsupportedTypeSingleDetails"] = $"{AppProductInfo.Name} opens {{0}} files.",
        ["DirtyPromptTitle"] = "Save changes to \"{0}\"?",
        ["DirtyPromptCloseFile"] = "Otherwise your changes will be lost when the tab closes.",
        ["DirtyPromptCloseFolder"] = "Otherwise your changes will be lost when the folder closes.",
        ["DirtyPromptReload"] = "Otherwise your changes will be lost on reload.",
        ["DirtyPromptLeaveEditMode"] = "Otherwise your changes will be lost when you leave editing.",
        ["DirtyPromptCloseWindow"] = $"Otherwise your changes will be lost when you quit {AppProductInfo.Name}.",
        ["SaveInvalidPath"] = "Couldn't save to this path: {0}",
        ["SaveAccessDenied"] = "Access denied: {0}",
        ["SaveWriteFailure"] = "Couldn't save the document: {0}",
        ["SaveGenericFailure"] = "Couldn't save the document.",
        ["OpenDialogTitle"] = "Open Markdown file",
        ["OpenFolderDialogTitle"] = "Open folder",
        ["ExternalChangeTitle"] = "{0} changed on disk.",
        ["ExternalChangeMessage"] = "Another program saved its version, and your edits aren't saved yet.",
        ["ExternalChangeReloadTooltip"] = "Your edits will be lost",
        ["ExternalChangeReload"] = "Load from disk",
        ["ExternalChangeKeep"] = "Keep my edits",
        ["TabDeletedSuffix"] = "(deleted)",
        ["SidebarHideTooltip"] = "Hide file panel ({0})",
        ["SidebarShowTooltip"] = "Show file panel ({0})",
        ["SidebarNewFile"] = "New File",
        ["SidebarNewFolder"] = "New Folder",
        ["SidebarCreateTooltip"] = "New file or folder",
        ["SidebarOpenAnotherFolder"] = "Open Another Folder…",
        ["TreeRename"] = "Rename",
        ["TreeDuplicate"] = "Duplicate",
        ["TreeDelete"] = "Delete",
        ["TreeOpenInNewTab"] = "Open in New Tab",
        ["TreeRevealInExplorerWindows"] = "Show in Explorer",
        ["TreeRevealInExplorerMacOS"] = "Show in Finder",
        ["TreeRevealInExplorerLinux"] = "Show in File Manager",
        ["TreeNameTaken"] = "A file with this name already exists",
        ["TreeFolderNameTaken"] = "A folder with this name already exists",
        ["TreeInvalidChars"] = """These characters aren't allowed: \ / : * ? " < > |""",
        ["TreeReservedName"] = "This name is reserved by the system",
        ["TreeOperationFailed"] = "The operation failed",
        ["DeleteFileTitle"] = "Delete \"{0}\"?",
        ["DeleteFileBody"] = "The file will be moved to the recycle bin. If it is open in a tab, that tab will close.",
        ["DeleteFolderTitle"] = "Delete folder \"{0}\"?",
        ["DeleteFolderBody"] = "The folder will be moved to the recycle bin.",
        ["DeleteFolderNonEmptyTitle"] = "Delete folder \"{0}\" and everything in it?",
        ["DeleteFolderNonEmptyBody"] = "The folder has {1} items. Everything will be moved to the recycle bin. Open tabs from this folder will close.",
        ["DeletePermanentBody"] = "The recycle bin is not available here. The item will be deleted permanently and cannot be restored.",
        ["DeleteUnsavedChangesWarning"] = "Unsaved changes in \"{0}\" will be lost.",
        ["DeleteConfirm"] = "Delete",
        ["DeletePermanentConfirm"] = "Delete permanently",
        ["DeleteCancel"] = "Cancel",
        ["FileOpErrorTitle"] = "Couldn't delete \"{0}\"",
        ["FileOpErrorClose"] = "Close",
        ["SidebarSearchPlaceholder"] = "Search files",
        ["SidebarSearchReset"] = "Esc to clear search",
        ["SidebarSearchEmpty"] = "No matches in this folder",
        ["SidebarSearchMatches"] = "MATCHES",
        ["SidebarSearchTruncated"] = "Showing the first matches only. Narrow your query.",
        ["TabsOverflow"] = "{0} more",
        ["TabsOverflowHeader"] = "OPEN TABS",
        ["TabsCloseOthers"] = "Close Others",
        ["TabClose"] = "Close tab",
        ["NewDocumentTooltip"] = "New document ({0})",
        ["EmptySurfaceTitle"] = "No document selected",
        ["EmptySurfaceHint"] = "Pick a file on the left — it opens in a tab.",
        ["AppMenuCloseFolderLabel"] = "Close Folder",
        ["WelcomeOpenFolder"] = "Open Folder…",
        ["SidebarTooltip"] = "Files in this folder",
        ["TreeNodeMissing"] = "Folder is gone",
        ["TreeNodeAccessDenied"] = "Access denied",
        ["TreeNodeReadError"] = "Couldn't read this folder",
        ["FolderErrorNotFoundTitle"] = "Couldn't find that folder",
        ["FolderErrorAccessDeniedTitle"] = "Access denied",
        ["FolderErrorReadTitle"] = "Couldn't read that folder",
        ["SaveDialogTitle"] = "Save Markdown file",
        ["MarkdownDocuments"] = "Markdown documents",
        ["UntitledFileName"] = "Untitled.md"
    };

    private static readonly Dictionary<string, string> Russian = new(StringComparer.Ordinal)
    {
        ["WelcomeTagline"] = "Тихое место для чтения Markdown.",
        ["WelcomeNewDocument"] = "Новый документ",
        ["WelcomeOpenFile"] = "Открыть файл…",
        ["RecentTitle"] = "НЕДАВНИЕ",
        ["RecentClear"] = "Очистить",
        ["RecentToday"] = "сегодня",
        ["RecentYesterday"] = "вчера",
        ["RecentDateFormat"] = "d MMM",
        ["RecentDateWithYearFormat"] = "d MMM yyyy",
        ["RecentFileNotFoundTitle"] = "Файл не найден",
        ["RecentFolderNotFoundTitle"] = "Папка не найдена",
        ["RecentRemoveBody"] = "Убрать «{0}» из «Недавних»?",
        ["RecentRemoveConfirm"] = "Убрать",
        ["RecentRemoveCancel"] = "Отмена",
        ["WelcomeDropHint"] = "или перетащите сюда .md файл или папку",
        ["TitleBarMinimize"] = "Свернуть",
        ["TitleBarMaximize"] = "Развернуть",
        ["TitleBarRestore"] = "Восстановить",
        ["TitleBarClose"] = "Закрыть",
        ["AppMenuTooltip"] = "Меню",
        ["EditToggleTooltip"] = "Переключить режим редактирования ({0})",
        ["EditDone"] = "Готово",
        ["EditDoneTooltip"] = "Закончить правку ({0})",
        ["EditUnsaved"] = "Не сохранено",
        ["ReadingSettingsTooltip"] = "Вид: тема, шрифт, размер",
        ["OverlayCloseSettings"] = "Закрыть настройки",
        ["AppSettingsHeader"] = "Настройки",
        ["AppMenuNewDocument"] = "Новый документ",
        ["AppMenuOpenFile"] = "Открыть файл…",
        ["AppMenuOpenFolder"] = "Открыть папку…",
        ["AppMenuSave"] = "Сохранить",
        ["AppMenuSaveAs"] = "Сохранить как…",
        ["AppMenuReload"] = "Перечитать с диска",
        ["AppMenuFilesPanel"] = "Панель файлов",
        ["AppMenuCloseTab"] = "Закрыть вкладку",
        ["AppMenuSettings"] = "Настройки…",
        ["AppMenuAbout"] = $"О {AppProductInfo.Name}",
        ["AboutWindowTitle"] = $"О {AppProductInfo.Name}",
        ["AboutVersion"] = "Версия {0}",
        ["AboutForkAttribution"] = "Форк MarkMello © 2026 MarkMello contributors",
        ["AppSettingsReadingHintPrefix"] = "Тема, шрифт и размер текста — в карточке",
        ["AppSettingsReadingHintSuffix"] = "над документом.",
        ["LanguageLabel"] = "Язык",
        ["LanguageHint"] = "Оболочка и диалоги",
        ["LanguageSystem"] = "Системный",
        ["LanguageEnglish"] = "Английский",
        ["LanguageRussian"] = "Русский",
        ["UpdatesLabel"] = "Обновления",
        ["ReadingThemeLabel"] = "Тема",
        ["ReadingThemeAuto"] = "Авто",
        ["ReadingThemeLight"] = "Светлая",
        ["ReadingThemeDark"] = "Тёмная",
        ["ReadingFontLabel"] = "Шрифт",
        ["ReadingFontSerif"] = "С засечками",
        ["ReadingFontSans"] = "Без засечек",
        ["ReadingFontMono"] = "Моно",
        ["ReadingSizeLabel"] = "Размер текста",
        ["ReadingFontSizeValue"] = "{0} px",
        ["ReadingSizeDecreaseTooltip"] = "Меньше ({0})",
        ["ReadingSizeIncreaseTooltip"] = "Больше ({0})",
        ["ReadingLineHeightLabel"] = "Интерлиньяж",
        ["ReadingWidthLabel"] = "Ширина строки",
        ["ReadingWidthNarrow"] = "Узкая",
        ["ReadingWidthMedium"] = "Средняя",
        ["ReadingWidthWide"] = "Широкая",
        ["WindowBorderLabel"] = "Рамка окна",
        ["WindowBorderHint"] = "Контур по краю окна",
        ["WindowBorderAuto"] = "Авто",
        ["WindowBorderOn"] = "Вкл",
        ["WindowBorderOff"] = "Выкл",
        ["ReadingMoreSettingsHint"] = "Язык, обновления, версия",
        ["ReadingMoreSettingsLink"] = "Настройки…",
        ["StatusWordsOne"] = "{0:N0} слово",
        ["StatusWordsFew"] = "{0:N0} слова",
        ["StatusWordsMany"] = "{0:N0} слов",
        ["StatusReadMinutes"] = "{0} мин",
        ["DragDropHint"] = "Отпустите, чтобы открыть",
        ["DropFileDetails"] = "{0} откроется в новой вкладке",
        ["DropFolderNewWindowDetails"] = "Папка {0} откроется в новом окне",
        ["DropFolderOpenElsewhereDetails"] = "Папка {0} уже открыта в другом окне",
        ["DropFolderThisWindowDetails"] = "Папка {0} откроется в этом окне",
        ["DirtyPromptCancel"] = "Отмена",
        ["DirtyPromptDiscard"] = "Не сохранять",
        ["DirtyPromptSave"] = "Сохранить",
        ["LoadErrorOpenAnotherFile"] = "Открыть другой файл…",
        ["LoadErrorTryAgain"] = "Повторить",
        ["LoadErrorDismiss"] = "закрыть",
        ["EditorBoldTooltip"] = "Жирный",
        ["EditorItalicTooltip"] = "Курсив",
        ["EditorCodeTooltip"] = "Код",
        ["EditorLinkTooltip"] = "Ссылка",
        ["EditorListTooltip"] = "Список",
        ["EditorQuoteTooltip"] = "Цитата",
        ["EditorProtectedImageDataMessage"] = "Встроенные данные изображения можно редактировать только целой строкой.",
        ["ContextCopy"] = "Копировать",
        ["ContextSelectAll"] = "Выделить всё",
        ["ContextCopyLink"] = "Копировать ссылку",
        ["ContextCopyLinks"] = "Копировать ссылки",
        ["ContextCopyTelegramMarkdown"] = "Копировать выделение как Markdown для Telegram",
        ["CodeCopyTooltip"] = "Скопировать код",
        ["CodeCopiedStatus"] = "Код скопирован",
        ["AlertNote"] = "Примечание",
        ["AlertTip"] = "Совет",
        ["AlertImportant"] = "Важно",
        ["AlertWarning"] = "Предупреждение",
        ["AlertCaution"] = "Внимание",
        ["ImageLoading"] = "Загрузка…",
        ["DiagramRenderFailed"] = "Не удалось отрисовать диаграмму {0}",
        ["DiagramSvgUnsupported"] = "Диаграмма {0} отрисована, но её SVG встроенный просмотр пока не поддерживает",
        ["DiagramEmpty"] = "Диаграмма получилась пустой. Проверьте синтаксис.",
        ["EditorSourceLabel"] = "Markdown",
        ["EditorPreviewLabel"] = "Предпросмотр",
        ["FindPlaceholder"] = "Поиск в документе",
        ["FindPreviousTooltip"] = "Предыдущее совпадение ({0})",
        ["FindToggleTooltip"] = "Найти в документе ({0})",
        ["FindNextTooltip"] = "Следующее совпадение ({0})",
        ["FindCloseTooltip"] = "Закрыть поиск ({0})",
        ["FindResultCount"] = "{0} из {1}",
        ["FindNoResults"] = "Ничего не найдено",
        ["UpdateCheckNow"] = "Проверить",
        ["UpdateChecking"] = "Проверка...",
        ["UpdateDownload"] = "Скачать",
        ["UpdateDownloading"] = "Загрузка...",
        ["UpdateOpenDownloaded"] = "Открыть обновление",
        ["UpdateLaunchInstaller"] = "Запустить установщик",
        ["UpdateOpenDmg"] = "Открыть DMG",
        ["UpdateRevealAppImage"] = "Показать AppImage",
        ["UpdateDefaultTitle"] = "Проверка вручную",
        ["UpdateDefaultMessage"] = $"При запуске {AppProductInfo.Name} не ходит в сеть.",
        ["UpdateCheckingTitle"] = "Проверка GitHub Releases",
        ["UpdateCheckingMessage"] = "Ищем более новую сборку для этого устройства.",
        ["UpdateUnavailableTitle"] = "Обновления недоступны",
        ["UpdateUnavailableMessage"] = "Для этой сборки пока не настроен источник GitHub Releases.",
        ["UpdateUnsupportedPlatformTitle"] = "Для этой среды нет пакетного обновления",
        ["UpdateUnsupportedPlatformMessage"] = "{0} {1} отсутствует в текущей матрице релизов.",
        ["UpdateUpToDateTitle"] = "У вас актуальная версия",
        ["UpdateUpToDateMessage"] = "Текущая сборка {0} уже совпадает с последним опубликованным релизом ({1}).",
        ["UpdateAvailableTitle"] = "Доступно обновление {0}",
        ["UpdateAvailableMessage"] = "{0} готов для {1} {2}.",
        ["UpdateCheckFailedTitle"] = "Не удалось проверить обновления",
        ["UpdateDownloadTitle"] = "Загрузка {0}",
        ["UpdateDownloadMessage"] = "Сохраняем {0} из GitHub Releases.",
        ["UpdateReadyTitle"] = "Обновление готово",
        ["UpdateReadyLaunchInstaller"] = "{0} загружен. Запустите установщик, чтобы продолжить нативное обновление Windows.",
        ["UpdateReadyOpenDmg"] = "{0} загружен. Откройте DMG, чтобы продолжить нативную установку на macOS.",
        ["UpdateReadyRevealAppImage"] = "{0} загружен. Покажите AppImage и замените предыдущий бинарник, когда будете готовы.",
        ["UpdateReadyGeneric"] = "{0} загружен.",
        ["UpdateDownloadFailedTitle"] = "Ошибка загрузки",
        ["UpdateNativeFlowStartedTitle"] = "Запущен нативный сценарий обновления",
        ["UpdateNativeFlowStartedLaunchInstaller"] = "Установщик запущен. Продолжайте обновление через нативный сценарий.",
        ["UpdateNativeFlowStartedOpenDmg"] = "DMG открыт. Продолжайте установку через нативный сценарий macOS.",
        ["UpdateNativeFlowStartedRevealAppImage"] = "AppImage показан в файловом менеджере.",
        ["UpdateOpenDownloadedFailedTitle"] = "Не удалось открыть загруженное обновление",
        ["ErrorFileNotFoundTitle"] = "Не удалось найти файл",
        ["ErrorAccessDeniedTitle"] = "Доступ запрещён",
        ["ErrorReadFailureTitle"] = "Не удалось прочитать файл",
        ["ErrorUnsupportedTypeTitle"] = "Это не Markdown",
        ["ErrorFileNotFoundDetails"] = "Возможно, его переместили, переименовали или удалили.",
        ["ErrorAccessDeniedDetails"] = $"У {AppProductInfo.Name} нет прав на чтение этого файла.",
        ["ErrorUnsupportedTypeDetails"] = $"{AppProductInfo.Name} открывает файлы {{0}} и {{1}}.",
        ["ErrorUnsupportedTypeSingleDetails"] = $"{AppProductInfo.Name} открывает файлы {{0}}.",
        ["DirtyPromptTitle"] = "Сохранить изменения в «{0}»?",
        ["DirtyPromptCloseFile"] = "Иначе правки пропадут, когда вкладка закроется.",
        ["DirtyPromptCloseFolder"] = "Иначе правки пропадут, когда папка закроется.",
        ["DirtyPromptReload"] = "Иначе правки пропадут при перезагрузке.",
        ["DirtyPromptLeaveEditMode"] = "Иначе правки пропадут при выходе из правки.",
        ["DirtyPromptCloseWindow"] = $"Иначе правки пропадут при выходе из {AppProductInfo.Name}.",
        ["SaveInvalidPath"] = "Не удалось сохранить по этому пути: {0}",
        ["SaveAccessDenied"] = "Доступ запрещён: {0}",
        ["SaveWriteFailure"] = "Не удалось сохранить документ: {0}",
        ["SaveGenericFailure"] = "Не удалось сохранить документ.",
        ["OpenDialogTitle"] = "Открыть Markdown-файл",
        ["OpenFolderDialogTitle"] = "Открыть папку",
        ["ExternalChangeTitle"] = "{0} изменён на диске.",
        ["ExternalChangeMessage"] = "Другая программа сохранила свою версию, а ваши правки ещё не сохранены.",
        ["ExternalChangeReloadTooltip"] = "Ваши правки пропадут",
        ["ExternalChangeReload"] = "Загрузить с диска",
        ["ExternalChangeKeep"] = "Оставить мои правки",
        ["TabDeletedSuffix"] = "(удалён)",
        ["SidebarHideTooltip"] = "Скрыть панель файлов ({0})",
        ["SidebarShowTooltip"] = "Показать панель файлов ({0})",
        ["SidebarNewFile"] = "Новый файл",
        ["SidebarNewFolder"] = "Новая папка",
        ["SidebarCreateTooltip"] = "Новый файл или папка",
        ["SidebarOpenAnotherFolder"] = "Открыть другую папку…",
        ["TreeRename"] = "Переименовать",
        ["TreeDuplicate"] = "Дублировать",
        ["TreeDelete"] = "Удалить",
        ["TreeOpenInNewTab"] = "Открыть в новой вкладке",
        ["TreeRevealInExplorerWindows"] = "Показать в проводнике",
        ["TreeRevealInExplorerMacOS"] = "Показать в Finder",
        ["TreeRevealInExplorerLinux"] = "Показать в файловом менеджере",
        ["TreeNameTaken"] = "Файл с таким именем уже есть",
        ["TreeFolderNameTaken"] = "Папка с таким именем уже есть",
        ["TreeInvalidChars"] = """Нельзя использовать: \ / : * ? " < > |""",
        ["TreeReservedName"] = "Это имя занято системой",
        ["TreeOperationFailed"] = "Операция не удалась",
        ["DeleteFileTitle"] = "Удалить «{0}»?",
        ["DeleteFileBody"] = "Файл будет перемещён в корзину. Если он открыт во вкладке, вкладка закроется.",
        ["DeleteFolderTitle"] = "Удалить папку «{0}»?",
        ["DeleteFolderBody"] = "Папка будет перемещена в корзину.",
        ["DeleteFolderNonEmptyTitle"] = "Удалить папку «{0}» и всё её содержимое?",
        ["DeleteFolderNonEmptyBody"] = "В папке {1} элементов. Всё будет перемещено в корзину. Открытые вкладки из этой папки закроются.",
        ["DeletePermanentBody"] = "Корзина здесь недоступна. Элемент будет удалён безвозвратно, восстановить его будет нельзя.",
        ["DeleteUnsavedChangesWarning"] = "Несохранённые правки в «{0}» пропадут.",
        ["DeleteConfirm"] = "Удалить",
        ["DeletePermanentConfirm"] = "Удалить навсегда",
        ["DeleteCancel"] = "Отмена",
        ["FileOpErrorTitle"] = "Не удалось удалить «{0}»",
        ["FileOpErrorClose"] = "Закрыть",
        ["SidebarSearchPlaceholder"] = "Поиск по файлам",
        ["SidebarSearchReset"] = "Esc — сбросить поиск",
        ["SidebarSearchEmpty"] = "Ничего не найдено в этой папке",
        ["SidebarSearchMatches"] = "СОВПАДЕНИЯ",
        ["SidebarSearchTruncated"] = "Показаны только первые совпадения. Уточните запрос.",
        ["TabsOverflow"] = "ещё {0}",
        ["TabsOverflowHeader"] = "ОТКРЫТЫЕ ВКЛАДКИ",
        ["TabsCloseOthers"] = "Закрыть все, кроме активной",
        ["TabClose"] = "Закрыть вкладку",
        ["NewDocumentTooltip"] = "Новый документ ({0})",
        ["EmptySurfaceTitle"] = "Документ не выбран",
        ["EmptySurfaceHint"] = "Выберите файл в списке слева — он откроется во вкладке.",
        ["AppMenuCloseFolderLabel"] = "Закрыть папку",
        ["WelcomeOpenFolder"] = "Открыть папку…",
        ["SidebarTooltip"] = "Файлы этой папки",
        ["TreeNodeMissing"] = "Папка исчезла",
        ["TreeNodeAccessDenied"] = "Доступ запрещён",
        ["TreeNodeReadError"] = "Не удалось прочитать папку",
        ["FolderErrorNotFoundTitle"] = "Не удалось найти папку",
        ["FolderErrorAccessDeniedTitle"] = "Доступ запрещён",
        ["FolderErrorReadTitle"] = "Не удалось прочитать папку",
        ["SaveDialogTitle"] = "Сохранить Markdown-файл",
        ["MarkdownDocuments"] = "Markdown-документы",
        ["UntitledFileName"] = "Безымянный.md"
    };

    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");

    private AppLanguage _selectedLanguage;
    private AppLanguage _effectiveLanguage;
    private CultureInfo _culture = EnglishCulture;

    public LocalizationService()
        : this(AppLanguage.System)
    {
    }

    public LocalizationService(AppLanguage initialLanguage)
    {
        SetLanguage(initialLanguage);
    }

    public AppLanguage SelectedLanguage => _selectedLanguage;

    public AppLanguage EffectiveLanguage => _effectiveLanguage;

    public CultureInfo Culture => _culture;

    public string this[string key] => ResolveString(key);

    public string Format(string key, params object?[] args)
        => string.Format(_culture, ResolveString(key), args);

    public string FormatPlural(string key, int count)
        => string.Format(_culture, ResolveString(key + PluralForm(count)), count);

    public void SetLanguage(AppLanguage language)
    {
        var normalized = NormalizeLanguage(language);
        var effective = ResolveEffectiveLanguage(normalized);
        var culture = ResolveCulture(effective);

        var selectedChanged = _selectedLanguage != normalized;
        var effectiveChanged = _effectiveLanguage != effective;
        var cultureChanged = !_culture.Equals(culture);
        if (!selectedChanged && !effectiveChanged && !cultureChanged)
        {
            return;
        }

        _selectedLanguage = normalized;
        _effectiveLanguage = effective;
        _culture = culture;

        OnPropertyChanged(nameof(SelectedLanguage));
        OnPropertyChanged(nameof(EffectiveLanguage));
        OnPropertyChanged(nameof(Culture));
        NotifyLocalizedTextChanged();
    }

    private void NotifyLocalizedTextChanged()
    {
        // Avalonia indexer bindings may subscribe to either the CLR indexer
        // property name (Item) or the common WPF-style indexer marker (Item[]).
        // Raising both keeps every active shell/view binding refreshed when the
        // language changes. The empty name is the standard full-refresh signal.
        OnPropertyChanged("Item");
        OnPropertyChanged("Item[]");
        OnPropertyChanged(string.Empty);
    }

    private string ResolveString(string key)
    {
        var primary = _effectiveLanguage == AppLanguage.Russian ? Russian : English;
        if (primary.TryGetValue(key, out var value))
        {
            return value;
        }

        if (English.TryGetValue(key, out value))
        {
            return value;
        }

        return $"[[{key}]]";
    }

    /// <summary>
    /// Форма числительного для ключа <c>&lt;key&gt;One|Few|Many</c>. В английском форм
    /// две — единственное и остальное; в русском три, по последней цифре числа,
    /// кроме одиннадцати-четырнадцати.
    /// </summary>
    private string PluralForm(int count)
    {
        if (_effectiveLanguage != AppLanguage.Russian)
        {
            return count == 1 ? "One" : "Many";
        }

        var withinHundred = Math.Abs(count) % 100;
        var lastDigit = withinHundred % 10;

        if (lastDigit == 1 && withinHundred != 11)
        {
            return "One";
        }

        return lastDigit is >= 2 and <= 4 && withinHundred is < 12 or > 14 ? "Few" : "Many";
    }

    private static AppLanguage NormalizeLanguage(AppLanguage language)
        => language switch
        {
            AppLanguage.English => AppLanguage.English,
            AppLanguage.Russian => AppLanguage.Russian,
            _ => AppLanguage.System
        };

    private static AppLanguage ResolveEffectiveLanguage(AppLanguage selectedLanguage)
    {
        if (selectedLanguage is AppLanguage.English or AppLanguage.Russian)
        {
            return selectedLanguage;
        }

        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ru", StringComparison.OrdinalIgnoreCase)
            ? AppLanguage.Russian
            : AppLanguage.English;
    }

    private static CultureInfo ResolveCulture(AppLanguage language)
        => language == AppLanguage.Russian ? RussianCulture : EnglishCulture;
}
