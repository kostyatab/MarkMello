using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;
using MarkMello.Presentation.ViewModels;

namespace MarkMello.Presentation.Views;

/// <summary>
/// Сайдбар открытой папки. Монтируется только когда папка открыта:
/// в single-file режиме контрола нет в визуальном дереве (ADR-0007 Rule 3).
/// </summary>
public partial class WorkspaceSidebarView : UserControl
{
    // Шапка по A-Chrome: на macOS слева светофор, кнопка панели справа; на Windows
    // и Linux кнопка слева. Подложка кнопки в 11px от края, как у всех кнопок сайдбара.
    private const double HeaderInset = 11;

    // Меню с клавиатуры раскрывается от подложки строки — она той же высоты, что кнопки.
    private const double RowAnchorHeight = 26;

    /// <summary>
    /// Строка дерева показывает текущий документ. Заливку рисует шаблон
    /// <see cref="TreeViewItem"/>, куда класс из шаблона данных не дотягивается, поэтому
    /// признак переносится на контейнер стилем и ловится селектором свойства.
    /// </summary>
    public static readonly AttachedProperty<bool> IsActiveRowProperty =
        AvaloniaProperty.RegisterAttached<WorkspaceSidebarView, TreeViewItem, bool>("IsActiveRow");

    public static bool GetIsActiveRow(TreeViewItem item) => item.GetValue(IsActiveRowProperty);

    public static void SetIsActiveRow(TreeViewItem item, bool value) => item.SetValue(IsActiveRowProperty, value);

    /// <summary>Строка ждёт ответа в диалоге удаления — так же переносится на контейнер.</summary>
    public static readonly AttachedProperty<bool> IsPendingDeleteRowProperty =
        AvaloniaProperty.RegisterAttached<WorkspaceSidebarView, TreeViewItem, bool>("IsPendingDeleteRow");

    public static bool GetIsPendingDeleteRow(TreeViewItem item) => item.GetValue(IsPendingDeleteRowProperty);

    public static void SetIsPendingDeleteRow(TreeViewItem item, bool value) => item.SetValue(IsPendingDeleteRowProperty, value);

    public WorkspaceSidebarView()
    {
        InitializeComponent();

        var isMacOS = OperatingSystem.IsMacOS();
        var (padding, alignment) = CalculateHeaderLayout(isMacOS);
        SidebarHeader.Height = MainWindow.CalculateWindowRowHeight(isMacOS);
        SidebarHeader.Padding = padding;
        SidebarToggleButton.HorizontalAlignment = alignment;

        // TreeViewItem помечает нажатие обработанным ради выделения, поэтому Tapped
        // до строки не доходит: слушаем отпускание кнопки вместе с обработанными событиями.
        FileTree.AddHandler(
            PointerReleasedEvent,
            OnTreePointerReleased,
            RoutingStrategies.Bubble,
            handledEventsToo: true);

        // Контекстное меню открывается на нажатии, как системное, и по той же причине
        // слушает обработанные события: строку забирает себе TreeViewItem.
        FileTree.AddHandler(
            PointerPressedEvent,
            OnTreePointerPressed,
            RoutingStrategies.Bubble,
            handledEventsToo: true);

        // Esc внутри TextBox помечается обработанным самим полем.
        SearchInput.AddHandler(
            KeyDownEvent,
            OnSearchKeyDown,
            RoutingStrategies.Bubble,
            handledEventsToo: true);
    }

    internal static (Thickness Padding, HorizontalAlignment ToggleAlignment) CalculateHeaderLayout(bool isMacOS)
        => (new Thickness(HeaderInset, 0), isMacOS ? HorizontalAlignment.Right : HorizontalAlignment.Left);

