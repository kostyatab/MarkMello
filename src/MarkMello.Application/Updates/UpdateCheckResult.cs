namespace MarkMello.Application.Updates;

public abstract record UpdateCheckResult
{
    private UpdateCheckResult()
    {
    }

    public sealed record SourceNotConfigured(string Message) : UpdateCheckResult;

    public sealed record UnsupportedPlatform(string PlatformName, string ArchitectureName) : UpdateCheckResult;

    public sealed record UpToDate(
        string CurrentVersion,
        string LatestVersion,
        DateTimeOffset PublishedAt,
        string ReleasePageUrl) : UpdateCheckResult;

    public sealed record UpdateAvailable(AppUpdatePackage Package) : UpdateCheckResult;

    /// <param name="IsConnectionProblem">
    /// Не дошли до GitHub: нет сети, таймаут. Иначе GitHub ответил, но не тем — лимит
    /// запросов, нет файла для платформы, битый ответ, — и совет «проверьте интернет» неверен.
    /// </param>
    public sealed record Failed(string Message, bool IsConnectionProblem = false) : UpdateCheckResult;
}
