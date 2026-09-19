using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MarkMello.Presentation.ViewModels;

namespace MarkMello.Presentation.Views;

public partial class WelcomeView : UserControl
{
    public WelcomeView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Проверка «Недавних» на диске — после первого кадра стартового экрана, а не вместе
    /// с ним: фоновый приоритет ждёт, пока пройдёт отрисовка.
    /// </summary>
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        Dispatcher.UIThread.Post(
            () => (DataContext as ShellViewModel)?.ProbeRecentItems(),
            DispatcherPriority.Background);
    }
}
