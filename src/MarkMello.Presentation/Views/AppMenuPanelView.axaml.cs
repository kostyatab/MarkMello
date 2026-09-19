using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MarkMello.Presentation.ViewModels;

namespace MarkMello.Presentation.Views;

public partial class AppMenuPanelView : UserControl
{
    public AppMenuPanelView()
    {
        InitializeComponent();
        AddHandler(Button.ClickEvent, OnMenuItemClick);
    }

    /// <summary>
    /// Пункт меню выполнил команду — меню закрывается. Закрыть его прямо в Click нельзя:
    /// кнопка читает команду после события, а без меню у неё уже не будет контекста.
    /// Команда, которая сама сменила оверлей (например, «Настройки…»), своё не теряет.
    /// </summary>
    private void OnMenuItemClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel viewModel)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (viewModel.IsAppMenuOpen)
            {
                viewModel.CloseOverlayCommand.Execute(null);
            }
        });
    }
}
