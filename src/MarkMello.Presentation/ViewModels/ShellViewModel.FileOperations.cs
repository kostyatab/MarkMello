using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkMello.Application.UseCases;

namespace MarkMello.Presentation.ViewModels;

/// <summary>
/// Подтверждение удаления и реакция вкладок на файловые операции.
/// Диалог живёт на уровне окна: он модальный и перекрывает и дерево, и документ.
/// </summary>
public partial class ShellViewModel
{
    private FileTreeNodeViewModel? _deleteTarget;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeletePromptContent))]
    private bool _isDeletePromptOpen;

    /// <summary>Карточка подтверждения строится в момент вопроса, а не живёт скрытой.</summary>
    public object? DeletePromptContent => IsDeletePromptOpen ? this : null;

    [ObservableProperty]
    private string _deletePromptTitle = string.Empty;

    [ObservableProperty]
    private string _deletePromptMessage = string.Empty;

    /// <summary>
    /// Корзина недоступна: тот же диалог переспрашивает уже про безвозвратное удаление.
    /// Пользователь должен подтвердить именно потерю, а не «удаление» вообще, поэтому
    /// и кнопка говорит «Удалить навсегда».
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeleteConfirmLabel))]
    private bool _isPermanentDeletePrompt;

    /// <summary>Ошибка операции показывается той же карточкой с одной кнопкой «Закрыть».</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeleteCancelLabel))]
    private bool _isDeleteErrorPrompt;

    public string DeleteConfirmLabel => _localization[IsPermanentDeletePrompt ? "DeletePermanentConfirm" : "DeleteConfirm"];

    public string DeleteCancelLabel => _localization[IsDeleteErrorPrompt ? "FileOpErrorClose" : "DeleteCancel"];

    /// <summary>
    /// Порядок кнопок — как у платформы (ADR-0009 Rule 10), в тех же колонках ряда, что и
    /// у диалога правок: на macOS «Отмена», «Удалить»; на Windows и Linux — «Удалить», «Отмена».
    /// </summary>
    public int DeleteCancelColumn => UsesMacOSDialogOrder ? 3 : 4;

    public int DeleteConfirmColumn => UsesMacOSDialogOrder ? 4 : 3;

    /// <summary>
    /// Готовит тексты подтверждения: у файла, пустой и непустой папки они разные.
    /// Под диалогом о правках удаление не начинается: оно закрыло бы спрошенную вкладку.
    /// </summary>
    private async Task RequestDeleteAsync(FileTreeNodeViewModel node)
    {
        if (IsModalDialogOpen)
        {
            return;
        }

        _deleteTarget = node;
        IsPermanentDeletePrompt = false;
        IsDeleteErrorPrompt = false;

        if (!node.IsDirectory)
        {
            DeletePromptTitle = Format("DeleteFileTitle", node.Name);
            DeletePromptMessage = _localization["DeleteFileBody"];
        }
        else
        {
            // Считаем только верхний уровень: рекурсивный обход ради текста запрещён (Rule 5).
            var count = await _fileOperations.CountChildrenAsync(node.Path).ConfigureAwait(true);

            if (count == 0)
            {
                DeletePromptTitle = Format("DeleteFolderTitle", node.Name);
                DeletePromptMessage = _localization["DeleteFolderBody"];
            }
            else
            {
                DeletePromptTitle = Format("DeleteFolderNonEmptyTitle", node.Name);
                DeletePromptMessage = Format("DeleteFolderNonEmptyBody", node.Name, count);
            }
        }

        DeletePromptMessage = WithUnsavedChangesWarning(DeletePromptMessage, node.Path);

        // Контекстное меню уже закрылось — строка держит подсветку, чтобы было видно, что удаляем.
        node.IsPendingDelete = true;
        IsDeletePromptOpen = true;
    }

    /// <summary>
    /// Вкладки под удаляемым путём закроются без диалога «Сохранить»: сохранять файл, который
    /// сейчас исчезнет, бессмысленно. Поэтому потерю правок называем прямо в подтверждении —
    /// по строке на каждый файл.
    /// </summary>
    private string WithUnsavedChangesWarning(string message, string path)
    {
        var warnings = OpenDocuments.Tabs
            .Where(tab => tab.EditorSession?.IsDirty == true
                && tab.Path is { } tabPath
                && IsSameOrUnder(tabPath, path))
            .Select(tab => Format("DeleteUnsavedChangesWarning", tab.Title));

        return string.Join('\n', warnings.Prepend(message));
    }

    [RelayCommand]
    private async Task ConfirmDeleteAsync()
    {
        if (_deleteTarget is not { } node || Workspace is not { } workspace)
        {
            CancelDelete();
            return;
        }

        if (IsDeleteErrorPrompt)
        {
            CancelDelete();
            return;
        }

        var result = IsPermanentDeletePrompt
            ? await workspace.DeletePermanentlyConfirmedAsync(node).ConfigureAwait(true)
            : await workspace.DeleteConfirmedAsync(node).ConfigureAwait(true);

        switch (result)
        {
            case WorkspaceMutationResult.Deleted:
                await CloseTabsUnderPathAsync(node.Path).ConfigureAwait(true);
                CancelDelete();
                break;

            case WorkspaceMutationResult.TrashUnavailable:
                // Ничего не удалено: переспрашиваем уже про безвозвратное удаление.
                IsPermanentDeletePrompt = true;
                DeletePromptMessage = WithUnsavedChangesWarning(_localization["DeletePermanentBody"], node.Path);
                break;

            default:
                IsDeleteErrorPrompt = true;
                DeletePromptTitle = Format("FileOpErrorTitle", node.Name);
                DeletePromptMessage = workspace.OperationError ?? _localization["TreeOperationFailed"];
                break;
        }
    }

    [RelayCommand]
    private void CancelDelete()
    {
        if (_deleteTarget is { } node)
        {
            node.IsPendingDelete = false;
        }

        IsDeletePromptOpen = false;
        IsPermanentDeletePrompt = false;
        IsDeleteErrorPrompt = false;
        _deleteTarget = null;

        // Файлы, которые ОС прислала, пока шёл вопрос, открываются после ответа.
        _ = OpenDeferredActivationsAsync();
    }

    /// <summary>Переименование: вкладки этого файла и файлов внутри папки следуют за новым путём.</summary>
    private void RetargetTabsUnderPath(string oldPath, string newPath)
    {
        foreach (var tab in OpenDocuments.Tabs.ToList())
        {
            if (tab.Path is not { } path)
            {
                continue;
            }

            if (PathsMatch(path, oldPath))
            {
                tab.Retarget(newPath, Path.GetFileName(newPath));
                tab.Tooltip = BuildTabTooltip(newPath);
            }
            else if (IsUnderDirectory(path, oldPath))
            {
                var moved = newPath + path[oldPath.Length..];
                tab.Retarget(moved, Path.GetFileName(moved));
                tab.Tooltip = BuildTabTooltip(moved);
            }
        }

        if (PathsMatch(CurrentDocumentPath, oldPath))
        {
            _currentPath = newPath;
            RenameOpenDocument(newPath);
        }

        OpenDocuments.Refresh();

        // Полоса «изменён на диске» называет файл по имени — оно могло смениться.
        OnPropertyChanged(nameof(ExternalChangeTitle));
    }

    /// <summary>
    /// Имя открытого документа живёт в загруженной модели и в сессии редактора,
    /// поэтому после переименования его надо обновить в обеих — иначе заголовок окна
    /// продолжает показывать старое имя.
    /// </summary>
    private void RenameOpenDocument(string newPath)
    {
        var fileName = Path.GetFileName(newPath);

        if (Document is { } document)
        {
            Document = document with { Path = newPath, FileName = fileName };
        }

        EditorSession?.Rename(newPath, fileName);
        RefreshDocumentSummary();
    }

    /// <summary>Удаление: открытые вкладки удалённого файла или папки закрываются.</summary>
    private async Task CloseTabsUnderPathAsync(string path)
    {
        foreach (var tab in OpenDocuments.Tabs.ToList())
        {
            if (tab.Path is { } tabPath && IsSameOrUnder(tabPath, path))
            {
                await RemoveTabAsync(tab).ConfigureAwait(true);
            }
        }
    }

    private string Format(string key, params object?[] arguments)
        => string.Format(_localization.Culture, _localization[key], arguments);

    private static bool PathsMatch(string? left, string? right)
        => left is not null
            && right is not null
            && string.Equals(left, right, PathComparison);

    private static bool IsSameOrUnder(string path, string target)
        => PathsMatch(path, target) || IsUnderDirectory(path, target);

    private static bool IsUnderDirectory(string path, string directory)
        => path.StartsWith(
            Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar,
            PathComparison);

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}
