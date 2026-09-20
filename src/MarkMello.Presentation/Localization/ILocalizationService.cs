using System.ComponentModel;
using System.Globalization;
using MarkMello.Domain;

namespace MarkMello.Presentation.Localization;

public interface ILocalizationService : INotifyPropertyChanged
{
    AppLanguage SelectedLanguage { get; }

    AppLanguage EffectiveLanguage { get; }

    CultureInfo Culture { get; }

    string this[string key] { get; }

    string Format(string key, params object?[] args);

    /// <summary>
    /// Строка с числительным: ключ дополняется формой — <c>One</c>, <c>Few</c> или
    /// <c>Many</c> — по правилам текущего языка.
    /// </summary>
    string FormatPlural(string key, int count);

    void SetLanguage(AppLanguage language);
}
