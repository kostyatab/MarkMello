using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MarkMello.Presentation.ViewModels;

namespace MarkMello.Presentation.Views;

/// <summary>
/// Окно обновления (ADR-0004, «Update Model»). Своего состояния у окна нет: оно показывает
/// общее <see cref="UpdateViewModel"/>, поэтому закрытие окна загрузку не прерывает, а
/// открытое заново — показывает ту же загрузку.
/// </summary>
public partial class UpdateWindow : Window
{
    private readonly UpdateViewModel? _viewModel;

    public UpdateWindow()
    {
        InitializeComponent();
    }

    public UpdateWindow(UpdateViewModel viewModel)
        : this()
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.CloseRequested += OnCloseRequested;
    }

    /// <summary>
    /// Окно закрывается тем же, чем «О Softmark» и карточки: `Esc` и `⌘W` / `Ctrl+W`.
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
        // Модель общая и переживает окно — отписываемся, чтобы она не держала закрытое окно.
        if (_viewModel is not null)
        {
            _viewModel.CloseRequested -= OnCloseRequested;
        }

        base.OnClosed(e);
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();

    /// <summary>«Что нового» открывается в браузере системы — только по клику.</summary>
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
