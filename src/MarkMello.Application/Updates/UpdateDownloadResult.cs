namespace MarkMello.Application.Updates;

public abstract record UpdateDownloadResult
{
    private UpdateDownloadResult()
    {
    }

    public sealed record Success(AppUpdatePackage Package, string DownloadedFilePath) : UpdateDownloadResult;

    /// <param name="IsConnectionProblem">Связь оборвалась или не установилась; иначе — ответ сервера или запись файла.</param>
    public sealed record Failed(string Message, bool IsConnectionProblem = false) : UpdateDownloadResult;
}
