using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MarkMello.Presentation.Localization;

namespace MarkMello.Presentation.ViewModels;

/// <summary>
/// Окно «О MarkMello» (ADR-0009 Rule 7): имя, версия сборки, лицензия, автор и ссылки
/// проекта — те же сведения, что в нижней строке карточки «Настройки». Создаётся только
/// по нажатию пункта меню, на быстрый путь открытия документа не влияет.
/// </summary>
public sealed class AboutViewModel : ObservableObject, IDisposable
{
    private static readonly string[] LocalizedPropertyNames =
    [
        nameof(WindowTitle),
        nameof(VersionLine),
        nameof(WebsiteLabel)
    ];

    private readonly ILocalizationService _localization;
    private readonly string _version = AppProductInfo.GetVersion();
    private bool _isDisposed;

    public AboutViewModel(ILocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(localization);
        _localization = localization;
        _localization.PropertyChanged += OnLocalizationChanged;
    }

    public string WindowTitle => _localization["AboutWindowTitle"];

    /// <summary>
    /// Неизменные сведения — свойства экземпляра, а не статика: окно связывается с ними
    /// compiled-биндингами, а им нужен экземпляр.
    /// </summary>
    public string ProductName { get; } = AppProductInfo.Name;

    /// <summary>Версия сборки с подписью: «Версия 1.0.0-preview.12».</summary>
    public string VersionLine => _localization.Format("AboutVersion", _version);

    public string License { get; } = AppProductInfo.License;

    public string Author { get; } = AppProductInfo.Author;

    public string AuthorUrl { get; } = AppProductInfo.AuthorUrl;

    public string WebsiteLabel => _localization["AboutWebsite"];

    public string WebsiteUrl { get; } = AppProductInfo.WebsiteUrl;

    public string TelegramUrl { get; } = AppProductInfo.TelegramUrl;

    public string GitHubUrl { get; } = AppProductInfo.GitHubUrl;

    /// <summary>Смена языка перерисовывает подписи в уже открытом окне, а не только в новом.</summary>
    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        foreach (var propertyName in LocalizedPropertyNames)
        {
            OnPropertyChanged(propertyName);
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _localization.PropertyChanged -= OnLocalizationChanged;
    }
}
