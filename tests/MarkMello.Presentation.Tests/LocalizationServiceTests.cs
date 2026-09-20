using MarkMello.Domain;
using MarkMello.Presentation.Localization;
using System.Reflection;

namespace MarkMello.Presentation.Tests;

public sealed class LocalizationServiceTests
{
    [Fact]
    public void EnglishAndRussianDictionariesExposeSameKeys()
    {
        var english = GetDictionary("English");
        var russian = GetDictionary("Russian");

        Assert.Empty(english.Keys.Except(russian.Keys));
        Assert.Empty(russian.Keys.Except(english.Keys));
    }


    /// <summary>
    /// Плашка счётчика набирается числительным: в русском три формы, в английском две.
    /// </summary>
    [Theory]
    [InlineData(1, "1 слово")]
    [InlineData(2, "2 слова")]
    [InlineData(5, "5 слов")]
    [InlineData(11, "11 слов")]
    [InlineData(21, "21 слово")]
    [InlineData(102, "102 слова")]
    [InlineData(114, "114 слов")]
    [InlineData(0, "0 слов")]
    public void RussianWordCountAgreesWithTheNumeral(int count, string expected)
    {
        var localization = new LocalizationService(AppLanguage.Russian);

        Assert.Equal(expected, localization.FormatPlural("StatusWords", count));
    }

    [Theory]
    [InlineData(1, "1 word")]
    [InlineData(2, "2 words")]
    [InlineData(0, "0 words")]
    public void EnglishWordCountAgreesWithTheNumeral(int count, string expected)
    {
        var localization = new LocalizationService(AppLanguage.English);

        Assert.Equal(expected, localization.FormatPlural("StatusWords", count));
    }

    [Fact]
    public void SetLanguageRaisesIndexerNotificationsForActiveBindings()
    {
        var localization = new LocalizationService(AppLanguage.English);
        var names = new List<string?>();
        localization.PropertyChanged += (_, e) => names.Add(e.PropertyName);

        localization.SetLanguage(AppLanguage.Russian);

        Assert.Contains(nameof(ILocalizationService.SelectedLanguage), names);
        Assert.Contains(nameof(ILocalizationService.EffectiveLanguage), names);
        Assert.Contains(nameof(ILocalizationService.Culture), names);
        Assert.Contains("Item", names);
        Assert.Contains("Item[]", names);
        Assert.Contains(string.Empty, names);
        Assert.Equal("Тихое место для чтения Markdown.", localization["WelcomeTagline"]);
    }

    private static IReadOnlyDictionary<string, string> GetDictionary(string fieldName)
    {
        var field = typeof(LocalizationService).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
        if (field is null)
        {
            throw new InvalidOperationException($"Localization dictionary '{fieldName}' was not found.");
        }

        var value = field.GetValue(null);
        return Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(value);
    }
}
