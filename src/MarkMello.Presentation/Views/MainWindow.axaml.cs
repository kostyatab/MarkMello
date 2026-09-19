using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using MarkMello.Application.Abstractions;
using MarkMello.Domain;
using MarkMello.Domain.Workspace;
using MarkMello.Presentation.ViewModels;

namespace MarkMello.Presentation.Views;

public partial class MainWindow : Window
{
    private const double DefaultWindowWidth = 1280;
    private const double DefaultWindowHeight = 840;
    private const double WindowRowHeight = 44;
    // На macOS строка совпадает с системным тулбаром unified, и светофор стоит по её центру.
    private const double MacOsWindowRowHeight = 52;
    // Подложки кнопок и вкладок в 11 от краёв строки, иконки в 19 — на вертикали светофора.
    private const double WindowRowLeadingInset = 11;
    private const double WindowRowTrailingInset = 11;
    // Светофор macOS в строке окна: 19 до первой кнопки, 60 на три кнопки, 12 до содержимого.
    private const double MacOsTrafficLightsInset = 91;
    // Кнопки строки (mm-row-button) высотой 30 стоят по центру строки.
    private const double WindowRowButtonHeight = 30;
    // Карточка открывается в 6 от низа своей кнопки, как поповеры macOS.
    private const double OverlayCardGap = 6;
    private const double OverlayCardTrailingInset = WindowRowTrailingInset;
    private const int WindowPlacementMarginPixels = 8;

    /// <summary>Класс области, за пустое место которой тянется окно: строка окна и шапка сайдбара.</summary>
    internal const string WindowDragClass = "mm-window-drag";

    private readonly ShellViewModel _viewModel = default!;
    private double _windowButtonsWidth;
    private readonly StartupSmokeTestOptions _startupSmokeTestOptions = StartupSmokeTestOptions.Disabled;
    private readonly IStartupMetrics? _startupMetrics;
    private readonly ISettingsStore? _settings;
    private readonly Task _startupInitializationTask = Task.CompletedTask;
    private Win32Properties.CustomWndProcHookCallback? _windowsWndProcHookCallback;
    private WindowsMonitorArea? _windowsMaximizeMonitorArea;
    private WindowPlacement? _lastNormalWindowPlacement;
    private Border? _windowBorder;
    private bool _isWindowsManualMaximized;
    private bool _isConvertingWindowsNativeMaximize;
    private bool _pendingWindowsStartupMaximize;
    private bool _allowConfirmedClose;
    private IFindHost? _findHost;

    public MainWindow()
    {
        InitializeComponent();
        ApplyWindowRowLayout();
    }

    public MainWindow(
        ShellViewModel viewModel,
        StartupSmokeTestOptions startupSmokeTestOptions,
        ISettingsStore settings,
        IStartupMetrics startupMetrics)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(startupSmokeTestOptions);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(startupMetrics);

        _viewModel = viewModel;
        _startupSmokeTestOptions = startupSmokeTestOptions;
        _settings = settings;
        _startupMetrics = startupMetrics;
        DataContext = viewModel;

        ConfigurePlatformChrome();
        InitializeComponent();
        ApplyWindowRowLayout();
        ApplyStartupWindowPlacement();
        SyncSidebarColumn();
        SyncOverlayWindowClasses();
        UpdateTitleBarMaximizeVisuals();
        UpdateWindowBorder();

