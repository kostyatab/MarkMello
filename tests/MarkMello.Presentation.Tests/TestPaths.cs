namespace MarkMello.Presentation.Tests;

/// <summary>
/// Абсолютные пути для фикстур, которые не трогают диск. Литерал вроде <c>C:\docs</c>
/// годится только для Windows: на macOS и Linux обратный слеш — обычный символ имени,
/// и Path-API видят в <c>C:\docs</c> одно имя файла, а не папку «docs».
/// </summary>
internal static class TestPaths
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "MarkMello.Tests");

    public static string At(params string[] segments) => Path.Combine([Root, .. segments]);
}
