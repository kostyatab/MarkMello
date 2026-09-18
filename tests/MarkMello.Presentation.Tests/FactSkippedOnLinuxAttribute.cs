namespace MarkMello.Presentation.Tests;

/// <summary>
/// <see cref="FactAttribute"/>, который на Linux пропускается с указанной причиной, —
/// для сценариев, которых на этой ОС не существует. Пропуск виден в отчёте как Skipped,
/// а ранний <c>return</c> в теле выдал бы непроверенный тест за зелёный.
/// </summary>
internal sealed class FactSkippedOnLinuxAttribute : FactAttribute
{
    public FactSkippedOnLinuxAttribute(string reason)
    {
        Reason = reason;

        if (OperatingSystem.IsLinux())
        {
            Skip = reason;
        }
    }

    public string Reason { get; }
}
