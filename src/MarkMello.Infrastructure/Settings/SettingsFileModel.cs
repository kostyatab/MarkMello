using System.Text.Json;
using MarkMello.Domain;
using MarkMello.Domain.Workspace;

namespace MarkMello.Infrastructure.Settings;

internal sealed record SettingsFileModel(
    ThemeMode Theme,
    ReadingPreferences Preferences,
    AppLanguage Language,
    WindowPlacement? WindowPlacement,
    WindowBorderMode WindowBorder = WindowBorderMode.Auto,
    bool DocumentOutline = true,
    double? SidebarWidth = null,
    WorkspaceSessionState? Session = null,
    // Сырой JSON, а не список записей: битая запись «Недавних» не должна ронять чтение
    // всего файла вместе с темой и параметрами чтения. Разбирает JsonSettingsStore.
    JsonElement? Recent = null);
