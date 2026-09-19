using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MarkMello.Presentation.Views;

public partial class AppSettingsDialogView : UserControl
{
    public AppSettingsDialogView()
    {
        InitializeComponent();
    }

    /// <summary>Ссылки внизу окна открываются в браузере системы — только по клику.</summary>
    private async void OnLinkClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: string rawUrl }
            || !Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
        {
            return;
        }

        var launcher = TopLevel.GetTopLevel(this)?.Launcher;
        if (launcher is null)
        {
            return;
        }

        await launcher.LaunchUriAsync(uri).ConfigureAwait(true);
    }
}
