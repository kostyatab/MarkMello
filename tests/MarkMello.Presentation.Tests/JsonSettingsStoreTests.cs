using MarkMello.Domain;
using MarkMello.Domain.Recent;
using MarkMello.Infrastructure.Settings;

namespace MarkMello.Presentation.Tests;

public sealed class JsonSettingsStoreTests
{
    [Fact]
    public async Task SaveAndLoadRoundTripsSettings()
    {
        var rootDirectory = CreateTempDirectory();
        try
        {
            var store = new JsonSettingsStore(rootDirectory);
            var expectedPreferences = new ReadingPreferences(
                FontFamilyMode.Mono,
                19,
                1.8,
                ReadingPreferences.WideContentWidth,
                DocumentMinimapMode.On);

            await store.SavePreferencesAsync(expectedPreferences);
            await store.SaveThemeAsync(ThemeMode.Dark);
            await store.SaveLanguageAsync(AppLanguage.Russian);
            await store.SaveWindowPlacementAsync(new WindowPlacement(120, 80, 900, 700, IsMaximized: true));

            var reloadedStore = new JsonSettingsStore(rootDirectory);
            var actualPreferences = await reloadedStore.LoadPreferencesAsync();
            var actualTheme = await reloadedStore.LoadThemeAsync();
            var actualLanguage = await reloadedStore.LoadLanguageAsync();
            var actualWindowPlacement = await reloadedStore.LoadWindowPlacementAsync();

            Assert.Equal(expectedPreferences, actualPreferences);
            Assert.Equal(ThemeMode.Dark, actualTheme);
            Assert.Equal(AppLanguage.Russian, actualLanguage);
            Assert.Equal(new WindowPlacement(120, 80, 900, 700, IsMaximized: true), actualWindowPlacement);
        }
        finally
        {
            DeleteDirectory(rootDirectory);
        }
    }

    [Fact]
    public async Task LoadFallsBackToDefaultsWhenSettingsFileIsCorrupted()
    {
        var rootDirectory = CreateTempDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(rootDirectory, "settings.json"), "{ invalid json");

            var store = new JsonSettingsStore(rootDirectory);

            var preferences = await store.LoadPreferencesAsync();
            var theme = await store.LoadThemeAsync();
            var language = await store.LoadLanguageAsync();
            var windowPlacement = await store.LoadWindowPlacementAsync();

