using TextMateSharp.Internal.Grammars.Reader;
using TextMateSharp.Internal.Types;
using TextMateSharp.Registry;
using TextMateSharp.Themes;

namespace MarkMello.Infrastructure.Highlighting;

/// <summary>
/// Источник грамматик для <see cref="Registry"/> — встроенный zip
/// (<see cref="TextMateGrammarCatalog"/>). Тема TextMate не нужна: цвета
/// выбираются по скоупам (ADR-0010 §1), поэтому реестр получает пустую тему.
/// </summary>
internal sealed class TextMateRegistryOptions(TextMateGrammarCatalog catalog) : IRegistryOptions
{
    /// <summary>
    /// Пакет языка, грамматика которого сейчас загружается. Реестр отдаёт только
    /// грамматики этого пакета: чужие языки внутри блока (скрипт в heredoc bash,
    /// fence внутри markdown) остаются без цветов. Иначе bash тянет за собой
    /// markdown, а тот — десятки языков: +56 МБ памяти ради одного блока.
    /// </summary>
    public string? Package { get; set; }

    public IRawGrammar? GetGrammar(string scopeName)
        => Package is not null && string.Equals(catalog.FindPackage(scopeName), Package, StringComparison.Ordinal)
            ? catalog.ReadGrammar(scopeName, GrammarReader.ReadGrammarSync)
            : null;

    public ICollection<string>? GetInjections(string scopeName) => null;

    public IRawTheme GetTheme(string scopeName) => EmptyTheme.Instance;

    public IRawTheme GetDefaultTheme() => EmptyTheme.Instance;

    private sealed class EmptyTheme : IRawTheme
    {
        public static EmptyTheme Instance { get; } = new();

        public string GetName() => "MarkMello";

        public string? GetInclude() => null;

        public ICollection<IRawThemeSetting> GetSettings() => [];

        public ICollection<IRawThemeSetting> GetTokenColors() => [];

        public ICollection<KeyValuePair<string, object>> GetGuiColors() => [];
    }
}
