namespace MarkMello.Application.Updates;

public abstract record UpdatePrepareResult
{
    private UpdatePrepareResult()
    {
    }

    public sealed record Success(string Message) : UpdatePrepareResult;

    public sealed record Failed(string Message) : UpdatePrepareResult;

    /// <summary>Скачанного файла больше нет — его удалили или переместили; скачать можно заново.</summary>
    public sealed record FileNotFound(string Path) : UpdatePrepareResult;
}
