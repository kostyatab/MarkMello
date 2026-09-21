using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using MarkMello.Presentation.ViewModels;

namespace MarkMello.Presentation.Views;

/// <summary>
/// Полоса вкладок. Активация и средний клик обрабатываются здесь, потому что
/// это жесты, а не команды: XAML-кнопка на всю вкладку сломала бы крестик внутри неё.
/// </summary>
public partial class TabStripView : UserControl
{
    private const string TabClass = "mm-tab";

    private Control? _pressedTab;

    public TabStripView()
    {
        InitializeComponent();

        // Вкладка фокусируется с клавиатуры, но не мышью: клик по ней оставляет фокус
        // в документе, и стрелки продолжают его листать. FocusManager отдаёт фокус на
        // туннеле у самого источника нажатия, поэтому вкладка перестаёт быть фокусируемой
        // раньше, чем он до неё доходит, и становится снова после всплытия.
        AddHandler(PointerPressedEvent, OnStripPointerPressedTunnel, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnStripPointerPressedBubble, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private void OnStripSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is ShellViewModel viewModel)
        {
            // Ширина полосы — единственное, что решает, сколько вкладок помещается.
            viewModel.OpenDocuments.AvailableWidth = e.NewSize.Width;
        }
    }

    private void OnStripPointerPressedTunnel(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Visual source
            && source.GetSelfAndVisualAncestors().OfType<Control>().FirstOrDefault(KeepsFocusOffOnClick) is { } tab)
        {
            _pressedTab = tab;
            tab.Focusable = false;
        }
    }

    private void OnStripPointerPressedBubble(object? sender, PointerPressedEventArgs e)
    {
        // Локальное значение снимается, и вкладка снова берёт Focusable из своего стиля.
        _pressedTab?.ClearValue(FocusableProperty);
        _pressedTab = null;
    }

    private void OnTabPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: DocumentTabViewModel tab }
            || DataContext is not ShellViewModel viewModel)
        {
            return;
        }

        var point = e.GetCurrentPoint(this);

        if (point.Properties.IsMiddleButtonPressed)
        {
            viewModel.OpenDocuments.CloseCommand.Execute(tab);
            e.Handled = true;
            return;
        }

        if (point.Properties.IsLeftButtonPressed)
        {
            viewModel.OpenDocuments.ActivateCommand.Execute(tab);

            // Вкладка стоит в строке окна: необработанное нажатие ушло бы в перетаскивание окна.
            e.Handled = true;
        }
    }

    /// <summary>Вкладка в фокусе открывается по Enter или пробелу, как кнопка.</summary>
    private void OnTabKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space)
            || e.KeyModifiers != KeyModifiers.None
            || sender is not Control { DataContext: DocumentTabViewModel tab }
            || DataContext is not ShellViewModel viewModel)
        {
            return;
        }

        viewModel.OpenDocuments.ActivateCommand.Execute(tab);
        e.Handled = true;
    }

    /// <summary>
    /// «ещё N» раскрывает список скрытых вкладок карточкой в слое меню окна
    /// (ADR-0009 Rule 4): позиция считается от самой кнопки, поэтому окно узнаёт её
    /// до того, как меню откроется. Повторное нажатие меню закрывает.
    /// </summary>
    private void OnTabsOverflowButtonClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel viewModel)
        {
            return;
        }

        if (TopLevel.GetTopLevel(this) is IMenuCardHost host)
        {
            host.AnchorMenuCard(TabsOverflowButton);
        }

        viewModel.ToggleTabsOverflowMenuCommand.Execute(null);
    }

    /// <summary>
    /// Мышью фокус не берут ни вкладка, ни «ещё N»: фокус остаётся в документе. Кнопка
    /// «ещё N» пропадает, когда её меню закрывает ✕ последней скрытой вкладки, — и
    /// фокусу, вернувшемуся из меню на неё, было бы некуда встать.
    /// </summary>
    private bool KeepsFocusOffOnClick(Control control)
        => IsTab(control) || ReferenceEquals(control, TabsOverflowButton);

    private static bool IsTab(Control control) => control is Border && control.Classes.Contains(TabClass);
}