            Assert.Equal(ReadingPreferences.Default, preferences);
            Assert.Equal(ThemeMode.System, theme);
            Assert.Equal(DocumentMinimapMode.Off, preferences.DocumentMinimapMode);
            Assert.Equal(AppLanguage.System, language);
            Assert.Null(windowPlacement);
        }
        finally
        {
            DeleteDirectory(rootDirectory);
        }
    }

    [Fact]
    public async Task LoadNormalizesOutOfRangePreferenceValues()
    {
        var rootDirectory = CreateTempDirectory();
        const string json = """
        {
          "theme": "Light",
          "preferences": {
            "fontFamily": "Mono",
            "fontSize": 4,
            "lineHeight": 9.0,
            "contentWidth": 1700,
            "documentMinimapMode": "Off"
          }
        }
        """;

        try
        {
            await File.WriteAllTextAsync(Path.Combine(rootDirectory, "settings.json"), json);

            var store = new JsonSettingsStore(rootDirectory);
            var preferences = await store.LoadPreferencesAsync();
            var theme = await store.LoadThemeAsync();
            var language = await store.LoadLanguageAsync();
            var windowPlacement = await store.LoadWindowPlacementAsync();

            Assert.Equal(ThemeMode.Light, theme);
            Assert.Equal(FontFamilyMode.Mono, preferences.FontFamily);
            Assert.Equal(ReadingPreferences.MinFontSize, preferences.FontSize);
            Assert.Equal(ReadingPreferences.MaxLineHeight, preferences.LineHeight);
            Assert.Equal(ReadingPreferences.MaxContentWidth, preferences.ContentWidth);
            Assert.Equal(DocumentMinimapMode.Off, preferences.DocumentMinimapMode);
            Assert.Equal(AppLanguage.System, language);
            Assert.Null(windowPlacement);
        }
        finally
        {
            DeleteDirectory(rootDirectory);
        }
    }


    [Fact]
    public async Task LoadTurnsTheMinimapOffWhenLegacySettingsHaveNoMinimapMode()
    {
        var rootDirectory = CreateTempDirectory();
        const string json = """
        {
          "theme": "Light",
          "preferences": {
            "fontFamily": "Serif",
            "fontSize": 18,
            "lineHeight": 1.7,
            "contentWidth": 820
          }
        }
        """;

        try
        {
            await File.WriteAllTextAsync(Path.Combine(rootDirectory, "settings.json"), json);

            var store = new JsonSettingsStore(rootDirectory);
            var preferences = await store.LoadPreferencesAsync();

            Assert.Equal(DocumentMinimapMode.Off, preferences.DocumentMinimapMode);
        }
        finally
        {
            DeleteDirectory(rootDirectory);
        }
    }

    [Fact]
    public async Task LoadKeepsSavedPreferencesThatDifferFromTheCurrentDefaults()
    {
        // Умолчания сменились с Serif 18 / 1,7 на Sans 14 / 1,6 (MM-60): у того,
        // кто уже сохранял настройки, они остаются прежними.
        var rootDirectory = CreateTempDirectory();
        const string json = """
        {
          "preferences": {
            "fontFamily": "Serif",
            "fontSize": 18,
            "lineHeight": 1.7,
            "contentWidth": 820
          }
        }
        """;

        try
        {
            await File.WriteAllTextAsync(Path.Combine(rootDirectory, "settings.json"), json);

            var store = new JsonSettingsStore(rootDirectory);
            var preferences = await store.LoadPreferencesAsync();

            Assert.Equal(new ReadingPreferences(FontFamilyMode.Serif, 18, 1.7, 820), preferences);
        }
        finally
        {
            DeleteDirectory(rootDirectory);
        }
    }

    [Fact]
    public async Task LoadFallsBackToNullWindowPlacementWhenPlacementIsInvalid()
    {
        var rootDirectory = CreateTempDirectory();
        const string json = """
        {
          "theme": "Light",
          "preferences": {
            "fontFamily": "Serif",
            "fontSize": 18,
            "lineHeight": 1.7,
            "contentWidth": 720
          },
          "language": "English",
          "windowPlacement": {
            "x": 100,
            "y": 100,
            "width": 0,
            "height": 640,
            "isMaximized": false
          }
        }
        """;

        try
        {
            await File.WriteAllTextAsync(Path.Combine(rootDirectory, "settings.json"), json);

            var store = new JsonSettingsStore(rootDirectory);
            var windowPlacement = await store.LoadWindowPlacementAsync();

            Assert.Null(windowPlacement);
        }
        finally
        {
            DeleteDirectory(rootDirectory);
        }
    }

    [Fact]
    public async Task RecentEntriesRoundTripAndKeepOtherSettings()
    {
        var rootDirectory = CreateTempDirectory();
        try
        {
            var openedAt = new DateTimeOffset(2026, 9, 19, 10, 30, 0, TimeSpan.FromHours(3));
            IReadOnlyList<RecentEntry> expected =
            [
                new(Path.Combine(rootDirectory, "notes.md"), RecentEntryKind.File, openedAt),
                new(Path.Combine(rootDirectory, "docs"), RecentEntryKind.Folder, openedAt.AddDays(-1))
            ];

            var store = new JsonSettingsStore(rootDirectory);
            await store.SaveThemeAsync(ThemeMode.Dark);
            await store.SaveRecentAsync(expected);

            var reloaded = new JsonSettingsStore(rootDirectory);

            Assert.Equal(expected, await reloaded.LoadRecentAsync());
            Assert.Equal(ThemeMode.Dark, await reloaded.LoadThemeAsync());
        }
        finally
        {
            DeleteDirectory(rootDirectory);
        }
    }

    [Fact]
    public async Task OldSettingsFileWithoutRecentLoadsEmptyList()
    {
        var rootDirectory = CreateTempDirectory();
        const string json = """
        {
          "theme": "Dark",
          "preferences": { "fontFamily": "Serif", "fontSize": 18, "lineHeight": 1.7, "contentWidth": 720 },
          "language": "English"
        }
        """;

        try
        {
            await File.WriteAllTextAsync(Path.Combine(rootDirectory, "settings.json"), json);

            var store = new JsonSettingsStore(rootDirectory);

            Assert.Empty(await store.LoadRecentAsync());
            Assert.Equal(ThemeMode.Dark, await store.LoadThemeAsync());
        }
        finally
        {
            DeleteDirectory(rootDirectory);
        }
    }

    [Theory]
    [InlineData("42")]
    [InlineData("\"broken\"")]
    [InlineData("{ \"path\": 1 }")]
    public async Task BrokenRecentFieldLoadsEmptyListWithoutResettingOtherSettings(string recentJson)
    {
        var rootDirectory = CreateTempDirectory();
        var json = $$"""
        {
          "theme": "Dark",
          "preferences": { "fontFamily": "Serif", "fontSize": 18, "lineHeight": 1.7, "contentWidth": 720 },
          "language": "Russian",
          "recent": {{recentJson}}
        }
        """;

        try
        {
            await File.WriteAllTextAsync(Path.Combine(rootDirectory, "settings.json"), json);

            var store = new JsonSettingsStore(rootDirectory);

            Assert.Empty(await store.LoadRecentAsync());
            Assert.Equal(ThemeMode.Dark, await store.LoadThemeAsync());
            Assert.Equal(AppLanguage.Russian, await store.LoadLanguageAsync());
        }
        finally
        {
            DeleteDirectory(rootDirectory);
        }
    }

    [Fact]
    public async Task BrokenRecentEntryIsDroppedAndTheRestIsKept()
    {
        var rootDirectory = CreateTempDirectory();
        var notes = Path.Combine(rootDirectory, "notes.md");
        var json = $$"""
        {
          "theme": "Light",
          "preferences": { "fontFamily": "Serif", "fontSize": 18, "lineHeight": 1.7, "contentWidth": 720 },
          "language": "English",
          "recent": [
            { "path": {{JsonEncode(notes)}}, "kind": "File", "openedAt": "2026-09-19T10:00:00+00:00" },
            { "path": "/tmp/x.md", "kind": "Spaceship", "openedAt": "2026-09-19T11:00:00+00:00" },
            { "path": "relative.md", "kind": "File", "openedAt": "2026-09-19T12:00:00+00:00" }
          ]
        }
        """;

        try
        {
            await File.WriteAllTextAsync(Path.Combine(rootDirectory, "settings.json"), json);

            var store = new JsonSettingsStore(rootDirectory);
            var recent = await store.LoadRecentAsync();

            var entry = Assert.Single(recent);
            Assert.Equal(notes, entry.Path);
            Assert.Equal(RecentEntryKind.File, entry.Kind);
        }
        finally
        {
            DeleteDirectory(rootDirectory);
        }
    }

    private static string JsonEncode(string value)
        => System.Text.Json.JsonSerializer.Serialize(value, TestJsonContext.Default.String);

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "MarkMello.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(string))]
internal sealed partial class TestJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
