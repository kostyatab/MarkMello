using System.Reflection;

namespace MarkMello.Presentation.ViewModels;

/// <summary>
/// Сведения о программе в одном месте: нижняя строка карточки «Настройки» и окно
/// «О MarkMello» (ADR-0009 Rule 7) показывают одно и то же — версию, лицензию,
/// автора и ссылки проекта.
/// </summary>
internal static class AppProductInfo
{
    public const string Name = "MarkMello";

    public const string License = "GPLv3";

    public const string Author = "Andrey Ermolaev";

    public const string AuthorUrl = "https://ermolaev.tech";

    public const string WebsiteUrl = "https://markmello.ru";

    public const string TelegramUrl = "https://t.me/mark_mello";

    public const string GitHubUrl = "https://github.com/dartdavros/MarkMello";

    /// <summary>
    /// Версия сборки без метаданных: <c>1.0.0-preview.12+abc1234</c> показывается как
    /// <c>1.0.0-preview.12</c>. Читается один раз на владельца — рефлексия по атрибуту
    /// сборки, не по строковым именам, поэтому AOT её сохраняет.
    /// </summary>
    public static string GetVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(AppProductInfo).Assembly;

        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            var buildMetadataIndex = informationalVersion.IndexOf('+');
            return buildMetadataIndex >= 0
                ? informationalVersion[..buildMetadataIndex]
                : informationalVersion;
        }

        var version = assembly.GetName().Version;
        return version is null
            ? "1.0.0"
            : $"{version.Major}.{Math.Max(version.Minor, 0)}.{Math.Max(version.Build, 0)}";
    }
}
