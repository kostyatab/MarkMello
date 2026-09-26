using System.Reflection;
using System.Xml.Linq;
using Avalonia.Controls;
using MarkMello.Domain;
using MarkMello.Presentation.Localization;
using MarkMello.Presentation.ViewModels;
using MarkMello.Presentation.Views;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Имя приложения — Softmark (ADR-0011). Старое имя остаётся в коде (namespace,
/// проекты, пути ресурсов), но в интерфейсе его быть не должно, кроме одной строки
/// атрибуции. Мерж апстрима легко приносит строку со словом «MarkMello» — эти
/// тесты её ловят.
/// </summary>
[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class ProductIdentityTests
{
    private const string OldName = "MarkMello";
    private const string AttributionKey = "AboutForkAttribution";

    /// <summary>Атрибуты разметки, которые пользователь видит как текст.</summary>
    private static readonly string[] VisibleAttributes =
        ["Text", "Content", "Title", "ToolTip.Tip", "Watermark", "PlaceholderText", "Header"];

    private static readonly string[] MarkupFolders = ["Views", "Themes"];

    private readonly AvaloniaHeadlessFixture _fixture;

    public ProductIdentityTests(AvaloniaHeadlessFixture fixture) => _fixture = fixture;

    [Theory]
    [InlineData("English")]
    [InlineData("Russian")]
    public void OnlyTheAttributionMentionsTheOldName(string dictionary)
    {
        var strings = GetDictionary(dictionary);

        Assert.Empty(strings
            .Where(static pair => pair.Key != AttributionKey && pair.Value.Contains(OldName, StringComparison.OrdinalIgnoreCase))
            .Select(static pair => $"{pair.Key} = {pair.Value}"));
        Assert.Contains(OldName, strings[AttributionKey], StringComparison.Ordinal);
    }

    /// <summary>Строки с именем приложения берут его из <c>AppProductInfo.Name</c>.</summary>
    [Theory]
    [InlineData(AppLanguage.English, "AppMenuAbout", "About Softmark")]
    [InlineData(AppLanguage.Russian, "AboutWindowTitle", "О Softmark")]
    [InlineData(AppLanguage.English, "DirtyPromptCloseWindow", "Otherwise your changes will be lost when you quit Softmark.")]
    [InlineData(AppLanguage.Russian, "UpdateWindowTitle", "Обновление Softmark")]
    public void NamedStringsCarryTheProductName(AppLanguage language, string key, string expected)
    {
        Assert.Equal(expected, new LocalizationService(language)[key]);
    }

    /// <summary>
    /// Подстановка имени не ломает плейсхолдеры формата: <c>{0}</c> и <c>{1}</c> по-прежнему
    /// заполняются расширениями файлов.
    /// </summary>
    [Theory]
    [InlineData(AppLanguage.English, "Softmark opens .md and .markdown files.")]
    [InlineData(AppLanguage.Russian, "Softmark открывает файлы .md и .markdown.")]
    public void UnsupportedTypeDetailsKeepTheirPlaceholders(AppLanguage language, string expected)
    {
        Assert.Equal(expected, new LocalizationService(language).Format("ErrorUnsupportedTypeDetails", ".md", ".markdown"));
    }

    [Fact]
    public void MarkupHasNoVisibleOldName()
    {
        var presentation = IconMarkupTests.PresentationSourceDirectory();
        var offenders = MarkupFolders
            .SelectMany(folder => Directory.GetFiles(Path.Combine(presentation, folder), "*.axaml", SearchOption.AllDirectories))
            .SelectMany(file => VisibleText(XDocument.Load(file)).Select(text => $"{Path.GetFileName(file)}: {text}"))
            // Пути ресурсов (avares://MarkMello.Presentation/...) — не текст, их не переименовываем.
            .Where(static entry => entry.Contains(OldName, StringComparison.OrdinalIgnoreCase)
                && !entry.Contains("avares://" + OldName + ".", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offenders);
    }

    /// <summary>
    /// Вордмарк стартового экрана — «Soft» + акцентное «mark» одним словом: пробел
    /// между двумя Run разбил бы его надвое.
    /// </summary>
    [Fact]
    public Task WelcomeWordmarkReadsAsOneWord()
    {
        return _fixture.RunAsync(() =>
        {
            var view = new WelcomeView();

            var wordmark = view.GetControl<TextBlock>("WelcomeWordmark");

            Assert.Equal("Softmark", AboutWindowTests.WordmarkText(wordmark));
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData("v1.0.0", "1.0.0")]
    [InlineData("V2.3.4", "2.3.4")]
    [InlineData("1.0.0", "1.0.0")]
    [InlineData("v1.0.0-preview.12+abc1234", "1.0.0-preview.12")]
    [InlineData("1.0.0-preview.12+abc1234", "1.0.0-preview.12")]
    [InlineData("vnext", "vnext")]
    public void VersionDropsTheTagPrefixAndBuildMetadata(string informationalVersion, string expected)
    {
        Assert.Equal(expected, AppProductInfo.NormalizeVersion(informationalVersion));
    }

    private static IEnumerable<string> VisibleText(XDocument document)
    {
        foreach (var element in document.Descendants())
        {
            foreach (var attribute in element.Attributes())
            {
                if (VisibleAttributes.Contains(attribute.Name.LocalName, StringComparer.Ordinal))
                {
                    yield return attribute.Value;
                }
            }

            if (!element.HasElements && !string.IsNullOrWhiteSpace(element.Value))
            {
                yield return element.Value;
            }
        }
    }

    private static IReadOnlyDictionary<string, string> GetDictionary(string fieldName)
    {
        var field = typeof(LocalizationService).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Localization dictionary '{fieldName}' was not found.");

        return Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(field.GetValue(null));
    }
}
