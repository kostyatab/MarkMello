using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MarkMello.Presentation.ViewModels;

namespace MarkMello.Presentation.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
    }

    public AboutWindow(AboutViewModel viewModel)
        : this()
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        DataContext = viewModel;
    }

    /// <summary>
    /// Окно закрывается тем же, чем закрываются карточки: `Esc` и `⌘W` / `Ctrl+W`.
    /// Кнопка окна остаётся системной.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Handled)
        {
            return;
        }

        var closeShortcut = e.Key == Key.W
            && e.KeyModifiers == (OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control);

        if (e.Key != Key.Escape && !closeShortcut)
        {
            return;
        }

        e.Handled = true;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        (DataContext as IDisposable)?.Dispose();
        base.OnClosed(e);
    }

    /// <summary>Ссылки открываются в браузере системы — только по клику (ADR-0003 §5).</summary>
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
