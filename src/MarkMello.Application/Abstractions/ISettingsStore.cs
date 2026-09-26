using MarkMello.Domain;
using MarkMello.Domain.Recent;
using MarkMello.Domain.Workspace;

namespace MarkMello.Application.Abstractions;

/// <summary>
/// Хранилище пользовательских настроек. В M4 реализуется как маленький JSON-файл
/// в платформенном config-каталоге с безопасным fallback на defaults.
/// </summary>
public interface ISettingsStore
{
    ValueTask<ReadingPreferences> LoadPreferencesAsync(CancellationToken cancellationToken = default);
    ValueTask SavePreferencesAsync(ReadingPreferences preferences, CancellationToken cancellationToken = default);

    ValueTask<ThemeMode> LoadThemeAsync(CancellationToken cancellationToken = default);
    ValueTask SaveThemeAsync(ThemeMode theme, CancellationToken cancellationToken = default);

    ValueTask<WindowBorderMode> LoadWindowBorderModeAsync(CancellationToken cancellationToken = default);
    ValueTask SaveWindowBorderModeAsync(WindowBorderMode mode, CancellationToken cancellationToken = default);

    /// <summary>Рельс оглавления у правого края документа; файл без поля читается как «Вкл».</summary>
    ValueTask<bool> LoadDocumentOutlineEnabledAsync(CancellationToken cancellationToken = default);
    ValueTask SaveDocumentOutlineEnabledAsync(bool enabled, CancellationToken cancellationToken = default);

    ValueTask<AppLanguage> LoadLanguageAsync(CancellationToken cancellationToken = default);
    ValueTask SaveLanguageAsync(AppLanguage language, CancellationToken cancellationToken = default);

    ValueTask<double> LoadSidebarWidthAsync(CancellationToken cancellationToken = default);
    ValueTask SaveSidebarWidthAsync(double width, CancellationToken cancellationToken = default);

    /// <summary>
    /// Состояние последней сессии окна. Читается только по явному запросу пользователя:
    /// в стартовый путь восстановление не входит.
    /// </summary>
    ValueTask<WorkspaceSessionState> LoadSessionAsync(CancellationToken cancellationToken = default);
    ValueTask SaveSessionAsync(WorkspaceSessionState session, CancellationToken cancellationToken = default);

    ValueTask<WindowPlacement?> LoadWindowPlacementAsync(CancellationToken cancellationToken = default);
    ValueTask SaveWindowPlacementAsync(WindowPlacement? placement, CancellationToken cancellationToken = default);

    /// <summary>
    /// «Недавние» стартового экрана, общие для всех окон (ADR-0009 Rule 8). Битое или
    /// отсутствующее поле читается как пустой список и не сбрасывает остальные настройки.
    /// </summary>
    ValueTask<IReadOnlyList<RecentEntry>> LoadRecentAsync(CancellationToken cancellationToken = default);
    ValueTask SaveRecentAsync(IReadOnlyList<RecentEntry> entries, CancellationToken cancellationToken = default);
}
