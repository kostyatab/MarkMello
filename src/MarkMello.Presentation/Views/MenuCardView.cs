using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MarkMello.Presentation.ViewModels;

namespace MarkMello.Presentation.Views;

/// <summary>
/// Общее поведение меню-карточек сайдбара (ADR-0009 Rule 4): пункт выполняет команду
/// и закрывает меню, стрелки водят по пунктам, открытая карточка держит фокус —
/// то, что у попапа Avalonia было даром. Фокус возвращает окно, когда меню закрывается.
/// Меню ⋯ живёт по тем же правилам в <see cref="AppMenuPanelView"/>: оно появилось
/// раньше и свой обработчик уже имеет.
/// </summary>
public abstract class MenuCardView : UserControl
{
    protected MenuCardView()
    {
        AddHandler(Button.ClickEvent, OnMenuItemClick);
        AddHandler(KeyDownEvent, OnMenuKeyDown, RoutingStrategies.Bubble);
    }

    /// <summary>Открыто ли именно это меню: закрывать чужое меню карточка не должна.</summary>
    protected abstract bool IsOpen(ShellViewModel viewModel);

    /// <summary>
    /// Перевести фокус на первый пункт. <see cref="NavigationMethod.Directional"/> ставит
    /// и подсветку: меню, вызванное с клавиатуры, сразу показывает, где пользователь.
    /// </summary>
    public void FocusFirstItem(NavigationMethod method = NavigationMethod.Unspecified)
        => Items().FirstOrDefault()?.Focus(method);

    /// <summary>
    /// Карточка создаётся в момент открытия, поэтому фокус ставится здесь: без него
    /// стрелки и Enter не дошли бы до пунктов. Подсветку при этом не зажигаем —
    /// она только под мышью и при обходе с клавиатуры (ADR-0009 Rule 4).
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(() =>
        {
            // Меню, вызванное с клавиатуры, фокус уже забрало — вместе с подсветкой.
            if (!Items().Any(static item => item.IsFocused))
            {
                FocusFirstItem();
            }
        });
    }

    /// <summary>
    /// Пункт выполнил команду — меню закрывается. Закрыть его прямо в Click нельзя:
    /// кнопка читает команду после события, а без меню у неё уже не будет контекста.
    /// </summary>
    private void OnMenuItemClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel viewModel)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (IsOpen(viewModel))
            {
                viewModel.CloseOverlayCommand.Execute(null);
            }
        });
    }

    /// <summary>Стрелки ходят по пунктам по кругу, как в системном меню.</summary>
    private void OnMenuKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Up or Key.Down))
        {
            return;
        }

        var items = Items().ToList();
        if (items.Count == 0)
        {
            return;
        }

        var current = items.FindIndex(static item => item.IsFocused);
        var step = e.Key == Key.Down ? 1 : -1;
        var next = current < 0
            ? (e.Key == Key.Down ? 0 : items.Count - 1)
            : (current + step + items.Count) % items.Count;

        items[next].Focus(NavigationMethod.Directional);
        e.Handled = true;
    }

    private IEnumerable<Button> Items()
        => this.GetVisualDescendants()
            .OfType<Button>()
            .Where(static button => button.IsVisible && button.IsEffectivelyEnabled);
}
