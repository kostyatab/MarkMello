using System.IO.Compression;
using System.Text.Json;

namespace MarkMello.Infrastructure.Highlighting;

/// <summary>
/// Грамматики TextMate из встроенного zip (ADR-0010 §6): какие метки блоков
/// знакомы и где лежит грамматика каждого скоупа. Сам zip собирается при
/// сборке из пакета TextMateSharp.Grammars (<c>TextMateGrammars.targets</c>).
///
/// Создаётся при первом блоке кода с меткой — читает только <c>package.json</c>
/// грамматик; файлы грамматик читаются по одному, по требованию.
/// </summary>
internal sealed class TextMateGrammarCatalog : IDisposable
{
    internal const string ResourceName = "MarkMello.Infrastructure.Highlighting.Grammars.zip";

    private static readonly (string Label, string Language)[] ExtraLabels = [("shell", "shellscript")];

    private readonly ZipArchive _archive;
    private readonly Lock _archiveGate = new();
    private readonly Dictionary<string, string> _scopeByLabel;
    private readonly Dictionary<string, string> _entryByScope;

    private TextMateGrammarCatalog(ZipArchive archive)
    {
        _archive = archive;
        (_scopeByLabel, _entryByScope) = ReadPackages(archive);
    }

    public static TextMateGrammarCatalog LoadEmbedded()
    {
        var stream = typeof(TextMateGrammarCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {ResourceName} is missing.");
        return new TextMateGrammarCatalog(new ZipArchive(stream, ZipArchiveMode.Read));
    }

    /// <summary>
    /// Скоуп грамматики по метке блока в нижнем регистре: идентификатор языка,
    /// его синоним или расширение файла без точки. <c>null</c> — язык незнаком.
    /// </summary>
    public string? FindScope(string label)
        => _scopeByLabel.TryGetValue(label, out var scope) ? scope : null;

    /// <summary>
    /// Пакет грамматики (папка VS Code-расширения, например <c>shellscript</c>),
    /// или <c>null</c>, если скоупа нет.
    /// </summary>
    public string? FindPackage(string scopeName)
        => _entryByScope.TryGetValue(scopeName, out var entryName) ? entryName[..entryName.IndexOf('.', StringComparison.Ordinal)] : null;

    /// <summary>
    /// Читает грамматику скоупа через <paramref name="read"/>. <c>default</c> —
    /// грамматики нет.
    /// </summary>
    public T? ReadGrammar<T>(string scopeName, Func<StreamReader, T> read)
    {
        if (!_entryByScope.TryGetValue(scopeName, out var entryName))
        {
            return default;
        }

        lock (_archiveGate)
        {
            var entry = _archive.GetEntry(entryName);
            if (entry is null)
            {
                return default;
            }

            using var reader = new StreamReader(entry.Open());
            return read(reader);
        }
    }

    public void Dispose() => _archive.Dispose();

    /// <summary>
    /// Метки собираются в три прохода — идентификаторы, синонимы, расширения, —
    /// чтобы идентификатор одного языка не перебивался расширением другого.
    /// Внутри прохода побеждает первая грамматика по имени записи.
    /// </summary>
    private static (Dictionary<string, string> ScopeByLabel, Dictionary<string, string> EntryByScope) ReadPackages(ZipArchive archive)
    {
        var languages = new List<(string Id, IReadOnlyList<string> Aliases, IReadOnlyList<string> Extensions)>();
        var scopeByLanguage = new Dictionary<string, string>(StringComparer.Ordinal);
        var entryByScope = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var entry in archive.Entries.OrderBy(entry => entry.FullName, StringComparer.Ordinal))
        {
            const string PackageSuffix = ".package.json";
            if (!entry.FullName.EndsWith(PackageSuffix, StringComparison.Ordinal))
            {
                continue;
            }

            var folder = entry.FullName[..^PackageSuffix.Length];
            using var stream = entry.Open();
            using var package = JsonDocument.Parse(stream, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (!package.RootElement.TryGetProperty("contributes", out var contributes))
            {
                continue;
            }

            if (contributes.TryGetProperty("grammars", out var grammars) && grammars.ValueKind == JsonValueKind.Array)
            {
                foreach (var grammar in grammars.EnumerateArray())
                {
                    var scopeName = GetString(grammar, "scopeName");
                    var path = GetString(grammar, "path");
                    if (scopeName is null || path is null)
                    {
                        continue;
                    }

                    entryByScope.TryAdd(scopeName, ToEntryName(folder, path));
                    if (GetString(grammar, "language") is { } language)
                    {
                        scopeByLanguage.TryAdd(language, scopeName);
                    }
                }
            }

            if (contributes.TryGetProperty("languages", out var languageList) && languageList.ValueKind == JsonValueKind.Array)
            {
                foreach (var language in languageList.EnumerateArray())
                {
                    if (GetString(language, "id") is { } id)
                    {
                        languages.Add((id, GetStrings(language, "aliases"), GetStrings(language, "extensions")));
                    }
                }
            }
        }

        var scopeByLabel = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var labels in new Func<(string Id, IReadOnlyList<string> Aliases, IReadOnlyList<string> Extensions), IEnumerable<string>>[]
                 {
                     language => [language.Id],
                     language => language.Aliases,
                     language => language.Extensions.Select(extension => extension.TrimStart('.')),
                 })
        {
            foreach (var language in languages)
            {
                if (!scopeByLanguage.TryGetValue(language.Id, out var scope))
                {
                    continue;
                }

                foreach (var label in labels(language))
                {
                    if (label.Length > 0)
                    {
                        scopeByLabel.TryAdd(label.ToLowerInvariant(), scope);
                    }
                }
            }
        }

        // Частые метки GitHub, которых нет среди синонимов грамматик.
        foreach (var (label, language) in ExtraLabels)
        {
            if (scopeByLanguage.TryGetValue(language, out var scope))
            {
                scopeByLabel.TryAdd(label, scope);
            }
        }

        return (scopeByLabel, entryByScope);
    }

    /// <summary><c>./syntaxes/csharp.tmLanguage.json</c> → <c>csharp.syntaxes.csharp.tmLanguage.json</c>.</summary>
    private static string ToEntryName(string folder, string path)
    {
        var relative = path.StartsWith("./", StringComparison.Ordinal) ? path[2..] : path;
        return folder + "." + relative.Replace('/', '.');
    }

    private static string? GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static List<string> GetStrings(JsonElement element, string name)
    {
        var result = new List<string>();
        if (element.TryGetProperty(name, out var values) && values.ValueKind == JsonValueKind.Array)
        {
            foreach (var value in values.EnumerateArray())
            {
                if (value.ValueKind == JsonValueKind.String && value.GetString() is { } text)
                {
                    result.Add(text);
                }
            }
        }

        return result;
    }
}
