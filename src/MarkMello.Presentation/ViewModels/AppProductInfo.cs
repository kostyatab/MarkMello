using System.Reflection;

namespace MarkMello.Presentation.ViewModels;

/// <summary>
/// Сведения о программе в одном месте: нижняя строка карточки «Настройки» и окно
/// «О Softmark» (ADR-0009 Rule 7) показывают имя, версию, лицензию и ссылку на
/// репозиторий; окно About добавляет строку атрибуции оригинала (ADR-0011).
/// <see cref="Name"/> — единственный источник имени
/// приложения: строки интерфейса подставляют его, а не пишут литералом (ADR-0011).
/// </summary>
internal static class AppProductInfo
{
    public const string Name = "Softmark";

    public const string License = "GPLv3";

    public const string GitHubUrl = "https://github.com/kostyatab/Softmark";

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
            return NormalizeVersion(informationalVersion);
        }

        var version = assembly.GetName().Version;
        return version is null
            ? "1.0.0"
            : $"{version.Major}.{Math.Max(version.Minor, 0)}.{Math.Max(version.Build, 0)}";
    }

    /// <summary>
    /// Релиз собирается с <c>InformationalVersion</c>, равным тегу (<c>v1.0.0</c>), поэтому
    /// ведущая «v» срезается здесь, а не в воркфлоу: так версия не зависит от того, как
    /// назван тег. Метаданные сборки после «+» отбрасываются.
    /// </summary>
    internal static string NormalizeVersion(string informationalVersion)
    {
        var version = informationalVersion.Trim();

        var buildMetadataIndex = version.IndexOf('+');
        if (buildMetadataIndex >= 0)
        {
            version = version[..buildMetadataIndex];
        }

        return version.Length > 1 && version[0] is 'v' or 'V' && char.IsDigit(version[1])
            ? version[1..]
            : version;
    }
}
