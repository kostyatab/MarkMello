using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MarkMello.Presentation.ViewModels;
using MarkMello.Presentation.Views.Markdown;

namespace MarkMello.Presentation.Views;

public partial class ViewerView : UserControl, IFindHost
{
    private const double WheelStepMultiplier = 6.0;
    private const double KeyboardPageOverlap = 48.0;
    private ScrollViewer? _scroll;
    private MarkdownDocumentView? _documentView;
    private bool _hasRenderedDocument;

    // Идёт пересборка после докраски кода (ADR-0010 §4), а не новый документ.
    private bool _isRecolorRender;

    public ViewerView()
    {
        InitializeComponent();
    }

    // ---------- IFindHost ----------

    public string? ActiveQuery => _documentView?.ActiveSearchQuery;

    public int MatchIndex => _documentView?.MatchIndex ?? -1;

    public int MatchCount => _documentView?.MatchCount ?? 0;

    public event EventHandler? FindStateChanged;

    public void ApplyQuery(string? query) => _documentView?.ApplySearchQuery(query);

    public void FindNext() => _documentView?.FindNext();

    public void FindPrevious() => _documentView?.FindPrevious();

    public void ClearFind() => _documentView?.ApplySearchQuery(null);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _scroll = this.FindControl<ScrollViewer>("DocScroll");
        if (_scroll is not null)
        {
            _scroll.ScrollChanged += OnScrollChanged;
            _scroll.AddHandler(InputElement.PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Tunnel);
        }

        AddHandler(KeyDownEvent, OnViewerKeyDown, RoutingStrategies.Tunnel);

        _documentView = this.FindControl<MarkdownDocumentView>("DocumentView");
        if (_documentView is not null)
        {
            _documentView.DocumentRendered += OnDocumentRendered;
            _documentView.DocumentRenderInvalidated += OnDocumentRenderInvalidated;
            _documentView.MarkdownFileLinkRequested += OnMarkdownFileLinkRequested;
            _documentView.SearchStateChanged += OnDocumentSearchStateChanged;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _hasRenderedDocument = false;
        _isRecolorRender = false;

        if (_scroll is not null)
        {
            _scroll.ScrollChanged -= OnScrollChanged;
            _scroll.RemoveHandler(InputElement.PointerWheelChangedEvent, OnPointerWheelChanged);
            _scroll = null;
        }

        RemoveHandler(KeyDownEvent, OnViewerKeyDown);

        if (_documentView is not null)
        {
            _documentView.DocumentRendered -= OnDocumentRendered;
            _documentView.DocumentRenderInvalidated -= OnDocumentRenderInvalidated;
            _documentView.MarkdownFileLinkRequested -= OnMarkdownFileLinkRequested;
            _documentView.SearchStateChanged -= OnDocumentSearchStateChanged;
            _documentView = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_scroll is null || Math.Abs(e.Delta.Y) <= double.Epsilon)
        {
            return;
        }

        // Preserve horizontal wheel gestures for nested controls such as
        // horizontally scrollable code blocks. We only take over primarily
        // vertical scrolling to match the faster browser-like reading feel.
        if (Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y))
        {
            return;
        }

        var maxOffset = _scroll.ScrollBarMaximum.Y;
        if (maxOffset <= 0)
        {
            return;
        }

        var baseStep = _scroll.SmallChange.Height > 0 ? _scroll.SmallChange.Height : 16.0;
        var wheelStep = baseStep * WheelStepMultiplier;
        var nextOffset = Math.Clamp(_scroll.Offset.Y - e.Delta.Y * wheelStep, 0, maxOffset);

        if (Math.Abs(nextOffset - _scroll.Offset.Y) <= double.Epsilon)
        {
            return;
        }

