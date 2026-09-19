using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MarkMello.Presentation.ViewModels;

namespace MarkMello.Presentation.Views;

/// <summary>
/// Карточка поиска под кнопкой поиска. Создаётся заново при каждом открытии, поэтому
/// переходы по совпадениям — всплывающие события: окно ловит их у себя и не держит
/// ссылку на конкретный экземпляр карточки.
/// </summary>
public partial class FindBarView : UserControl
{
    public static readonly RoutedEvent<RoutedEventArgs> FindNextRequestedEvent =
        RoutedEvent.Register<FindBarView, RoutedEventArgs>(nameof(FindNextRequested), RoutingStrategies.Bubble);

    public static readonly RoutedEvent<RoutedEventArgs> FindPreviousRequestedEvent =
        RoutedEvent.Register<FindBarView, RoutedEventArgs>(nameof(FindPreviousRequested), RoutingStrategies.Bubble);

    private TextBox? _findInput;

    public FindBarView()
    {
        InitializeComponent();
    }

    public event EventHandler<RoutedEventArgs>? FindNextRequested
    {
        add => AddHandler(FindNextRequestedEvent, value);
        remove => RemoveHandler(FindNextRequestedEvent, value);
    }

    public event EventHandler<RoutedEventArgs>? FindPreviousRequested
    {
        add => AddHandler(FindPreviousRequestedEvent, value);
        remove => RemoveHandler(FindPreviousRequestedEvent, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _findInput = this.FindControl<TextBox>("FindInput");
        FocusInputAsync();
    }

    private void FocusInputAsync()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _findInput?.Focus();
            _findInput?.SelectAll();
        }, DispatcherPriority.Background);
    }

    private void OnFindInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            (DataContext as ShellViewModel)?.CloseFindBarCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Enter)
        {
            return;
        }

        RaiseEvent(new RoutedEventArgs(e.KeyModifiers.HasFlag(KeyModifiers.Shift)
            ? FindPreviousRequestedEvent
            : FindNextRequestedEvent));
        e.Handled = true;
    }

    private void OnPreviousClick(object? sender, RoutedEventArgs e)
        => RaiseEvent(new RoutedEventArgs(FindPreviousRequestedEvent));

    private void OnNextClick(object? sender, RoutedEventArgs e)
        => RaiseEvent(new RoutedEventArgs(FindNextRequestedEvent));
}
