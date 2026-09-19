using MarkMello.Domain;

namespace MarkMello.Application.Abstractions;

/// <summary>
/// Применение пользовательской темы к UI framework. Реализация в Presentation
/// (зависит от Avalonia.Application). VM не должна знать про Avalonia напрямую.
/// </summary>
public interface IThemeService
{
    /// <summary>
    /// Применить тему к Application.RequestedThemeVariant. <see cref="ThemeMode.System"/>
    /// следует за темой ОС и после смены её на ходу — без перезапуска.
    /// </summary>
    void Apply(ThemeMode mode);
}