        _scroll.Offset = new Vector(_scroll.Offset.X, nextOffset);
        e.Handled = true;
    }

    private void OnViewerKeyDown(object? sender, KeyEventArgs e)
    {
        if (_scroll is null || e.Handled || DataContext is not ShellViewModel { IsViewer: true, IsEditMode: false })
        {
            return;
        }

        if (HasCommandModifier(e.KeyModifiers) || e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            return;
        }

        var nextOffsetY = GetKeyboardScrollOffset(
            e.Key,
            e.KeyModifiers,
            _scroll.Offset.Y,
            _scroll.ScrollBarMaximum.Y,
            _scroll.SmallChange.Height,
            _scroll.Viewport.Height);

        if (nextOffsetY is null || Math.Abs(nextOffsetY.Value - _scroll.Offset.Y) <= double.Epsilon)
        {
            return;
        }

        _scroll.Offset = new Vector(_scroll.Offset.X, nextOffsetY.Value);
        e.Handled = true;
    }

    internal static double? GetKeyboardScrollOffset(
        Key key,
        KeyModifiers modifiers,
        double currentOffset,
        double maximumOffset,
        double smallChange,
        double viewportHeight)
    {
        var max = Math.Max(0, maximumOffset);
        var current = Math.Clamp(currentOffset, 0, max);
        var lineStep = smallChange > 0 ? smallChange : 40.0;
        var pageStep = Math.Max(lineStep, viewportHeight - KeyboardPageOverlap);

        var target = key switch
        {
            Key.Down => current + lineStep,
            Key.Up => current - lineStep,
            Key.PageDown => current + pageStep,
            Key.PageUp => current - pageStep,
            Key.Home => 0,
            Key.End => max,
            Key.Space when modifiers.HasFlag(KeyModifiers.Shift) => current - pageStep,
            Key.Space => current + pageStep,
            _ => (double?)null,
        };

        return target is null ? null : Math.Clamp(target.Value, 0, max);
    }

    private static bool HasCommandModifier(KeyModifiers modifiers)
        => modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta);

    private void OnDocumentRendered(object? sender, EventArgs e)
    {
        if (_isRecolorRender)
        {
            // Докраска кода: тот же документ, другие только цвета. Фокус
            // и прокрутка к совпадению поиска остаются как были.
            _isRecolorRender = false;
            return;
        }

        if (DataContext is ShellViewModel vm)
        {
            vm.MarkReadableDocumentRendered();
            vm.StartPendingCodeHighlighting();
            RestorePendingScrollOffset(vm);
        }

        _hasRenderedDocument = true;
        FocusDocumentViewAsync();

        // Keep the active search match in view after a document re-render.
        if (_documentView?.MatchIndex >= 0)
        {
            _documentView.ScrollToActiveMatch();
        }
    }

    /// <summary>
    /// Возврат на вкладку восстанавливает её позицию прокрутки. Делается после отрисовки:
    /// до неё ScrollBarMaximum ещё нулевой и любое смещение схлопнется в ноль.
    /// </summary>
    private void RestorePendingScrollOffset(ShellViewModel viewModel)
    {
        if (viewModel.TakePendingScrollOffset() is not { } offset || _scroll is null)
        {
            return;
        }

        if (offset <= 0)
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                if (_scroll is null)
                {
                    return;
                }

                var target = Math.Clamp(offset, 0, _scroll.ScrollBarMaximum.Y);
                _scroll.Offset = new Vector(_scroll.Offset.X, target);
            },
            DispatcherPriority.Background);
    }

    private void OnDocumentSearchStateChanged(object? sender, EventArgs e)
        => FindStateChanged?.Invoke(this, EventArgs.Empty);

    private void FocusDocumentViewAsync()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_documentView is not null && DataContext is ShellViewModel { IsViewer: true, IsEditMode: false })
            {
                _documentView.Focus(NavigationMethod.Unspecified);
            }
        }, DispatcherPriority.Background);
    }

    private void OnDocumentRenderInvalidated(object? sender, EventArgs e)
    {
        _isRecolorRender = _hasRenderedDocument
            && DataContext is ShellViewModel vm
            && vm.ConsumeRecolor(_documentView?.Document);
        if (_isRecolorRender)
        {
            return;
        }

        _hasRenderedDocument = false;
    }

    private async void OnMarkdownFileLinkRequested(object? sender, MarkdownFileLinkRequestedEventArgs e)
    {
        if (DataContext is not ShellViewModel vm)
        {
            return;
        }

        await vm.OpenLinkedDocumentAsync(e.TargetPath).ConfigureAwait(true);
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_scroll is null)
        {
            return;
        }

        var max = _scroll.ScrollBarMaximum.Y;
        var current = _scroll.Offset.Y;
        if (DataContext is ShellViewModel vm)
        {
            vm.ReadingProgress = max > 0 ? Math.Clamp(current / max * 100.0, 0, 100) : 0;

            // Позиция уезжает во вкладку на каждое изменение: при переключении
            // вьюер уже показывает другой документ и спрашивать его поздно.
            vm.ReportScrollOffset(current);
        }
    }
}