        AddHandler(FindBarView.FindNextRequestedEvent, OnFindBarFindNextRequested);
        AddHandler(FindBarView.FindPreviousRequestedEvent, OnFindBarFindPreviousRequested);
        AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnTitleBarPointerPressed, RoutingStrategies.Bubble);
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);

        Opened += OnWindowOpened;
        Closing += OnWindowClosing;
        SizeChanged += OnWindowSizeChanged;
        PositionChanged += OnWindowPositionChanged;
        PropertyChanged += OnWindowAvaloniaPropertyChanged;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.CloseRequested += OnViewModelCloseRequested;

        _startupInitializationTask = InitializeStartupAsync();
    }

    /// <summary>
    /// Platform chrome rules for Avalonia 12:
    /// - Windows: extended client area + BorderOnly keeps the native resize border
    ///   while the XAML layout draws the window row with its own window buttons.
    /// - macOS: keep native decorations, but extend the client area under our layout.
    ///   BorderOnly/None still have problematic drag behaviour in 12.0.x. An empty
    ///   unified toolbar puts the traffic lights where system windows have them.
    /// - Linux: keep native chrome because window manager behaviour varies widely.
    /// </summary>
    private void ConfigurePlatformChrome()
    {
        if (OperatingSystem.IsWindows())
        {
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaTitleBarHeightHint = WindowRowHeight;
            WindowDecorations = global::Avalonia.Controls.WindowDecorations.BorderOnly;
            _windowsWndProcHookCallback = OnWindowsWndProc;
            Win32Properties.AddWndProcHookCallback(this, _windowsWndProcHookCallback);
        }
        else if (OperatingSystem.IsMacOS())
        {
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaTitleBarHeightHint = MacOsWindowRowHeight;
            WindowDecorations = global::Avalonia.Controls.WindowDecorations.Full;
            MacOSWindowToolbar.Attach(this);
        }
        // Linux: let the window manager draw its native chrome.
    }

    /// <summary>
    /// Высота строки окна и шапки сайдбара (ADR-0009 Rule 1). На macOS это высота
    /// системного тулбара unified: светофор стоит по её центру, как у Finder и Почты.
    /// </summary>
    internal static double CalculateWindowRowHeight(bool isMacOS)
        => isMacOS ? MacOsWindowRowHeight : WindowRowHeight;

    private void ApplyWindowRowLayout()
    {
        if (this.FindControl<Border>("WindowRow") is { } row)
        {
            row.Height = CalculateWindowRowHeight(OperatingSystem.IsMacOS());
        }

        ApplyOverlayCardInset();

        if (this.FindControl<Grid>("WindowRowContent") is { } rowContent)
        {
            rowContent.Margin = CalculateWindowRowPadding(
                OperatingSystem.IsMacOS(),
                OperatingSystem.IsWindows(),
                (DataContext as ShellViewModel)?.ShowsSidebar == true);
        }
    }

    /// <summary>
    /// Отступы строки окна (ADR-0009 Rule 1). Светофор macOS стоит в шапке открытого
    /// сайдбара, а без неё — в самой строке слева, и строка оставляет ему место.
    /// Кнопки окна Windows прижаты к правому краю, на macOS и Linux справа поле.
    /// </summary>
    internal static Thickness CalculateWindowRowPadding(bool isMacOS, bool isWindows, bool showsSidebar)
        => new(
            isMacOS && !showsSidebar ? MacOsTrafficLightsInset : WindowRowLeadingInset,
            0,
            isWindows ? 0 : WindowRowTrailingInset,
            0);

    /// <summary>
    /// Карточки строки — поиск, Aa, меню ⋯ и настройки — раскрываются под своей кнопкой
    /// у правого края (ADR-0009 Rule 2). На Windows правее кнопок строки стоят ещё
    /// кнопки окна, поэтому карточки отступают на их ширину: она приходит из самой
    /// разметки, чтобы отступ не разъезжался с размерами кнопок.
    /// </summary>
    private void OnWindowButtonsSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        _windowButtonsWidth = e.NewSize.Width;
        ApplyOverlayCardInset();
    }

    private void ApplyOverlayCardInset()
    {
        if (this.FindControl<Panel>("OverlayCardHost") is { } host)
        {
            host.Margin = CalculateOverlayCardMargin(OperatingSystem.IsMacOS(), _windowButtonsWidth);
        }
    }

    /// <summary>
    /// Хост карточек лежит под строкой окна, а карточка должна встать в <see cref="OverlayCardGap"/>
    /// от низа кнопки — поэтому он поднимается в строку на поле под кнопкой.
    /// </summary>
    internal static Thickness CalculateOverlayCardMargin(bool isMacOS, double windowButtonsWidth)
        => new(
            0,
            OverlayCardGap - (CalculateWindowRowHeight(isMacOS) - WindowRowButtonHeight) / 2,
            OverlayCardTrailingInset + Math.Max(0, windowButtonsWidth),
            0);

    private async void OnWindowOpened(object? sender, EventArgs e)
    {
        ApplyPendingWindowsStartupMaximize();
        await _startupInitializationTask.ConfigureAwait(true);
        await CompleteStartupSmokeTestAsync().ConfigureAwait(true);
    }

    private async Task InitializeStartupAsync()
    {
        try
        {
            await _viewModel.InitializeAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (_startupSmokeTestOptions.IsEnabled)
        {
            Console.Error.WriteLine(exception);
            ShutdownClassicDesktopLifetime(exitCode: 1);
        }
        catch
        {
            // Keep the fast path resilient: VM initialization should not crash the window.
            // Real logging belongs with the infrastructure logging work in M4+.
        }
    }

    private async Task CompleteStartupSmokeTestAsync()
    {
        if (!_startupSmokeTestOptions.IsEnabled)
        {
            return;
        }

        await MeasureFolderModeAsync().ConfigureAwait(true);
        await Task.Delay(_startupSmokeTestOptions.ExitAfterOpenDelay).ConfigureAwait(true);
        WriteStartupTimings();
        ShutdownClassicDesktopLifetime(exitCode: 0);
    }

    /// <summary>
    /// Замер folder mode: открытие папки и раскрытие первого каталога.
    /// Открывать папку иначе, чем руками через picker, нельзя, поэтому измерение
    /// живёт в том же smoke-режиме, что и тайминги старта, и никогда не включается
    /// в обычном запуске.
    /// </summary>
    private async Task MeasureFolderModeAsync()
    {
        if (_startupSmokeTestOptions.OpenFolderPath is not { } folderPath)
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        await _viewModel.OpenFolderPathAsync(folderPath).ConfigureAwait(true);
        Console.WriteLine($"[workspace] {"OpenFolder",-20} {stopwatch.Elapsed.TotalMilliseconds,8:F1} ms");

        if (_viewModel.Workspace is not { } workspace)
        {
            return;
        }

        var firstDirectory = workspace.Roots.FirstOrDefault(static node => node.IsDirectory);
        if (firstDirectory is null)
        {
            return;
        }

        stopwatch.Restart();
        await workspace.ExpandNodeAsync(firstDirectory).ConfigureAwait(true);
        Console.WriteLine($"[workspace] {"ExpandNode",-20} {stopwatch.Elapsed.TotalMilliseconds,8:F1} ms");
    }

    /// <summary>
    /// Печатает снимок startup-таймингов в stdout. Вызывается только в smoke-режиме,
    /// поэтому Release-сборку можно измерять теми же командами, что и CI-прогон.
    /// </summary>
    private void WriteStartupTimings()
    {
        if (_startupMetrics is null)
        {
            return;
        }

        foreach (var timing in _startupMetrics.Snapshot().StageTimings.OrderBy(static pair => pair.Key))
        {
            Console.WriteLine($"[startup] {timing.Key,-20} {timing.Value.TotalMilliseconds,8:F1} ms");
        }
    }

    internal static bool IsOverlayPopupInteractionSource(Visual source)
    {
        for (var current = source; current is not null; current = current.GetVisualParent())
        {
            if (current is ComboBox or ComboBoxItem)
            {
                return true;
            }

            if (string.Equals(current.GetType().Name, "PopupRoot", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // ---------- Find card (Ctrl+F) ----------

    private IFindHost? ResolveFindHost()
    {
        if (_findHost is { } cachedHost
            && cachedHost is Visual cachedVisual
            && cachedVisual.IsAttachedToVisualTree())
        {
            return cachedHost;
        }

        DetachFindHost();

        var bodyPanel = this.FindControl<Panel>("BodyPanel");
        if (bodyPanel is null)
        {
            return null;
        }

        _findHost = bodyPanel
            .GetVisualDescendants()
            .OfType<IFindHost>()
            .FirstOrDefault();
        if (_findHost is not null)
        {
            _findHost.FindStateChanged += OnFindHostStateChanged;
        }

        return _findHost;
    }

    private void DetachFindHost()
    {
        if (_findHost is not null)
        {
            _findHost.FindStateChanged -= OnFindHostStateChanged;
            _findHost = null;
        }
    }

    private void InvalidateFindHost() => DetachFindHost();

    private void SyncFindCountersFromHost()
    {
        var host = ResolveFindHost();
        _viewModel.FindMatchIndex = host?.MatchIndex ?? -1;
        _viewModel.FindMatchCount = host?.MatchCount ?? 0;
    }

    private void OnFindBarFindNextRequested(object? sender, RoutedEventArgs e)
    {
        ResolveFindHost()?.FindNext();
        SyncFindCountersFromHost();
        e.Handled = true;
    }

    private void OnFindBarFindPreviousRequested(object? sender, RoutedEventArgs e)
    {
        ResolveFindHost()?.FindPrevious();
        SyncFindCountersFromHost();
        e.Handled = true;
    }

    private void OnFindHostStateChanged(object? sender, EventArgs e)
        => SyncFindCountersFromHost();

    private static void ShutdownClassicDesktopLifetime(int exitCode)
    {
        if (global::Avalonia.Application.Current?.ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown(exitCode);
            return;
        }

        Environment.ExitCode = exitCode;
    }

    protected override void OnClosed(EventArgs e)
    {
        DetachFindHost();

        if (_windowsWndProcHookCallback is not null)
        {
            Win32Properties.RemoveWndProcHookCallback(this, _windowsWndProcHookCallback);
            _windowsWndProcHookCallback = null;
        }

        Closing -= OnWindowClosing;
        SizeChanged -= OnWindowSizeChanged;
        PositionChanged -= OnWindowPositionChanged;
        PropertyChanged -= OnWindowAvaloniaPropertyChanged;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.CloseRequested -= OnViewModelCloseRequested;
        base.OnClosed(e);
    }

    // ---------- Window control buttons (Windows only path) ----------

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object? sender, RoutedEventArgs e)
        => ToggleWindowMaximize();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Пустое место строки окна и шапки сайдбара тянет окно, на Windows двойной клик
    /// разворачивает его. Кнопки, вкладки и поля забирают нажатие себе, поэтому
    /// обработчик на всплытии видит только нажатия по фону.
    /// </summary>
    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var isWindows = OperatingSystem.IsWindows();
        var isMacOS = OperatingSystem.IsMacOS();
        if (!CanUseDraggableTitleBar(isWindows, isMacOS)
            || e.Source is not Visual source
            || !IsWindowDragSource(source))
        {
            return;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (isWindows && e.ClickCount == 2)
        {
            if (CanResize)
            {
                ToggleWindowMaximize();
            }

            e.Handled = true;
            return;
        }

        if (e.ClickCount != 1)
        {
            return;
        }

        try
        {
            if (isWindows)
            {
                CaptureWindowsNativeMaximizeMonitorArea();
                TryCaptureWindowsSnappedMaximize();
                if ((_isWindowsManualMaximized || WindowState == WindowState.Maximized)
                    && RestoreWindowsManualMaximizeForDrag())
                {
                    BeginMoveDrag(e);
                    e.Handled = true;
                    return;
                }
            }

            BeginMoveDrag(e);
            e.Handled = true;
        }
        catch
        {
            // Unsupported platforms or transient states simply do not start a drag.
        }
    }

    internal static bool CanUseDraggableTitleBar(bool isWindows, bool isMacOS)
        => isWindows || isMacOS;

    internal static bool IsWindowDragSource(Visual source)
    {
        for (Visual? current = source; current is not null; current = current.GetVisualParent())
        {
            if (current.Classes.Contains(WindowDragClass))
            {
                return true;
            }
        }

        return false;
    }

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Клик по скриму модального диалога не закрывает карточки под ним.
        if (_viewModel.IsModalDialogOpen)
        {
            return;
        }

        if (!_viewModel.HasOpenOverlay || e.Source is not Visual source)
        {
            return;
        }

        if (IsPointerWithinOpenOverlay(source) || IsOverlayPopupInteractionSource(source))
        {
            return;
        }

        _viewModel.CloseOverlayCommand.Execute(null);
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (!HasSettingsShortcutModifier(e.KeyModifiers))
        {
            return;
        }

        if (e.PhysicalKey != PhysicalKey.Comma
            && e.Key != Key.OemComma
            && !string.Equals(e.KeySymbol, ",", StringComparison.Ordinal))
        {
            return;
        }

        _viewModel.ToggleAppSettingsCommand.Execute(null);
        e.Handled = true;
    }

    // ---------- Drag & drop ----------

    private void OnDragEnter(object? sender, DragEventArgs e) => UpdateDropTarget(e);

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        UpdateDropTarget(e);
        e.Handled = true;
    }

    /// <summary>
    /// Слой на всё окно говорит, что случится при отпускании (A-Drop). Бывает, что список
    /// файлов платформа отдаёт только при отпускании: тогда слой есть, а второй строки нет.
    /// </summary>
    private void UpdateDropTarget(DragEventArgs e)
    {
        if (TryGetDroppedTarget(e) is { } target)
        {
            _viewModel.ShowDropTarget(target.Path, target.IsDirectory);
            e.DragEffects = DragDropEffects.Copy;
            return;
        }

        if (e.DataTransfer.TryGetFiles() is null && e.DataTransfer.Contains(DataFormat.File))
        {
            _viewModel.ShowDropTarget(null, isDirectory: false);
            e.DragEffects = DragDropEffects.Copy;
            return;
        }

        _viewModel.HideDropTarget();
        e.DragEffects = DragDropEffects.None;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        _viewModel.HideDropTarget();
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        _viewModel.HideDropTarget();

        var target = TryGetDroppedTarget(e);
        if (target is not { } dropped)
        {
            return;
        }

        try
        {
            // Каталог открывает workspace, файл — документ. Разделение здесь,
            // чтобы обе точки входа шли теми же путями, что picker и меню.
            await (dropped.IsDirectory
                ? _viewModel.OpenFolderPathAsync(dropped.Path)
                : _viewModel.OpenDroppedFileAsync(dropped.Path));
        }
        catch
        {
            // The VM converts failures into the LoadError state.
        }
    }

    private async void OnSidebarSplitterDragCompleted(object? sender, VectorEventArgs e)
    {
        try
        {
            if (SidebarLayout is { ColumnDefinitions: { Count: > 0 } columns })
            {
                _viewModel.SidebarWidth = WorkspaceSidebarWidth.Normalize(columns[0].Width.Value);
            }

            await _viewModel.PersistSidebarWidthAsync();
        }
        catch
        {
            // Не сохранили ширину — не повод ронять окно.
        }
    }

    private readonly record struct DroppedTarget(string Path, bool IsDirectory);

    private static DroppedTarget? TryGetDroppedTarget(DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        if (files is null)
        {
            return null;
        }

        foreach (var item in files)
        {
            var path = item.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            switch (item)
            {
                case IStorageFolder:
                    return new DroppedTarget(path, IsDirectory: true);

                case IStorageFile when SupportedDocumentTypes.IsSupportedPath(path):
                    return new DroppedTarget(path, IsDirectory: false);
            }
        }

        return null;
    }

    /// <summary>
    /// Ширина сайдбара живёт на колонке: GridSplitter двигает колонку, а не контент,
    /// поэтому фиксированная ширина у самого сайдбара оставляла рядом пустую полосу,
    /// а перетаскивание не доходило до view-model. Скрытый сайдбар схлопывает колонку
    /// в ноль — минимум из макета в этот момент не действует.
    /// </summary>
    private void SyncSidebarColumn()
    {
        if (SidebarLayout is not { ColumnDefinitions: { Count: > 0 } columns })
        {
            return;
        }

        var (minWidth, width) = CalculateSidebarColumn(_viewModel.ShowsSidebar, _viewModel.SidebarWidth);
        columns[0].MinWidth = minWidth;
        columns[0].Width = width;
    }

    /// <summary>
    /// Скрытый сайдбар схлопывает колонку в ноль вместе с минимумом: иначе от него
    /// осталась бы пустая полоса шириной 220.
    /// </summary>
    internal static (double MinWidth, GridLength Width) CalculateSidebarColumn(bool showsSidebar, double sidebarWidth)
        => showsSidebar
            ? (WorkspaceSidebarWidth.Minimum, new GridLength(WorkspaceSidebarWidth.Normalize(sidebarWidth)))
            : (0d, new GridLength(0));

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.ShowsSidebar)
            or nameof(ShellViewModel.SidebarWidth))
        {
            SyncSidebarColumn();
            ApplyWindowRowLayout();
            return;
        }

        if (e.PropertyName is nameof(ShellViewModel.ShellOverlay)
            or nameof(ShellViewModel.IsSettingsOpen)
            or nameof(ShellViewModel.IsAppMenuOpen)
            or nameof(ShellViewModel.HasOpenOverlay))
        {
            SyncOverlayWindowClasses();
            return;
        }

        if (e.PropertyName == nameof(ShellViewModel.ReadingProgress)
            || e.PropertyName == nameof(ShellViewModel.IsViewer))
        {
            UpdateReadingProgressBarWidth();
            return;
        }

        if (e.PropertyName == nameof(ShellViewModel.IsEditMode))
        {
            InvalidateFindHost();
            UpdateReadingProgressBarWidth();
            return;
        }

        if (e.PropertyName == nameof(ShellViewModel.State))
        {
            InvalidateFindHost();
            return;
        }

        if (e.PropertyName == nameof(ShellViewModel.IsFindBarOpen))
        {
            if (_viewModel.IsFindBarOpen)
            {
                ResolveFindHost()?.ApplyQuery(_viewModel.FindQuery);
            }
            else
            {
                ResolveFindHost()?.ClearFind();
            }

            SyncFindCountersFromHost();
            return;
        }

        if (e.PropertyName == nameof(ShellViewModel.FindQuery))
        {
            if (_viewModel.IsFindBarOpen)
            {
                ResolveFindHost()?.ApplyQuery(_viewModel.FindQuery);
                SyncFindCountersFromHost();
            }

            return;
        }

        if (e.PropertyName is nameof(ShellViewModel.TitleBarMaximize)
            or nameof(ShellViewModel.TitleBarRestore))
        {
            UpdateTitleBarMaximizeVisuals();
        }

        if (e.PropertyName == nameof(ShellViewModel.WindowBorderMode))
        {
            UpdateWindowBorder();
        }
    }

    private void UpdateWindowBorder()
    {
        // GetControl throws when the name is missing, which is an authoring bug
        // in our own XAML rather than a runtime condition — better loud at
        // startup than a window that silently never gets its outline.
        var border = _windowBorder ??= this.GetControl<Border>("WindowBorder");

        border.BorderThickness = new Thickness(
            ShouldDrawWindowBorder(
                _viewModel.WindowBorderMode,
                OperatingSystem.IsWindows(),
                _isWindowsManualMaximized || WindowState == WindowState.Maximized)
                ? 1
                : 0);
    }

    /// <summary>
    /// Whether the app draws its own window outline.
    ///
    /// Auto draws it only where MarkMello replaces the system chrome with its
    /// own — that is Windows, where a light window on a light background is
    /// otherwise indistinguishable from the one behind it. macOS and Linux keep
    /// native decorations and already have an edge.
    ///
    /// A maximized window never gets one: its edges sit against the screen
    /// bounds, so the outline would only eat a row of pixels.
    /// </summary>
    internal static bool ShouldDrawWindowBorder(WindowBorderMode mode, bool isWindows, bool isMaximized)
    {
        if (isMaximized)
        {
            return false;
        }

        return mode switch
        {
            WindowBorderMode.On => true,
            WindowBorderMode.Off => false,
            _ => isWindows
        };
    }

    private static bool IsWithinVisual(Visual source, Visual target)
    {
        for (Visual? current = source; current is not null; current = current.GetVisualParent())
        {
            if (ReferenceEquals(current, target))
            {
                return true;
            }
        }

        return false;
    }

    private void UpdateTitleBarMaximizeVisuals()
    {
        var isRestoreState = _isWindowsManualMaximized || WindowState == WindowState.Maximized;

        if (this.FindControl<Control>("TitleBarMaximizeIcon") is { } maximizeIcon)
        {
            maximizeIcon.IsVisible = !isRestoreState;
        }

        if (this.FindControl<Control>("TitleBarRestoreIcon") is { } restoreIcon)
        {
            restoreIcon.IsVisible = isRestoreState;
        }

        if (this.FindControl<Button>("TitleBarMaximizeButton") is { } button)
        {
            ToolTip.SetTip(
                button,
                isRestoreState
                    ? _viewModel.TitleBarRestore
                    : _viewModel.TitleBarMaximize);
        }
    }

    // Windows custom chrome has two maximize paths. The title-bar button uses
    // SetWindowPos directly so the window stays on the monitor it already
    // occupies. WM_GETMINMAXINFO remains for native maximize requests such as
    // double-click and Aero Snap, where Windows asks for monitor-relative bounds.
    private IntPtr OnWindowsWndProc(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (msg != WindowsMessages.WmGetMinMaxInfo || lParam == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        if (TryApplyWindowsMonitorMaximizeBounds(hWnd, lParam))
        {
            handled = true;
        }

        return IntPtr.Zero;
    }

    private bool TryApplyWindowsMonitorMaximizeBounds(IntPtr hWnd, IntPtr minMaxInfoPointer)
    {
        var monitorArea = _windowsMaximizeMonitorArea
            ?? TryCreateWindowsMonitorAreaFromWindowBounds(hWnd)
            ?? TryCreateWindowsMonitorAreaFromHandle(hWnd)
            ?? TryCreateWindowsMonitorAreaFromCursor();
        if (monitorArea is null || !monitorArea.Value.IsValid)
        {
            return false;
        }

        var maximizeBounds = CalculateWindowsMonitorMaximizeBounds(
            monitorArea.Value.MonitorBounds,
            monitorArea.Value.WorkingArea,
            monitorArea.Value.Scaling,
            MinWidth,
            MinHeight);

        var minMaxInfo = Marshal.PtrToStructure<MINMAXINFO>(minMaxInfoPointer);
        minMaxInfo.ptMaxPosition.X = maximizeBounds.MaxPositionX;
        minMaxInfo.ptMaxPosition.Y = maximizeBounds.MaxPositionY;
        minMaxInfo.ptMaxSize.X = maximizeBounds.MaxSizeWidth;
        minMaxInfo.ptMaxSize.Y = maximizeBounds.MaxSizeHeight;
        minMaxInfo.ptMinTrackSize.X = Math.Max(minMaxInfo.ptMinTrackSize.X, maximizeBounds.MinTrackWidth);
        minMaxInfo.ptMinTrackSize.Y = Math.Max(minMaxInfo.ptMinTrackSize.Y, maximizeBounds.MinTrackHeight);
        Marshal.StructureToPtr(minMaxInfo, minMaxInfoPointer, fDeleteOld: false);
        return true;
    }

    private void ToggleWindowMaximize()
    {
        if (_isWindowsManualMaximized)
        {
            RestoreWindowsManualMaximize();
            return;
        }

        if (WindowState == WindowState.Maximized)
        {
            RestoreWindowsNormalChrome(TryGetWindowsHandle());
            WindowState = WindowState.Normal;
            return;
        }

        CaptureLastNormalWindowPlacement();
        if (TryMaximizeWindowToCurrentWindowsMonitor())
        {
            return;
        }

        WindowState = WindowState.Maximized;
    }

    private bool TryMaximizeWindowToCurrentWindowsMonitor()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var handle = TryGetWindowsHandle();
        var monitorArea = TryCreateWindowsMonitorAreaFromWindowBounds(handle)
            ?? TryCreateWindowsMonitorAreaFromHandle(handle);
        if (monitorArea is null || !monitorArea.Value.IsValid)
        {
            return false;
        }

        return TryApplyWindowsManualMaximize(monitorArea.Value);
    }

    private bool TryApplyWindowsManualMaximize(WindowsMonitorArea monitorArea)
    {
        var handle = TryGetWindowsHandle();
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        _windowsMaximizeMonitorArea = monitorArea;
        _isWindowsManualMaximized = true;
        var targetBounds = monitorArea.WorkingArea;
        ApplyWindowsManualMaximizeChrome(handle);
        if (!SetWindowPos(
                handle,
                IntPtr.Zero,
                targetBounds.X,
                targetBounds.Y,
                targetBounds.Width,
                targetBounds.Height,
                WindowsMessages.SwpNoZOrder | WindowsMessages.SwpNoOwnerZOrder | WindowsMessages.SwpFrameChanged))
        {
            _windowsMaximizeMonitorArea = null;
            _isWindowsManualMaximized = false;
            RestoreWindowsNormalChrome(handle);
            UpdateTitleBarMaximizeVisuals();
            return false;
        }

        UpdateTitleBarMaximizeVisuals();
        UpdateReadingProgressBarWidth();
        return true;
    }

    private void RestoreWindowsManualMaximize()
    {
        var placement = _lastNormalWindowPlacement;
        if (placement is null)
        {
            _isWindowsManualMaximized = false;
            _windowsMaximizeMonitorArea = null;
            RestoreWindowsNormalChrome(TryGetWindowsHandle());
            UpdateTitleBarMaximizeVisuals();
            return;
        }

        var normalizedPlacement = placement with { IsMaximized = false };
        RestoreWindowsNormalChrome(TryGetWindowsHandle());
        WindowState = WindowState.Normal;
        Width = Math.Max(MinWidth, normalizedPlacement.Width);
        Height = Math.Max(MinHeight, normalizedPlacement.Height);
        Position = new PixelPoint(
            (int)Math.Round(normalizedPlacement.X),
            (int)Math.Round(normalizedPlacement.Y));
        _isWindowsManualMaximized = false;
        _windowsMaximizeMonitorArea = null;
        UpdateTitleBarMaximizeVisuals();
        UpdateReadingProgressBarWidth();
    }

    // Manual maximize leaves the OS window in the Normal state. When the user
    // drags the title bar, emulate the native "restore under cursor" gesture.
    private bool RestoreWindowsManualMaximizeForDrag()
    {
        var placement = _lastNormalWindowPlacement;
        var handle = TryGetWindowsHandle();
        if (placement is null || handle == IntPtr.Zero || !GetCursorPos(out var cursorPoint))
        {
            RestoreWindowsManualMaximize();
            return false;
        }

        var frameBounds = GetVisibleWindowsFrameBounds(handle);
        var scaling = _windowsMaximizeMonitorArea?.Scaling ?? GetValidScaling(RenderScaling);
        var restoreBounds = CalculateWindowsDragRestoreBounds(
            placement,
            frameBounds,
            cursorPoint,
            scaling,
            MinWidth,
            MinHeight);

        _isWindowsManualMaximized = true;
        var wasNativeMaximized = WindowState == WindowState.Maximized;
        if (wasNativeMaximized)
        {
            try
            {
                _isConvertingWindowsNativeMaximize = true;
                WindowState = WindowState.Normal;
            }
            finally
            {
                _isConvertingWindowsNativeMaximize = false;
            }
        }

        RestoreWindowsNormalChrome(handle);
        Width = restoreBounds.LogicalWidth;
        Height = restoreBounds.LogicalHeight;

        if (!SetWindowPos(
                handle,
                IntPtr.Zero,
                restoreBounds.X,
                restoreBounds.Y,
                restoreBounds.Width,
                restoreBounds.Height,
                WindowsMessages.SwpNoZOrder | WindowsMessages.SwpNoOwnerZOrder | WindowsMessages.SwpFrameChanged))
        {
            RestoreWindowsManualMaximize();
            return false;
        }

        _isWindowsManualMaximized = false;
        _windowsMaximizeMonitorArea = null;
        Position = new PixelPoint(restoreBounds.X, restoreBounds.Y);
        UpdateTitleBarMaximizeVisuals();
        UpdateReadingProgressBarWidth();
        return true;
    }

    private void ApplyPendingWindowsStartupMaximize()
    {
        if (!_pendingWindowsStartupMaximize)
        {
            return;
        }

        _pendingWindowsStartupMaximize = false;
        var handle = TryGetWindowsHandle();
        var monitorArea = _windowsMaximizeMonitorArea
            ?? TryCreateWindowsMonitorAreaFromWindowBounds(handle)
            ?? TryCreateWindowsMonitorAreaFromHandle(handle);

        if (monitorArea is not null)
        {
            TryApplyWindowsManualMaximize(monitorArea.Value);
        }
    }

    private void CaptureWindowsNativeMaximizeMonitorArea()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var handle = TryGetWindowsHandle();
        _windowsMaximizeMonitorArea = TryCreateWindowsMonitorAreaFromWindowBounds(handle)
            ?? TryCreateWindowsMonitorAreaFromCursor()
            ?? TryCreateWindowsMonitorAreaFromHandle(handle);
    }

    private void OnWindowAvaloniaPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WindowStateProperty)
        {
            UpdateTitleBarMaximizeVisuals();
            UpdateWindowBorder();
        }

        if (e.Property != WindowStateProperty
            || !OperatingSystem.IsWindows()
            || _isWindowsManualMaximized
            || _isConvertingWindowsNativeMaximize
            || WindowState != WindowState.Maximized)
        {
            return;
        }

        var handle = TryGetWindowsHandle();
        var monitorArea = _windowsMaximizeMonitorArea
            ?? TryCreateWindowsMonitorAreaFromWindowBounds(handle)
            ?? TryCreateWindowsMonitorAreaFromHandle(handle)
            ?? TryCreateWindowsMonitorAreaFromCursor();
        if (monitorArea is null || !monitorArea.Value.IsValid)
        {
            return;
        }

        try
        {
            _isConvertingWindowsNativeMaximize = true;
            _isWindowsManualMaximized = true;
            WindowState = WindowState.Normal;
            if (!TryApplyWindowsManualMaximize(monitorArea.Value))
            {
                _isWindowsManualMaximized = false;
            }
        }
        finally
        {
            _isConvertingWindowsNativeMaximize = false;
        }
    }

    // Aero Snap can resize the visible DWM frame to the monitor work area before
    // Avalonia reports a maximized state. Capture that shape so later title-bar
    // drags restore like a native maximized window.
    private bool TryCaptureWindowsSnappedMaximize()
    {
        if (!OperatingSystem.IsWindows()
            || _isWindowsManualMaximized
            || _isConvertingWindowsNativeMaximize
            || WindowState != WindowState.Normal)
        {
            return false;
        }

        var handle = TryGetWindowsHandle();
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        var frameBounds = GetVisibleWindowsFrameBounds(handle);
        var monitorArea = TryFindWindowsMonitorAreaForWorkingBounds(frameBounds);
        if (monitorArea is null)
        {
            return false;
        }

        return TryApplyWindowsManualMaximize(monitorArea.Value);
    }

    private WindowsMonitorArea? TryCreateWindowsMonitorAreaFromWindowBounds(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero || !GetWindowRect(hWnd, out var windowRect))
        {
            return null;
        }

        var windowBounds = windowRect.ToPixelRect();
        return FindWindowsMonitorAreaForWindowBounds(windowBounds, GetWindowsMonitorAreas());
    }

    private WindowsMonitorArea? TryFindWindowsMonitorAreaForWorkingBounds(PixelRect bounds)
        => FindWindowsMonitorAreaForWorkingBounds(
            bounds,
            GetWindowsMonitorAreas(),
            WindowsMessages.SnapBoundsTolerancePixels);

    private WindowsMonitorArea? TryCreateWindowsMonitorAreaFromCursor()
    {
        if (!GetCursorPos(out var cursorPoint))
        {
            return null;
        }

        return TryCreateWindowsMonitorAreaFromMonitor(
            MonitorFromPoint(cursorPoint, WindowsMessages.MonitorDefaultToNearest));
    }

    private List<WindowsMonitorArea> GetWindowsMonitorAreas()
    {
        var monitorAreas = new List<WindowsMonitorArea>();
        EnumDisplayMonitors(
            IntPtr.Zero,
            IntPtr.Zero,
            (monitor, _, _, _) =>
            {
                var monitorArea = TryCreateWindowsMonitorAreaFromMonitor(monitor);
                if (monitorArea is not null)
                {
                    monitorAreas.Add(monitorArea.Value);
                }

                return true;
            },
            IntPtr.Zero);

        return monitorAreas;
    }

    private WindowsMonitorArea? TryCreateWindowsMonitorAreaFromHandle(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return null;
        }

        var monitor = MonitorFromWindow(hWnd, WindowsMessages.MonitorDefaultToNearest);
        return TryCreateWindowsMonitorAreaFromMonitor(monitor);
    }

    private WindowsMonitorArea? TryCreateWindowsMonitorAreaFromMonitor(IntPtr monitor)
    {
        if (monitor == IntPtr.Zero)
        {
            return null;
        }

        var monitorInfo = new MONITORINFO
        {
            cbSize = Marshal.SizeOf<MONITORINFO>()
        };

        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return null;
        }

        return new WindowsMonitorArea(
            monitorInfo.rcMonitor.ToPixelRect(),
            monitorInfo.rcWork.ToPixelRect(),
            GetValidScaling(RenderScaling));
    }

    internal static long CalculatePixelRectIntersectionArea(PixelRect first, PixelRect second)
    {
        var left = Math.Max(first.X, second.X);
        var top = Math.Max(first.Y, second.Y);
        var right = Math.Min(first.X + first.Width, second.X + second.Width);
        var bottom = Math.Min(first.Y + first.Height, second.Y + second.Height);
        var width = Math.Max(0, right - left);
        var height = Math.Max(0, bottom - top);

        return (long)width * height;
    }

    internal static WindowsMonitorArea? FindWindowsMonitorAreaForWindowBounds(
        PixelRect windowBounds,
        IReadOnlyList<WindowsMonitorArea> monitorAreas)
    {
        var bestMonitorArea = default(WindowsMonitorArea);
        var hasBestMonitorArea = false;
        var bestIntersectionArea = 0L;

        foreach (var monitorArea in monitorAreas)
        {
            if (!monitorArea.IsValid)
            {
                continue;
            }

            var intersectionArea = CalculatePixelRectIntersectionArea(windowBounds, monitorArea.MonitorBounds);
            if (intersectionArea > bestIntersectionArea)
            {
                bestMonitorArea = monitorArea;
                hasBestMonitorArea = true;
                bestIntersectionArea = intersectionArea;
            }
        }

        return hasBestMonitorArea
            ? bestMonitorArea
            : null;
    }

    internal static WindowsMonitorArea? FindWindowsMonitorAreaForWorkingBounds(
        PixelRect bounds,
        IReadOnlyList<WindowsMonitorArea> monitorAreas,
        int tolerancePixels)
    {
        foreach (var monitorArea in monitorAreas)
        {
            if (monitorArea.IsValid
                && IsPixelRectCloseTo(bounds, monitorArea.WorkingArea, tolerancePixels))
            {
                return monitorArea;
            }
        }

        return null;
    }

    internal static bool IsPixelRectCloseTo(PixelRect actual, PixelRect expected, int tolerancePixels)
    {
        var tolerance = Math.Max(0, tolerancePixels);
        return Math.Abs(actual.X - expected.X) <= tolerance
            && Math.Abs(actual.Y - expected.Y) <= tolerance
            && Math.Abs(actual.Width - expected.Width) <= tolerance
            && Math.Abs(actual.Height - expected.Height) <= tolerance;
    }

    internal static WindowsDragRestoreBounds CalculateWindowsDragRestoreBounds(
        WindowPlacement normalPlacement,
        PixelRect maximizedFrameBounds,
        POINT cursorPoint,
        double renderScaling,
        double minWidth,
        double minHeight)
    {
        var scaling = GetValidScaling(renderScaling);
        var logicalWidth = Math.Max(minWidth, normalPlacement.Width);
        var logicalHeight = Math.Max(minHeight, normalPlacement.Height);
        var width = Math.Max(1, (int)Math.Round(logicalWidth * scaling));
        var height = Math.Max(1, (int)Math.Round(logicalHeight * scaling));
        var frameWidth = Math.Max(1, maximizedFrameBounds.Width);
        var horizontalRatio = Math.Clamp(
            (cursorPoint.X - maximizedFrameBounds.X) / (double)frameWidth,
            0,
            1);
        var x = cursorPoint.X - (int)Math.Round(width * horizontalRatio);
        var y = cursorPoint.Y - Math.Min(
            Math.Max(0, cursorPoint.Y - maximizedFrameBounds.Y),
            Math.Max(0, height / 3));

        return new WindowsDragRestoreBounds(x, y, width, height, logicalWidth, logicalHeight);
    }

    private static PixelRect GetVisibleWindowsFrameBounds(IntPtr hWnd)
    {
        var result = DwmGetWindowAttribute(
            hWnd,
            WindowsMessages.DwmwaExtendedFrameBounds,
            out var frameBounds,
            Marshal.SizeOf<RECT>());
        if (result == 0)
        {
            return frameBounds.ToPixelRect();
        }

        return GetWindowRect(hWnd, out var windowRect)
            ? windowRect.ToPixelRect()
            : new PixelRect(0, 0, 1, 1);
    }

    private static void TrySetWindowsCornerPreference(IntPtr hWnd, int cornerPreference)
    {
        if (hWnd == IntPtr.Zero)
        {
            return;
        }

        _ = DwmSetWindowAttribute(
            hWnd,
            WindowsMessages.DwmwaWindowCornerPreference,
            ref cornerPreference,
            sizeof(int));
    }

    // A manually maximized Normal window would otherwise keep Windows 11 rounded
    // corners and a border gap, unlike a real maximized window.
    private void ApplyWindowsManualMaximizeChrome(IntPtr hWnd)
    {
        WindowDecorations = global::Avalonia.Controls.WindowDecorations.None;
        TrySetWindowsCornerPreference(hWnd, WindowsMessages.DwmwcpDoNotRound);
        TrySetWindowsBorderColor(hWnd, WindowsMessages.DwmwaColorNone);
    }

    private void RestoreWindowsNormalChrome(IntPtr hWnd)
    {
        WindowDecorations = global::Avalonia.Controls.WindowDecorations.BorderOnly;
        TrySetWindowsCornerPreference(hWnd, WindowsMessages.DwmwcpDefault);
        TrySetWindowsBorderColor(hWnd, WindowsMessages.DwmwaColorDefault);
    }

    private static void TrySetWindowsBorderColor(IntPtr hWnd, int borderColor)
    {
        if (hWnd == IntPtr.Zero)
        {
            return;
        }

        _ = DwmSetWindowAttribute(
            hWnd,
            WindowsMessages.DwmwaBorderColor,
            ref borderColor,
            sizeof(int));
    }

    private IntPtr TryGetWindowsHandle()
    {
        try
        {
            var handle = TryGetPlatformHandle();
            return handle is not null
                   && string.Equals(handle.HandleDescriptor, "HWND", StringComparison.OrdinalIgnoreCase)
                ? handle.Handle
                : IntPtr.Zero;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    internal static WindowsMonitorMaximizeBounds CalculateWindowsMonitorMaximizeBounds(
        PixelRect monitorBounds,
        PixelRect workingArea,
        double renderScaling,
        double minWidth,
        double minHeight)
    {
        var scaling = renderScaling > 0 && !double.IsNaN(renderScaling) && !double.IsInfinity(renderScaling)
            ? renderScaling
            : 1;

        return new WindowsMonitorMaximizeBounds(
            MaxPositionX: Math.Max(0, workingArea.X - monitorBounds.X),
            MaxPositionY: Math.Max(0, workingArea.Y - monitorBounds.Y),
            MaxSizeWidth: Math.Max(1, workingArea.Width),
            MaxSizeHeight: Math.Max(1, workingArea.Height),
            MinTrackWidth: Math.Max(1, (int)Math.Ceiling(Math.Max(0, minWidth) * scaling)),
            MinTrackHeight: Math.Max(1, (int)Math.Ceiling(Math.Max(0, minHeight) * scaling)));
    }

    private static double GetValidScaling(double scaling)
        => scaling > 0 && !double.IsNaN(scaling) && !double.IsInfinity(scaling)
            ? scaling
            : 1;

    internal readonly record struct WindowsMonitorArea(
        PixelRect MonitorBounds,
        PixelRect WorkingArea,
        double Scaling)
    {
        public bool IsValid
            => MonitorBounds.Width > 0
               && MonitorBounds.Height > 0
               && WorkingArea.Width > 0
               && WorkingArea.Height > 0;
    }

    internal readonly record struct WindowsDragRestoreBounds(
        int X,
        int Y,
        int Width,
        int Height,
        double LogicalWidth,
        double LogicalHeight);

    internal readonly record struct WindowsMonitorMaximizeBounds(
        int MaxPositionX,
        int MaxPositionY,
        int MaxSizeWidth,
        int MaxSizeHeight,
        int MinTrackWidth,
        int MinTrackHeight);

    private static bool HasSettingsShortcutModifier(KeyModifiers modifiers)
        => modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta);

    private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (TryCaptureWindowsSnappedMaximize())
        {
            UpdateReadingProgressBarWidth();
            return;
        }

        CaptureLastNormalWindowPlacement();
        UpdateReadingProgressBarWidth();
    }

    private void OnWindowPositionChanged(object? sender, PixelPointEventArgs e)
    {
        if (TryCaptureWindowsSnappedMaximize())
        {
            return;
        }

        CaptureLastNormalWindowPlacement();
    }

    private void ApplyStartupWindowPlacement()
    {
        var savedPlacement = LoadWindowPlacementBestEffort();
        var screen = TryGetStartupScreen(savedPlacement);

        if (screen is null)
        {
            ApplyFallbackStartupPlacement(savedPlacement);
            return;
        }

        var startupPlacement = CalculateStartupWindowPlacement(
            savedPlacement,
            screen.WorkingArea,
            screen.Scaling,
            MinWidth,
            MinHeight);

        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = startupPlacement.Width;
        Height = startupPlacement.Height;
        Position = new PixelPoint((int)startupPlacement.X, (int)startupPlacement.Y);
        _lastNormalWindowPlacement = startupPlacement with { IsMaximized = false };

        if (savedPlacement?.IsMaximized == true)
        {
            if (OperatingSystem.IsWindows())
            {
                _pendingWindowsStartupMaximize = true;
            }
            else
            {
                WindowState = WindowState.Maximized;
            }
        }
    }

    internal static WindowPlacement CalculateStartupWindowPlacement(
        WindowPlacement? savedPlacement,
        PixelRect workingArea,
        double screenScaling,
        double minWidth,
        double minHeight)
    {
        var normalizedPlacement = WindowPlacement.Normalize(savedPlacement);
        var scaling = screenScaling > 0 && !double.IsNaN(screenScaling) && !double.IsInfinity(screenScaling)
            ? screenScaling
            : 1;

        var maxWidth = Math.Max(minWidth, (workingArea.Width - WindowPlacementMarginPixels * 2) / scaling);
        var maxHeight = Math.Max(minHeight, (workingArea.Height - WindowPlacementMarginPixels * 2) / scaling);

        var width = Math.Clamp(normalizedPlacement?.Width ?? DefaultWindowWidth, minWidth, maxWidth);
        var height = Math.Clamp(normalizedPlacement?.Height ?? DefaultWindowHeight, minHeight, maxHeight);
        var widthPixels = Math.Max(1, (int)Math.Ceiling(width * scaling));
        var heightPixels = Math.Max(1, (int)Math.Ceiling(height * scaling));

        var x = normalizedPlacement is null
            ? CenterInRange(workingArea.X, workingArea.Width, widthPixels)
            : ClampToWorkingRange((int)Math.Round(normalizedPlacement.X), workingArea.X, workingArea.Width, widthPixels);
        var y = normalizedPlacement is null
            ? CenterInRange(workingArea.Y, workingArea.Height, heightPixels)
            : ClampToWorkingRange((int)Math.Round(normalizedPlacement.Y), workingArea.Y, workingArea.Height, heightPixels);

        return new WindowPlacement(x, y, width, height, IsMaximized: false);
    }

    private static int CenterInRange(int origin, int availableSize, int itemSize)
        => origin + Math.Max(0, (availableSize - itemSize) / 2);

    private static int ClampToWorkingRange(int value, int origin, int availableSize, int itemSize)
    {
        var min = origin + WindowPlacementMarginPixels;
        var max = origin + availableSize - itemSize - WindowPlacementMarginPixels;

        if (max < min)
        {
            return origin;
        }

        return Math.Clamp(value, min, max);
    }

    private WindowPlacement? LoadWindowPlacementBestEffort()
    {
        if (_settings is null)
        {
            return null;
        }

        try
        {
            return _settings.LoadWindowPlacementAsync().AsTask().GetAwaiter().GetResult();
        }
        catch
        {
            return null;
        }
    }

    private Screen? TryGetStartupScreen(WindowPlacement? savedPlacement)
    {
        try
        {
            var normalizedPlacement = WindowPlacement.Normalize(savedPlacement);
            if (normalizedPlacement is not null)
            {
                var savedPoint = new PixelPoint(
                    (int)Math.Round(normalizedPlacement.X),
                    (int)Math.Round(normalizedPlacement.Y));
                var savedScreen = Screens.ScreenFromPoint(savedPoint);
                if (savedScreen is not null)
                {
                    return savedScreen;
                }
            }

            return Screens.Primary;
        }
        catch
        {
            return null;
        }
    }

    private void ApplyFallbackStartupPlacement(WindowPlacement? savedPlacement)
    {
        var normalizedPlacement = WindowPlacement.Normalize(savedPlacement);
        if (normalizedPlacement is null)
        {
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = Math.Max(MinWidth, normalizedPlacement.Width);
        Height = Math.Max(MinHeight, normalizedPlacement.Height);
        Position = new PixelPoint(
            (int)Math.Round(normalizedPlacement.X),
            (int)Math.Round(normalizedPlacement.Y));
        _lastNormalWindowPlacement = normalizedPlacement with { IsMaximized = false };

        if (normalizedPlacement.IsMaximized)
        {
            if (OperatingSystem.IsWindows())
            {
                _pendingWindowsStartupMaximize = true;
            }
            else
            {
                WindowState = WindowState.Maximized;
            }
        }
    }

    private void CaptureLastNormalWindowPlacement()
    {
        if (_isWindowsManualMaximized || WindowState != WindowState.Normal)
        {
            return;
        }

        _lastNormalWindowPlacement = CaptureCurrentNormalWindowPlacement();
    }

    private WindowPlacement CaptureCurrentNormalWindowPlacement()
    {
        var width = Width > 0 && !double.IsNaN(Width) && !double.IsInfinity(Width)
            ? Width
            : Math.Max(MinWidth, Bounds.Width);
        var height = Height > 0 && !double.IsNaN(Height) && !double.IsInfinity(Height)
            ? Height
            : Math.Max(MinHeight, Bounds.Height);

        return new WindowPlacement(
            Position.X,
            Position.Y,
            Math.Max(MinWidth, width),
            Math.Max(MinHeight, height),
            IsMaximized: false);
    }

    private void SaveCurrentWindowPlacementBestEffort()
    {
        if (_settings is null)
        {
            return;
        }

        try
        {
            var placement = CreateWindowPlacementForPersistence();
            _settings.SaveWindowPlacementAsync(placement).AsTask().GetAwaiter().GetResult();
        }
        catch
        {
            // Window placement persistence is best-effort and must never block closing.
        }
    }

    private WindowPlacement? CreateWindowPlacementForPersistence()
    {
        if (_isWindowsManualMaximized)
        {
            var normalPlacement = _lastNormalWindowPlacement ?? CaptureCurrentNormalWindowPlacement();
            return normalPlacement with { IsMaximized = true };
        }

        if (WindowState == WindowState.Normal)
        {
            return CaptureCurrentNormalWindowPlacement();
        }

        if (WindowState == WindowState.Maximized)
        {
            var normalPlacement = _lastNormalWindowPlacement ?? CaptureCurrentNormalWindowPlacement();
            return normalPlacement with { IsMaximized = true };
        }

        return _lastNormalWindowPlacement;
    }

    private void UpdateReadingProgressBarWidth()
    {
        var progressBar = this.FindControl<Border>("ReadingProgressBar");
        if (progressBar is null)
        {
            return;
        }

        if (!_viewModel.IsViewer || _viewModel.IsEditMode)
        {
            progressBar.Width = 0;
            return;
        }

        var bodyPanel = this.FindControl<Panel>("BodyPanel");
        var hostWidth = bodyPanel?.Bounds.Width ?? Bounds.Width;
        var progressRatio = Math.Clamp(_viewModel.ReadingProgress / 100.0, 0, 1);
        progressBar.Width = hostWidth * progressRatio;
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_allowConfirmedClose && _viewModel.TryQueueCloseRequest())
        {
            e.Cancel = true;
            return;
        }

        SaveCurrentWindowPlacementBestEffort();
    }

    private bool IsPointerWithinOpenOverlay(Visual source)
    {
        // «Настройки» — модальная карточка: скрим накрывает всё окно, и клик по нему
        // окно не закрывает, как и у остальных диалогов. Закрывают Esc и ✕.
        if (_viewModel.IsAppSettingsOpen)
        {
            return true;
        }

        if (_viewModel.IsSettingsOpen)
        {
            var settingsPanel = this.FindControl<Control>("SettingsPanel");
            if (settingsPanel is not null && IsWithinVisual(source, settingsPanel))
            {
                return true;
            }

            var settingsTrigger = this.FindControl<ToggleButton>("SettingsTriggerButton");
            return settingsTrigger is not null && IsWithinVisual(source, settingsTrigger);
        }

        if (_viewModel.IsAppMenuOpen)
        {
            var appMenuPanel = this.FindControl<Control>("AppMenuPanel");
            if (appMenuPanel is not null && IsWithinVisual(source, appMenuPanel))
            {
                return true;
            }
        }

        var appMenuTrigger = this.FindControl<ToggleButton>("AppMenuTriggerButton");
        return appMenuTrigger is not null && IsWithinVisual(source, appMenuTrigger);
    }

    private void SyncOverlayWindowClasses()
    {
        Classes.Set("mm-reading-settings-open", _viewModel.IsSettingsOpen);
        Classes.Set("mm-app-menu-open", _viewModel.IsAppMenuOpen);
    }

    private void OnViewModelCloseRequested(object? sender, EventArgs e)
    {
        _allowConfirmedClose = true;
        Close();
    }

    #pragma warning disable SYSLIB1054
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(
        IntPtr hdc,
        IntPtr lprcClip,
        MonitorEnumProc lpfnEnum,
        IntPtr dwData);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        IntPtr hwnd,
        uint dwAttribute,
        out RECT pvAttribute,
        int cbAttribute);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        uint dwAttribute,
        ref int pvAttribute,
        int cbAttribute);
    #pragma warning restore SYSLIB1054

    private delegate bool MonitorEnumProc(
        IntPtr hMonitor,
        IntPtr hdcMonitor,
        IntPtr lprcMonitor,
        IntPtr dwData);

    private static class WindowsMessages
    {
        public const uint WmGetMinMaxInfo = 0x0024;
        public const uint MonitorDefaultToNearest = 0x00000002;
        public const uint SwpNoZOrder = 0x0004;
        public const uint SwpNoOwnerZOrder = 0x0200;
        public const uint SwpFrameChanged = 0x0020;
        public const uint DwmwaExtendedFrameBounds = 9;
        public const uint DwmwaWindowCornerPreference = 33;
        public const uint DwmwaBorderColor = 34;
        public const int DwmwcpDefault = 0;
        public const int DwmwcpDoNotRound = 1;
        public const int DwmwaColorDefault = unchecked((int)0xFFFFFFFF);
        public const int DwmwaColorNone = unchecked((int)0xFFFFFFFE);
        public const int SnapBoundsTolerancePixels = 8;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public PixelRect ToPixelRect()
            => new(Left, Top, Math.Max(0, Right - Left), Math.Max(0, Bottom - Top));
    }
}
