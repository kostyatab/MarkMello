using MarkMello.Application.Updates;

namespace MarkMello.Application.Abstractions;

public interface IUpdateService
{
    Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Скачивает пакет обновления. Ход загрузки уходит в <paramref name="progress"/>.
    /// Отмена бросает <see cref="OperationCanceledException"/>; и при отмене, и при ошибке
    /// недокачанный файл удаляется.
    /// </summary>
    Task<UpdateDownloadResult> DownloadUpdateAsync(
        AppUpdatePackage package,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<UpdatePrepareResult> PrepareDownloadedUpdateAsync(
        AppUpdatePackage package,
        string downloadedFilePath,
        CancellationToken cancellationToken = default);
}