    /// <summary>
    /// Меню «имя папки ▾» — карточка под кнопкой (ADR-0009 Rule 4): позиция считается
    /// от самой кнопки, поэтому окно узнаёт её до того, как меню откроется.
    /// </summary>
    private void OnFolderMenuButtonClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel viewModel)
        {
            return;
        }

        AnchorMenuTo(FolderMenuButton);
        viewModel.ToggleFolderMenuCommand.Execute(null);
    }

    /// <summary>Меню «+» раскрывается от левого края своей кнопки, как и меню папки.</summary>
    private void OnCreateMenuButtonClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel viewModel)
        {
            return;
        }

        AnchorMenuTo(CreateMenuButton);
        viewModel.ToggleCreateMenuCommand.Execute(null);
    }

    /// <summary>
    /// Правый клик по строке: выделяет её и открывает карточку у курсора. Выделение —
    /// то же, что у попапа раньше: по строке видно, к чему относятся пункты меню.
    /// </summary>
    private void OnTreePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed
            || e.Source is not Visual source
            || DataContext is not ShellViewModel { Workspace: { } workspace } viewModel)
        {
            return;
        }

        // В поле инлайн-переименования правый клик принадлежит самому полю: его меню
        // правки нужнее, а карточка строки забрала бы фокус и отменила ввод.
        if (source.FindAncestorOfType<TextBox>(includeSelf: true) is not null)
        {
            return;
        }

        if (source.FindAncestorOfType<TreeViewItem>(includeSelf: true)?.DataContext
            is not FileTreeNodeViewModel node)
        {
            return;
        }

        workspace.SelectedNode = node;
        AnchorMenuAt(e.GetPosition(this));
        viewModel.OpenTreeContextMenuCommand.Execute(node);
        e.Handled = true;
    }

    private void AnchorMenuTo(Control trigger)
    {
        if (trigger.TranslatePoint(default, (Visual)this) is not { } origin)
        {
            return;
        }

        SetMenuAnchor(new Rect(origin, trigger.Bounds.Size));
    }

    /// <summary>Меню у курсора: якорь без высоты, поэтому карточка встаёт прямо под точкой.</summary>
    private void AnchorMenuAt(Point point) => SetMenuAnchor(new Rect(point, default(Size)));

    private void SetMenuAnchor(Rect anchorInSidebar)
        => MenuHost()?.AnchorSidebarMenu(this, anchorInSidebar);

    private ISidebarMenuHost? MenuHost() => TopLevel.GetTopLevel(this) as ISidebarMenuHost;

    private void OnTreePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Source is not Visual source)
        {
            return;
        }

        // Клик внутри поля инлайн-переименования правит имя, а не открывает документ.
        if (source.FindAncestorOfType<TextBox>(includeSelf: true) is not null)
        {
            return;
        }

        if (source.FindAncestorOfType<TreeViewItem>(includeSelf: true)?.DataContext
            is FileTreeNodeViewModel node)
        {
            ActivateFromPointer(e.InitialPressMouseButton, node);
        }
    }

    /// <summary>
    /// Левый клик по строке открывает документ; правый только выделяет её и показывает меню.
    /// Отдельный метод, потому что в headless строки дерева не материализуются
    /// и до события мыши тест дотянуться не может.
    /// </summary>
    internal void ActivateFromPointer(MouseButton button, FileTreeNodeViewModel node)
    {
        if (button == MouseButton.Left && DataContext is ShellViewModel { Workspace: { } workspace })
        {
            workspace.OpenNodeCommand.Execute(node);
        }
    }

    /// <summary>
    /// Клавиатура дерева: Enter открывает строку, F2 переименовывает, Delete удаляет,
    /// ⇧F10 и клавиша «меню» открывают контекстное меню — это у попапа было даром.
    /// Подписи клавиш в пунктах меню — только подписи, обработчик за ними здесь.
    /// </summary>
    private void OnTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ShellViewModel { Workspace: { } workspace }
            || workspace.SelectedNode is not { } node)
        {
            return;
        }

        // Пока идёт ввод имени, клавиши принадлежат полю: иначе Delete из строки ввода
        // ушёл бы в удаление файла.
        if (workspace.IsEditingName)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                workspace.OpenNodeCommand.Execute(node);
                e.Handled = true;
                break;

            case Key.F2:
                workspace.StartRenameCommand.Execute(node);
                e.Handled = true;
                break;

            case Key.Delete:
                workspace.RequestDeleteCommand.Execute(node);
                e.Handled = true;
                break;

            case Key.Apps:
            case Key.F10 when e.KeyModifiers == KeyModifiers.Shift:
                OpenContextMenuForSelectedRow(node);
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Меню с клавиатуры встаёт под выделенной строкой, а не у курсора: мышь может быть
    /// где угодно. Строки дерева в headless не материализуются, поэтому якорь ставится
    /// по строке, только если она есть.
    /// </summary>
    private void OpenContextMenuForSelectedRow(FileTreeNodeViewModel node)
    {
        if (DataContext is not ShellViewModel viewModel)
        {
            return;
        }

        var row = FileTree.GetVisualDescendants()
            .OfType<TreeViewItem>()
            .FirstOrDefault(item => ReferenceEquals(item.DataContext, node));
        if (row is not null && row.TranslatePoint(default, (Visual)this) is { } origin)
        {
            SetMenuAnchor(new Rect(origin, new Size(row.Bounds.Width, RowAnchorHeight)));
        }

        viewModel.OpenTreeContextMenuCommand.Execute(node);

        // Мышь оставляет фокус там, где он был, а с клавиатуры меню иначе недосягаемо.
        MenuHost()?.FocusSidebarMenu();
    }

    /// <summary>
    /// Поле ввода имени появляется вместе со строкой, поэтому фокус ставится здесь.
    /// При переименовании выделено имя без расширения, у нового файла курсор перед `.md`
    /// (макет 09).
    /// </summary>
    private void OnEditNameAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not TextBox editor || DataContext is not ShellViewModel { Workspace: { } workspace })
        {
            return;
        }

        editor.AddHandler(
            KeyDownEvent,
            OnEditNameKeyDown,
            RoutingStrategies.Bubble,
            handledEventsToo: true);

        Dispatcher.UIThread.Post(
            () =>
            {
                editor.Focus();

                var name = editor.Text ?? string.Empty;
                if (workspace.EditKind != TreeEditKind.Rename)
                {
                    editor.CaretIndex = 0;
                    return;
                }

                var extension = Path.GetExtension(name).Length;
                editor.SelectionStart = 0;
                editor.SelectionEnd = name.Length - extension;
            },
            DispatcherPriority.Input);
    }

    /// <summary>Esc сбрасывает поиск, не выходя из поля: это самый частый способ вернуться к дереву.</summary>
    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape
            || DataContext is not ShellViewModel { Workspace: { } workspace }
            || !workspace.HasSearchQuery)
        {
            return;
        }

        workspace.ClearSearchCommand.Execute(null);
        e.Handled = true;
    }

    /// <summary>Enter создаёт или переименовывает, Esc отменяет — как в любом инлайн-редакторе.</summary>
    private void OnEditNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ShellViewModel { Workspace: { } workspace })
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                workspace.CommitEditCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Escape:
                workspace.CancelEditCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Потеря фокуса отменяет ввод — но не тогда, когда поле уже показывает ошибку:
    /// иначе сообщение исчезало бы вместе с введённым именем.
    /// </summary>
    private void OnEditNameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel { Workspace: { HasEditError: false } workspace })
        {
            workspace.CancelEditCommand.Execute(null);
        }
    }
}
