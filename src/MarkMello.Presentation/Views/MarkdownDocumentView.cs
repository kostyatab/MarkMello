using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Logging;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MarkMello.Application.Abstractions;
using MarkMello.Domain;
using MarkMello.Presentation.Clipboard;
using MarkMello.Presentation.Localization;
using MarkMello.Presentation.Views.Markdown;
using MarkMello.Presentation.Views.Markdown.Minimap;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Threading;

namespace MarkMello.Presentation.Views;

/// <summary>
/// Native Markdown renderer для viewer mode.
/// В этой итерации переносит selection ownership на document level и покрывает:
/// headings, paragraphs, quote paragraph content, list paragraph content,
/// code blocks и table cells.
/// </summary>
public sealed class MarkdownDocumentView : UserControl
{
    public static readonly StyledProperty<RenderedMarkdownDocument?> DocumentProperty =
        AvaloniaProperty.Register<MarkdownDocumentView, RenderedMarkdownDocument?>(nameof(Document));

    public static readonly StyledProperty<Thickness> DocumentPaddingProperty =
        AvaloniaProperty.Register<MarkdownDocumentView, Thickness>(
            nameof(DocumentPadding),
            new Thickness(0));

    public static readonly StyledProperty<ReadingPreferences> ReadingPreferencesProperty =
        AvaloniaProperty.Register<MarkdownDocumentView, ReadingPreferences>(
            nameof(ReadingPreferences),
            ReadingPreferences.Default);

    public static readonly StyledProperty<IImageSourceResolver?> ImageSourceResolverProperty =
        AvaloniaProperty.Register<MarkdownDocumentView, IImageSourceResolver?>(nameof(ImageSourceResolver));

    private const double DragSelectionThreshold = 4;
    private const double CodeBlockHorizontalScrollBarReserve = 16;
    private const double TableHorizontalScrollBarReserve = 16;
    private static readonly TimeSpan CodeCopyConfirmationDuration = TimeSpan.FromSeconds(1.5);

    // The code block copy button is a 24px hit target around a 13px Lucide icon.
    // What has to line up with the info label is the icon's ink (2..22 of the
    // 24 grid), not the button box, so the button is shifted out by this inset.
    private const double CodeCopyButtonSize = 24;
    private const double CodeCopyIconSize = 13;
    private const double CodeCopyIconInkInset = (CodeCopyButtonSize - CodeCopyIconSize) / 2 + CodeCopyIconSize * 2 / 24;

    // Сторона иконки чекбокса task list относительно размера шрифта: рамка Lucide
    // занимает 20/24 сетки, то есть почти 1 em — выше заглавных букв, и галочка
    // внутри контура читается с первого взгляда.
    private const double TaskCheckboxSizeToFontSize = 1.15;

    // Отступ текста пункта списка от маркера.
    private const double ListItemTextIndent = 12;

    // В списке с чекбоксами зазоры меньше: чекбокс шире «•», и с обычным
    // отступом пункт распадается на номер, иконку и текст. От чекбокса до текста —
    // вдвое меньше обычного, от номера до чекбокса — ещё меньше: номер «1. » уже
    // заканчивается пробелом.
    private const double TaskListItemTextIndent = ListItemTextIndent / 2;
    private const double TaskCheckboxIndentAfterNumber = 3;

    // Шапка GitHub alert: иконка заметно выше строчных букв заголовка и вплотную
    // к нему — читается как одна метка; и отступ шапки от текста alert.
    private const double AlertIconSizeToFontSize = 1.25;
    private const double AlertIconTitleGap = 4;
    private const double AlertHeaderBottomMargin = 4;

    private static readonly DataFormat<byte[]> WindowsHtmlClipboardFormat = DataFormat.CreateBytesPlatformFormat("HTML Format");
    private static readonly DataFormat<byte[]> HtmlClipboardFormat = DataFormat.CreateBytesPlatformFormat("text/html");

    private readonly StackPanel _root = new()
    {
        Orientation = Orientation.Vertical,
        Spacing = 0,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private readonly Border _viewport = new()
    {
        Background = Brushes.Transparent,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    private readonly List<MarkdownDocumentSelectionFragmentBase> _selectionFragments = [];

    // Путь текстовой карты для каждого фрагмента из _selectionFragments (тот же
    // порядок). Нужен, чтобы у переиспользованного блока обновить DocumentRange:
    // правка выше по документу сдвигает абсолютные offset'ы всех блоков ниже.
    private readonly List<string> _selectionFragmentPaths = [];
    private readonly List<DocumentTextRange> _searchMatches = [];
    private string _activeSearchQuery = string.Empty;
    private int _activeMatchIndex = -1;
    private readonly Dictionary<string, Control> _headingAnchorTargets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _headingAnchorCounts = new(StringComparer.Ordinal);

    // Заголовки в порядке документа. Индекс якорей строится из этого списка в
    // конце Rebuild: нумерация дублей зависит от порядка по всему документу и
    // не может считаться поблочно.
    private readonly List<(MarkdownHeadingBlock Block, Control Control)> _headingAnchorRegistrations = [];
    private readonly List<MarkdownSourceLineVisualAnchor> _sourceLineAnchors = [];
    private List<BuiltTopLevelBlock> _builtBlocks = [];
    private MarkdownDocumentTextMap _textMap = MarkdownDocumentTextMap.Empty;

    // Заголовки GitHub alerts, с которыми построены текстовая карта и блоки.
    private MarkdownAlertTitles _alertTitles = MarkdownAlertTitles.Create(GetLocalizedString);
    private ILocalizationService? _localization;
    private bool _isPointerPressed;
    private bool _isDraggingSelection;
    private Point _pointerPressOrigin;
    private MarkdownDocumentSelectionFragmentBase? _pressedFragment;
    private MarkdownLinkSpan? _pressedLink;
    private bool _preserveSelectionOnRelease;
    private MenuItem? _copyMenuItem;
    private MenuItem? _copyLinkMenuItem;
    private MenuItem? _copyTelegramMarkdownMenuItem;
    private MenuItem? _selectAllMenuItem;
    private MarkdownLinkSpan? _contextMenuLink;
    private IReadOnlyList<string> _contextMenuSelectedLinkUrls = Array.Empty<string>();

    // Метка сноски, с которой читатель перешёл к сноске: номер сноски в блоке
    // сносок возвращает именно к ней, а не к первой ссылке на ту же сноску.
    private (MarkdownSelectionTextFragment Fragment, MarkdownLinkSpan Link)? _footnoteReturnTarget;
    private CancellationTokenSource? _readingPreferencesRefreshCts;
    private long _renderGeneration;
    private bool _hasPendingRenderedNotification;

    static MarkdownDocumentView()
    {
        DocumentProperty.Changed.AddClassHandler<MarkdownDocumentView>((view, _) => view.Rebuild());
        ImageSourceResolverProperty.Changed.AddClassHandler<MarkdownDocumentView>((view, _) => view.RebuildFromScratch());
        DocumentPaddingProperty.Changed.AddClassHandler<MarkdownDocumentView>((view, _) => view.ApplyDocumentPadding());
        ReadingPreferencesProperty.Changed.AddClassHandler<MarkdownDocumentView>((view, _) => view.RefreshForReadingPreferencesChange());
    }

    public MarkdownDocumentView()
    {
        Focusable = true;
        IsTabStop = true;
        UseLayoutRounding = true;
        HorizontalAlignment = HorizontalAlignment.Stretch;

        // Wide tables extend into the page margins (MarkdownTableHost): the view
        // must neither clip them nor their hit testing. The page's own
        // ScrollViewer still clips to the window.
        ClipToBounds = false;
        _root.UseLayoutRounding = true;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        EnsureRootTransitions();

        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        KeyDown += OnKeyDown;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += OnPointerCaptureLost;

        // Suppress outer ScrollViewer auto-scroll that would otherwise happen
        // when this (document-sized) control becomes focused. The event
        // bubbles up from the Focus() call; we swallow it ourselves.
        AddHandler(RequestBringIntoViewEvent, OnRequestBringIntoView, RoutingStrategies.Bubble);

        _viewport.Child = _root;
        ApplyDocumentPadding();
        Content = _viewport;

        EnsureContextMenu();
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        EnsureRootTransitions();
        EnsureContextMenu();

        _localization = TryGetLocalization();
        if (_localization is not null)
        {
            _localization.PropertyChanged += OnLocalizationChanged;
        }

        // Язык мог смениться, пока view не было в дереве.
        RefreshAlertTitles();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_localization is not null)
        {
            _localization.PropertyChanged -= OnLocalizationChanged;
            _localization = null;
        }

        LayoutUpdated -= OnLayoutUpdatedAfterDocumentRebuild;
        _hasPendingRenderedNotification = false;
        _readingPreferencesRefreshCts?.Cancel();
        _readingPreferencesRefreshCts?.Dispose();
        _readingPreferencesRefreshCts = null;
    }

    private void EnsureRootTransitions()
    {
        if (_root.Transitions is not null || !Dispatcher.UIThread.CheckAccess())
        {
            return;
        }

        _root.Transitions =
        [
            new DoubleTransition
            {
                Property = Visual.OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(140),
                Easing = new CubicEaseOut()
            }
        ];
    }

    private void EnsureContextMenu()
    {
        if (ContextMenu is not null || !Dispatcher.UIThread.CheckAccess())
        {
            return;
        }

        ContextMenu = BuildContextMenu();
    }

    private void OnRequestBringIntoView(object? sender, RequestBringIntoViewEventArgs e)
    {
        // If the request originates on this control itself (e.g. from focus
        // change during a selection gesture), there is nothing to bring into
        // view -- the document already is the scroll content. Allowing it to
        // bubble causes the ScrollViewer to jump to the top of our bounds.
        if (ReferenceEquals(e.TargetObject, this))
        {
            e.Handled = true;
        }
    }

    public RenderedMarkdownDocument? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public ReadingPreferences ReadingPreferences
    {
        get => GetValue(ReadingPreferencesProperty);
        set => SetValue(ReadingPreferencesProperty, value);
    }

    public Thickness DocumentPadding
    {
        get => GetValue(DocumentPaddingProperty);
        set => SetValue(DocumentPaddingProperty, value);
    }

    public IImageSourceResolver? ImageSourceResolver
    {
        get => GetValue(ImageSourceResolverProperty);
        set => SetValue(ImageSourceResolverProperty, value);
    }

    public int? SelectionAnchor { get; private set; }

    public int SelectionStart { get; private set; }

    public int SelectionEnd { get; private set; }

    public bool HasSelection => SelectionEnd > SelectionStart;

    public string SelectedText => HasSelection
        ? _textMap.GetText(new DocumentTextRange(SelectionStart, SelectionEnd))
        : string.Empty;

    public event EventHandler? DocumentRendered;

    public event EventHandler? DocumentRenderInvalidated;

    public event EventHandler<MarkdownFileLinkRequestedEventArgs>? MarkdownFileLinkRequested;

    /// <summary>
    /// Currently active search query, or null when find is not active.
    /// </summary>
    public string? ActiveSearchQuery => _activeSearchQuery.Length == 0 ? null : _activeSearchQuery;

    /// <summary>
    /// Total number of matches for the active query (0 when not searching).
    /// </summary>
    public int MatchCount => _searchMatches.Count;

    /// <summary>
    /// Zero-based index of the current match, or -1 when there are no matches.
    /// </summary>
    public int MatchIndex => _searchMatches.Count == 0 ? -1 : _activeMatchIndex;

    /// <summary>
    /// Raised whenever the match set or the current match changes, including
    /// after document rebuilds that re-apply an active query.
    /// </summary>
    public event EventHandler? SearchStateChanged;

    /// <summary>
    /// Starts (or updates) a document-wide, case-insensitive search over the
    /// rendered text. An empty or null query clears all search highlights.
    /// </summary>
    public void ApplySearchQuery(string? query)
    {
        // Not trimmed: a query is whatever the user typed, spaces included.
        // Trimming would make " the " and a bare space unsearchable.
        var normalized = query ?? string.Empty;
        if (string.Equals(_activeSearchQuery, normalized, StringComparison.Ordinal))
        {
            return;
        }

        _activeSearchQuery = normalized;
        RebuildSearchMatches(keepCurrentIndex: false);
        TryScrollToActiveMatch();
    }

    public bool FindNext()
    {
        if (_searchMatches.Count == 0)
        {
            return false;
        }

        _activeMatchIndex = MarkdownTextSearch.NextIndex(_activeMatchIndex, _searchMatches.Count);
        ApplyActiveSearchHighlightToFragments();
        SearchStateChanged?.Invoke(this, EventArgs.Empty);
        TryScrollToActiveMatch();
        return true;
    }

    public bool FindPrevious()
    {
        if (_searchMatches.Count == 0)
        {
            return false;
        }

        _activeMatchIndex = MarkdownTextSearch.PreviousIndex(_activeMatchIndex, _searchMatches.Count);
        ApplyActiveSearchHighlightToFragments();
        SearchStateChanged?.Invoke(this, EventArgs.Empty);
        TryScrollToActiveMatch();
        return true;
    }

    /// <summary>
    /// Scrolls the current match into view. Called after rebuilds so the
    /// active result stays visible when the document re-renders.
    /// </summary>
    public void ScrollToActiveMatch() => TryScrollToActiveMatch();

    /// <summary>
    /// Вертикальное смещение в документе для дробной позиции в исходнике
    /// (номер строки плюс доля продвижения внутри неё).
    ///
    /// Позиция дробная не для красоты: абзац в markdown обычно записан одной
    /// длинной строкой, поэтому целочисленный номер строки не различает начало
    /// и конец абзаца и preview застревал бы на его верхней кромке, пока
    /// редактор прокручивает весь абзац.
    /// </summary>
    internal bool TryGetVerticalOffsetForSourcePosition(double sourcePosition, out double offsetY)
    {
        offsetY = 0;
        var map = CreateSourcePositionMap();
        if (map.Count == 0)
        {
            return false;
        }

        if (sourcePosition <= map[0].SourceLine)
        {
            offsetY = map[0].Y;
            return true;
        }

        for (var index = 1; index < map.Count; index++)
        {
            var next = map[index];
            if (sourcePosition > next.SourceLine)
            {
                continue;
            }

            var previous = map[index - 1];
            var ratio = (sourcePosition - previous.SourceLine) / (next.SourceLine - previous.SourceLine);
            offsetY = previous.Y + ((next.Y - previous.Y) * ratio);
            return true;
        }

        offsetY = map[^1].Y;
        return true;
    }

    /// <summary>
    /// Обратное отображение: дробная позиция в исходнике для вертикального
    /// смещения в документе.
    /// </summary>
    internal bool TryGetSourcePositionForVerticalOffset(double offsetY, out double sourcePosition)
    {
        sourcePosition = 0;
        var map = CreateSourcePositionMap();
        if (map.Count == 0)
        {
            return false;
        }

        var normalizedOffset = Math.Max(0, offsetY);
        if (normalizedOffset <= map[0].Y)
        {
            sourcePosition = map[0].SourceLine;
            return true;
        }

        for (var index = 1; index < map.Count; index++)
        {
            var next = map[index];
            if (normalizedOffset > next.Y)
            {
                continue;
            }

            var previous = map[index - 1];
            var ratio = (normalizedOffset - previous.Y) / (next.Y - previous.Y);
            sourcePosition = previous.SourceLine + ((next.SourceLine - previous.SourceLine) * ratio);
            return true;
        }

        sourcePosition = map[^1].SourceLine;
        return true;
    }

    /// <summary>
    /// Монотонная кусочно-линейная карта «строка исходника → Y документа».
    ///
    /// Собирается из измеренных якорей блоков; вложенные блоки (пункты списка,
    /// абзацы цитаты) дают дополнительные точки и тем самым разрешение внутри
    /// крупных блоков. Точки, не возрастающие сразу по обеим координатам,
    /// отбрасываются — иначе интерполяция делила бы на ноль или ехала назад.
    /// </summary>
    private List<MarkdownSourcePositionPoint> CreateSourcePositionMap()
    {
        var anchors = CreateMeasuredSourceLineAnchors();
        var map = new List<MarkdownSourcePositionPoint>(anchors.Count + 1);

        foreach (var anchor in anchors)
        {
            if (map.Count == 0)
            {
                map.Add(new MarkdownSourcePositionPoint(anchor.StartLine, anchor.Y));
                continue;
            }

            var last = map[^1];
            if (anchor.StartLine > last.SourceLine && anchor.Y > last.Y)
            {
                map.Add(new MarkdownSourcePositionPoint(anchor.StartLine, anchor.Y));
            }
        }

        if (map.Count == 0)
        {
            return map;
        }

        // Замыкающая точка: конец последнего блока в низу документа, иначе
        // хвост документа не имел бы куда отображаться. Берём максимальную
        // конечную строку, а не последний по Y якорь: им может оказаться
        // вложенный блок с более коротким span.
        var lastLine = 0;
        foreach (var anchor in anchors)
        {
            lastLine = Math.Max(lastLine, anchor.EndLine);
        }

        var documentBottom = _root.TranslatePoint(new Point(0, _root.Bounds.Height), this)?.Y;
        var tailLine = lastLine + 1;
        if (documentBottom is { } bottom && tailLine > map[^1].SourceLine && bottom > map[^1].Y)
        {
            map.Add(new MarkdownSourcePositionPoint(tailLine, bottom));
        }

        return map;
    }

    // Test-only: enumerates registered source-line anchor spans without
    // requiring the view to be laid out. Edit-mode scroll synchronization
    // needs every block with a SourceSpan (including diagrams) to register
    // here so the editor cursor can map to the preview block. Layout-
    // measured offsets live behind <see cref="CreateMeasuredSourceLineAnchors"/>.
    internal IReadOnlyList<MarkdownSourceSpan> EnumerateRegisteredSourceSpans()
    {
        var spans = new List<MarkdownSourceSpan>(_sourceLineAnchors.Count);
        foreach (var anchor in _sourceLineAnchors)
        {
            spans.Add(anchor.SourceSpan);
        }
        return spans;
    }

    private List<MarkdownSourceLineAnchorSnapshot> CreateMeasuredSourceLineAnchors()
    {
        var result = new List<MarkdownSourceLineAnchorSnapshot>(_sourceLineAnchors.Count);

        foreach (var anchor in _sourceLineAnchors)
        {
            if (anchor.Control.Bounds.Width <= 0 || anchor.Control.Bounds.Height <= 0)
            {
                continue;
            }

            var origin = anchor.Control.TranslatePoint(new Point(0, 0), this);
            if (origin is null)
            {
                continue;
            }

            result.Add(new MarkdownSourceLineAnchorSnapshot(
                anchor.SourceSpan.StartLine,
                anchor.SourceSpan.EndLine,
                Math.Max(0, origin.Value.Y)));
        }

        result.Sort(static (left, right) =>
        {
            var visualComparison = left.Y.CompareTo(right.Y);
            return visualComparison != 0
                ? visualComparison
                : left.StartLine.CompareTo(right.StartLine);
        });

        return result;
    }

    internal DocumentMiniatureSnapshot CreateMiniatureSnapshot()
    {
        if (Document is null || Bounds.Height <= 0 || Bounds.Width <= 0)
        {
            return DocumentMiniatureSnapshot.Empty;
        }

        return new DocumentMiniatureSnapshot(
            totalWidth: Math.Max(1, Bounds.Width),
            totalHeight: Math.Max(1, Bounds.Height));
    }

    internal void RenderMiniature(DrawingContext context, Rect targetBounds)
    {
        var snapshot = CreateMiniatureSnapshot();
        if (snapshot.IsEmpty || targetBounds.Width <= 0 || targetBounds.Height <= 0)
        {
            return;
        }

        var scaleX = targetBounds.Width / snapshot.TotalWidth;
        var scaleY = targetBounds.Height / snapshot.TotalHeight;

        using (context.PushClip(targetBounds))
        {
            foreach (var border in _root.GetVisualDescendants().OfType<Border>().Where(IsMiniatureStructuralBorder))
            {
                DrawBorderDecorationMiniature(context, border, targetBounds, scaleX, scaleY);
            }

            foreach (var fragment in _selectionFragments)
            {
                DrawControlMiniature(context, fragment, targetBounds, scaleX, scaleY);
            }

            foreach (var imageView in _root.GetVisualDescendants().OfType<MarkdownImageView>())
            {
                DrawImagePlaceholderMiniature(context, imageView, targetBounds, scaleX, scaleY);
            }

            foreach (var rule in _root.GetVisualDescendants().OfType<Border>().Where(static border => border.Classes.Contains("mm-md-hr")))
            {
                DrawHorizontalRuleMiniature(context, rule, targetBounds, scaleX, scaleY);
            }
        }
    }

    private void DrawControlMiniature(
        DrawingContext context,
        Control control,
        Rect targetBounds,
        double scaleX,
        double scaleY)
    {
        if (control.Bounds.Width <= 0 || control.Bounds.Height <= 0)
        {
            return;
        }

        var origin = control.TranslatePoint(new Point(0, 0), this);
        if (origin is null)
        {
            return;
        }

        var matrix = new Matrix(
            scaleX,
            0,
            0,
            scaleY,
            targetBounds.X + origin.Value.X * scaleX,
            targetBounds.Y + origin.Value.Y * scaleY);

        using (context.PushTransform(matrix))
        {
            if (control is MarkdownSelectionTextFragment textFragment)
            {
                textFragment.RenderMiniature(context);
                return;
            }

            control.Render(context);
        }
    }

    private void DrawBorderDecorationMiniature(
        DrawingContext context,
        Border border,
        Rect targetBounds,
        double scaleX,
        double scaleY)
    {
        var bounds = TranslateControlBounds(border);
        if (bounds is null)
        {
            return;
        }

        var target = MapMiniatureRect(bounds.Value, targetBounds, scaleX, scaleY);
        if (target.Width <= 0 || target.Height <= 0)
        {
            return;
        }

        var background = border.Background;
        var borderBrush = border.BorderBrush;
        var pen = borderBrush is null || IsEmptyThickness(border.BorderThickness)
            ? null
            : new Pen(borderBrush, 1);

        if (background is null && pen is null)
        {
            return;
        }

        context.DrawRectangle(background, pen, target, 1.5, 1.5);
    }

    private void DrawImagePlaceholderMiniature(
        DrawingContext context,
        Control imageView,
        Rect targetBounds,
        double scaleX,
        double scaleY)
    {
        var bounds = TranslateControlBounds(imageView);
        if (bounds is null)
        {
            return;
        }

        var target = MapMiniatureRect(bounds.Value, targetBounds, scaleX, scaleY);
        if (target.Width <= 0 || target.Height <= 0)
        {
            return;
        }

        var fill = LookupBrush("MmSurfaceRaisedBrush") ?? LookupBrush("MmCodeBackgroundBrush") ?? Brushes.Transparent;
        var stroke = LookupBrush("MmBorderSubtleBrush") ?? LookupBrush("MmTextFaintBrush");
        context.DrawRectangle(fill, stroke is null ? null : new Pen(stroke, 1), target, 1.5, 1.5);
    }

    private void DrawHorizontalRuleMiniature(
        DrawingContext context,
        Control rule,
        Rect targetBounds,
        double scaleX,
        double scaleY)
    {
        var bounds = TranslateControlBounds(rule);
        if (bounds is null)
        {
            return;
        }

        var target = MapMiniatureRect(bounds.Value, targetBounds, scaleX, scaleY);
        if (target.Width <= 0 || target.Height <= 0)
        {
            return;
        }

        var brush = LookupBrush("MmBorderBrush") ?? LookupBrush("MmTextFaintBrush") ?? Brushes.Gray;
        context.DrawRectangle(brush, null, target);
    }

    private Rect? TranslateControlBounds(Control control)
    {
        if (control.Bounds.Width <= 0 || control.Bounds.Height <= 0)
        {
            return null;
        }

        var origin = control.TranslatePoint(new Point(0, 0), this);
        return origin is null
            ? null
            : new Rect(origin.Value, control.Bounds.Size);
    }

    private static bool IsMiniatureStructuralBorder(Border border)
        => border.Classes.Contains("mm-md-quote")
            || border.Classes.Contains("mm-md-codeblock")
            || border.Classes.Contains("mm-md-table")
            || border.Classes.Contains("mm-md-table-header-cell")
            || border.Classes.Contains("mm-md-table-cell");

    private static bool IsEmptyThickness(Thickness thickness)
        => thickness.Left <= 0 && thickness.Top <= 0 && thickness.Right <= 0 && thickness.Bottom <= 0;

    private static Rect MapMiniatureRect(Rect sourceBounds, Rect targetBounds, double scaleX, double scaleY)
        => new(
            targetBounds.X + sourceBounds.X * scaleX,
            targetBounds.Y + sourceBounds.Y * scaleY,
            sourceBounds.Width * scaleX,
            Math.Max(1, sourceBounds.Height * scaleY));

    public void SelectAll()
    {
        if (_textMap.Text.Length == 0)
        {
            ClearSelection();
            return;
        }

        SelectionAnchor = 0;
        SelectionStart = 0;
        SelectionEnd = _textMap.Text.Length;
        ApplySelectionToFragments();
    }

    public void ClearSelection()
    {
        SelectionAnchor = null;
        SelectionStart = 0;
        SelectionEnd = 0;
        ApplySelectionToFragments();
    }

    public void SelectRange(DocumentTextRange range)
    {
        if (_textMap.Text.Length == 0 || range.IsEmpty)
        {
            ClearSelection();
            return;
        }

        var start = Math.Clamp(range.Start, 0, _textMap.Text.Length);
        var end = Math.Clamp(range.End, start, _textMap.Text.Length);
        if (end <= start)
        {
            ClearSelection();
            return;
        }

        SelectionAnchor = start;
        SelectionStart = start;
        SelectionEnd = end;
        ApplySelectionToFragments();
    }

    /// <summary>
    /// Пересобирает preview, переиспользуя контролы блоков, которые не
    /// изменились с прошлого рендера.
    ///
    /// Полная пересборка стоит порядка полусекунды на документе в сотню
    /// килобайт, а правка обычно затрагивает один блок, поэтому неизменившиеся
    /// блоки остаются в дереве как есть — им не нужны ни повторное построение,
    /// ни повторный layout.
    /// </summary>
    private void Rebuild()
    {
        DocumentRenderInvalidated?.Invoke(this, EventArgs.Empty);
        ResetPointerState();

        var document = Document;

        // Заголовки alert — часть текстового потока: если язык сменился, а в
        // документе были alert, блоки строятся заново, иначе у переиспользованного
        // alert остался бы заголовок на прежнем языке.
        var alertTitles = MarkdownAlertTitles.Create(GetLocalizedString);
        var canReuseBlocks = alertTitles.HasSameTitles(_alertTitles) || !HasAlertTitles(_textMap);
        _alertTitles = alertTitles;

        _textMap = document is null
            ? MarkdownDocumentTextMap.Empty
            : MarkdownDocumentTextMap.Create(document, _alertTitles.Get);
        ClearSelection();

        var generation = ++_renderGeneration;
        _hasPendingRenderedNotification = false;

        var reusable = canReuseBlocks
            ? CreateReusableBlockIndex()
            : new Dictionary<MarkdownBlock, Queue<BuiltTopLevelBlock>>(MarkdownBlockStructuralComparer.Instance);
        var previous = _builtBlocks;
        var rebuilt = new List<BuiltTopLevelBlock>(document?.Blocks.Count ?? 0);

        _selectionFragments.Clear();
        _selectionFragmentPaths.Clear();
        _headingAnchorRegistrations.Clear();
        _sourceLineAnchors.Clear();

        if (document is not null)
        {
            for (var index = 0; index < document.Blocks.Count; index++)
            {
                var block = document.Blocks[index];
                rebuilt.Add(TryReuseBlock(reusable, block, index) ?? BuildTopLevelBlock(block, index));
            }
        }

        _builtBlocks = rebuilt;
        DisposeReplacedBlocks(previous, rebuilt);
        SyncRootChildren(rebuilt);
        RebuildHeadingAnchorIndex();

        // Метка, к которой вернёт номер сноски, могла уйти вместе с изменившимся блоком.
        if (_footnoteReturnTarget is { } returnTarget && !_selectionFragments.Contains(returnTarget.Fragment))
        {
            _footnoteReturnTarget = null;
        }

        // Re-apply the active query against the rebuilt fragments, and do it
        // even when the document is empty or null so match counts do not go
        // stale after the content disappears.
        RebuildSearchMatches(keepCurrentIndex: true);

        if (document is null || document.Blocks.Count == 0)
        {
            return;
        }

        QueueDocumentRenderedNotification(generation);
    }

    /// <summary>
    /// Индексирует блоки прошлого рендера по содержимому. Каждая запись может
    /// быть выдана один раз — один и тот же контрол не может стоять в дереве дважды.
    /// </summary>
    private Dictionary<MarkdownBlock, Queue<BuiltTopLevelBlock>> CreateReusableBlockIndex()
    {
        var index = new Dictionary<MarkdownBlock, Queue<BuiltTopLevelBlock>>(
            _builtBlocks.Count,
            MarkdownBlockStructuralComparer.Instance);

        foreach (var built in _builtBlocks)
        {
            if (!index.TryGetValue(built.Block, out var bucket))
            {
                bucket = new Queue<BuiltTopLevelBlock>();
                index[built.Block] = bucket;
            }

            bucket.Enqueue(built);
        }

        return index;
    }

    private BuiltTopLevelBlock? TryReuseBlock(
        Dictionary<MarkdownBlock, Queue<BuiltTopLevelBlock>> reusable,
        MarkdownBlock block,
        int index)
    {
        if (!reusable.TryGetValue(block, out var bucket) || bucket.Count == 0)
        {
            return null;
        }

        var built = bucket.Dequeue();
        var path = $"b{index}";

        // Содержимое то же, но позиция в документе могла измениться: обновляем
        // текстовые диапазоны выделения и исходные строки для scroll sync.
        for (var i = 0; i < built.Fragments.Length; i++)
        {
            var fragment = built.Fragments[i];
            var fragmentPath = path + built.FragmentRelativePaths[i];
            if (_textMap.TryGetFragment(fragmentPath, out var mapped))
            {
                fragment.DocumentRange = mapped.Range;
            }

            _selectionFragments.Add(fragment);
            _selectionFragmentPaths.Add(fragmentPath);
        }

        var startLine = block.SourceSpan?.StartLine ?? 0;
        foreach (var anchor in built.SourceAnchors)
        {
            _sourceLineAnchors.Add(new MarkdownSourceLineVisualAnchor(
                anchor.Control,
                new MarkdownSourceSpan(startLine + anchor.RelativeStartLine, startLine + anchor.RelativeEndLine)));
        }

        foreach (var heading in built.HeadingAnchors)
        {
            _headingAnchorRegistrations.Add(heading);
        }

        built.Block = block;
        return built;
    }

    private BuiltTopLevelBlock BuildTopLevelBlock(MarkdownBlock block, int index)
    {
        var path = $"b{index}";
        var fragmentStart = _selectionFragments.Count;
        var anchorStart = _sourceLineAnchors.Count;
        var headingStart = _headingAnchorRegistrations.Count;

        var control = BuildBlock(block, path, nested: false);

        var fragmentCount = _selectionFragments.Count - fragmentStart;
        var fragments = new MarkdownDocumentSelectionFragmentBase[fragmentCount];
        var relativePaths = new string[fragmentCount];
        for (var i = 0; i < fragmentCount; i++)
        {
            fragments[i] = _selectionFragments[fragmentStart + i];
            relativePaths[i] = _selectionFragmentPaths[fragmentStart + i][path.Length..];
        }

        var startLine = block.SourceSpan?.StartLine ?? 0;
        var anchorCount = _sourceLineAnchors.Count - anchorStart;
        var anchors = new BuiltSourceAnchor[anchorCount];
        for (var i = 0; i < anchorCount; i++)
        {
            var anchor = _sourceLineAnchors[anchorStart + i];
            anchors[i] = new BuiltSourceAnchor(
                anchor.Control,
                anchor.SourceSpan.StartLine - startLine,
                anchor.SourceSpan.EndLine - startLine);
        }

        return new BuiltTopLevelBlock
        {
            Block = block,
            Control = control,
            Fragments = fragments,
            FragmentRelativePaths = relativePaths,
            SourceAnchors = anchors,
            HeadingAnchors = _headingAnchorRegistrations
                .GetRange(headingStart, _headingAnchorRegistrations.Count - headingStart)
                .ToArray()
        };
    }

    private static void DisposeReplacedBlocks(
        List<BuiltTopLevelBlock> previous,
        List<BuiltTopLevelBlock> current)
    {
        if (previous.Count == 0)
        {
            return;
        }

        var kept = new HashSet<BuiltTopLevelBlock>(current);
        foreach (var built in previous)
        {
            if (kept.Contains(built))
            {
                continue;
            }

            foreach (var fragment in built.Fragments)
            {
                fragment.Dispose();
            }
        }
    }

    /// <summary>
    /// Приводит детей корневого стека к целевому списку, не трогая уже стоящие
    /// на своих местах контролы: удаление и повторная вставка означали бы
    /// detach/attach со всей повторной стилизацией, ради избавления от которой
    /// переиспользование и делается.
    /// </summary>
    private void SyncRootChildren(List<BuiltTopLevelBlock> blocks)
    {
        var desired = new HashSet<Control>(blocks.Count);
        foreach (var built in blocks)
        {
            desired.Add(built.Control);
        }

        for (var index = _root.Children.Count - 1; index >= 0; index--)
        {
            if (!desired.Contains(_root.Children[index]))
            {
                _root.Children.RemoveAt(index);
            }
        }

        for (var index = 0; index < blocks.Count; index++)
        {
            var control = blocks[index].Control;
            if (index < _root.Children.Count && ReferenceEquals(_root.Children[index], control))
            {
                continue;
            }

            var existing = _root.Children.IndexOf(control);
            if (existing >= 0)
            {
                _root.Children.Move(existing, index);
            }
            else
            {
                _root.Children.Insert(index, control);
            }
        }
    }

    private void RebuildHeadingAnchorIndex()
    {
        _headingAnchorTargets.Clear();
        _headingAnchorCounts.Clear();

        foreach (var (block, control) in _headingAnchorRegistrations)
        {
            var baseAnchor = MarkdownHeadingAnchorSlugger.CreateAnchor(block.Inlines);
            if (string.IsNullOrEmpty(baseAnchor))
            {
                continue;
            }

            var count = _headingAnchorCounts.TryGetValue(baseAnchor, out var currentCount)
                ? currentCount
                : 0;
            _headingAnchorCounts[baseAnchor] = count + 1;

            var anchor = count == 0
                ? baseAnchor
                : string.Create(CultureInfo.InvariantCulture, $"{baseAnchor}-{count}");

            _headingAnchorTargets.TryAdd(anchor, control);
        }
    }

    private void RebuildSearchMatches(bool keepCurrentIndex)
    {
        _searchMatches.Clear();
        if (_activeSearchQuery.Length > 0)
        {
            _searchMatches.AddRange(MarkdownTextSearch.FindAll(_textMap.Text, _activeSearchQuery));
        }

        if (_searchMatches.Count == 0)
        {
            _activeMatchIndex = -1;
        }
        else
        {
            _activeMatchIndex = keepCurrentIndex && _activeMatchIndex >= 0 && _activeMatchIndex < _searchMatches.Count
                ? _activeMatchIndex
                : 0;
        }

        ApplySearchHighlightsToFragments();
        SearchStateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Раскладывает совпадения по фрагментам.
    ///
    /// Совпадения приходят из <see cref="MarkdownTextSearch.FindAll"/>
    /// отсортированными и без перекрытий, поэтому фрагменту нужны не все они, а
    /// только окно: находим бинарным поиском первое дотягивающееся до фрагмента
    /// и идём вперёд, пока совпадения начинаются раньше его конца. Перебор всех
    /// совпадений для каждого фрагмента на документе в сотню килобайт — это
    /// порядка миллиона проверок на каждое нажатие в поле поиска.
    /// </summary>
    private void ApplySearchHighlightsToFragments()
    {
        foreach (var fragment in _selectionFragments)
        {
            fragment.SearchHighlightRanges = CollectMatchesWithin(fragment.DocumentRange);
        }

        ApplyActiveSearchHighlightToFragments();
    }

    /// <summary>
    /// Обновляет только активное совпадение. Переход к следующему или
    /// предыдущему результату не меняет набор совпадений, поэтому пересобирать
    /// списки диапазонов ради одной подсветки незачем.
    /// </summary>
    private void ApplyActiveSearchHighlightToFragments()
    {
        var activeRange = _activeMatchIndex >= 0 && _activeMatchIndex < _searchMatches.Count
            ? _searchMatches[_activeMatchIndex]
            : (DocumentTextRange?)null;

        foreach (var fragment in _selectionFragments)
        {
            if (activeRange is not { } active)
            {
                fragment.ActiveSearchHighlight = null;
                continue;
            }

            var intersection = fragment.DocumentRange.Intersection(active);
            fragment.ActiveSearchHighlight = intersection.IsEmpty ? null : intersection;
        }
    }

    private IReadOnlyList<DocumentTextRange> CollectMatchesWithin(DocumentTextRange fragmentRange)
    {
        List<DocumentTextRange>? ranges = null;

        for (var index = FindFirstMatchReaching(fragmentRange.Start); index < _searchMatches.Count; index++)
        {
            var match = _searchMatches[index];
            if (match.Start >= fragmentRange.End)
            {
                break;
            }

            var intersection = fragmentRange.Intersection(match);
            if (!intersection.IsEmpty)
            {
                // Большинство фрагментов не содержит ни одного совпадения —
                // список заводим только когда есть что в него положить.
                ranges ??= [];
                ranges.Add(intersection);
            }
        }

        return ranges ?? (IReadOnlyList<DocumentTextRange>)Array.Empty<DocumentTextRange>();
    }

    /// <summary>
    /// Индекс первого совпадения, которое заканчивается после
    /// <paramref name="offset"/>. Совпадения отсортированы по началу и не
    /// перекрываются, поэтому конец монотонен и бинарный поиск корректен.
    /// </summary>
    private int FindFirstMatchReaching(int offset)
    {
        var low = 0;
        var high = _searchMatches.Count;

        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (_searchMatches[middle].End <= offset)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private void TryScrollToActiveMatch()
    {
        if (_activeMatchIndex < 0 || _activeMatchIndex >= _searchMatches.Count)
        {
            return;
        }

        var match = _searchMatches[_activeMatchIndex];
        var fragment = FindFragmentForDocumentOffset(match.Start);
        if (fragment is null)
        {
            return;
        }

        var scrollViewer = this.FindAncestorOfType<ScrollViewer>();
        if (scrollViewer is null)
        {
            return;
        }

        var yInFragment = 0.0;
        if (fragment is MarkdownSelectionTextFragment textFragment
            && textFragment.TryGetLineTopForLocalOffset(match.Start - fragment.DocumentRange.Start, out var lineY))
        {
            yInFragment = lineY;
        }

        var targetPoint = fragment.TranslatePoint(new Point(0, yInFragment), scrollViewer);
        if (targetPoint is null)
        {
            return;
        }

        const double topInset = 24;
        var nextOffsetY = Math.Clamp(
            scrollViewer.Offset.Y + targetPoint.Value.Y - topInset,
            0,
            scrollViewer.ScrollBarMaximum.Y);

        scrollViewer.Offset = new Vector(scrollViewer.Offset.X, nextOffsetY);

        if (fragment is MarkdownSelectionTextFragment matchFragment)
        {
            ScrollMatchIntoHorizontalView(matchFragment, match, scrollViewer);
        }
    }

    /// <summary>
    /// A match in a wide table or a long code line can be off to the side of the
    /// block's own horizontal scroll area: scroll that area so the match is in
    /// view, clear of the table's edge fade.
    /// </summary>
    private void ScrollMatchIntoHorizontalView(
        MarkdownSelectionTextFragment fragment,
        DocumentTextRange match,
        ScrollViewer pageScrollViewer)
    {
        var blockScrollViewer = fragment.FindAncestorOfType<ScrollViewer>();
        if (blockScrollViewer is null
            || ReferenceEquals(blockScrollViewer, pageScrollViewer)
            || !this.IsVisualAncestorOf(blockScrollViewer)
            || !fragment.TryGetHorizontalExtentForLocalRange(
                match.Start - fragment.DocumentRange.Start,
                match.End - fragment.DocumentRange.Start,
                out var localLeft,
                out var localRight)
            || fragment.TranslatePoint(new Point(localLeft, 0), blockScrollViewer) is not { } matchLeft)
        {
            return;
        }

        const double inset = MarkdownTableHost.EdgeFadeWidth;
        var left = matchLeft.X;
        var right = left + (localRight - localLeft);
        var viewportWidth = blockScrollViewer.Viewport.Width;

        var delta = 0d;
        if (right > viewportWidth - inset)
        {
            delta = right - (viewportWidth - inset);
        }

        // A match wider than the view shows its start.
        if (left - delta < inset)
        {
            delta = left - inset;
        }

        if (Math.Abs(delta) < 0.5)
        {
            return;
        }

        blockScrollViewer.Offset = new Vector(
            Math.Clamp(blockScrollViewer.Offset.X + delta, 0, blockScrollViewer.ScrollBarMaximum.X),
            blockScrollViewer.Offset.Y);
    }

    private MarkdownDocumentSelectionFragmentBase? FindFragmentForDocumentOffset(int offset)
    {
        foreach (var fragment in _selectionFragments)
        {
            if (offset >= fragment.DocumentRange.Start && offset < fragment.DocumentRange.End)
            {
                return fragment;
            }
        }

        return null;
    }

    private void QueueDocumentRenderedNotification(long generation)
    {
        _hasPendingRenderedNotification = true;
        LayoutUpdated -= OnLayoutUpdatedAfterDocumentRebuild;
        LayoutUpdated += OnLayoutUpdatedAfterDocumentRebuild;

        Dispatcher.UIThread.Post(
            () => CompleteDocumentRenderedNotification(generation),
            DispatcherPriority.Render);
    }

    private void OnLayoutUpdatedAfterDocumentRebuild(object? sender, EventArgs e)
        => CompleteDocumentRenderedNotification(_renderGeneration);

    private void CompleteDocumentRenderedNotification(long generation)
    {
        if (!_hasPendingRenderedNotification || generation != _renderGeneration || Document is null)
        {
            return;
        }

        _hasPendingRenderedNotification = false;
        LayoutUpdated -= OnLayoutUpdatedAfterDocumentRebuild;
        DocumentRendered?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshForReadingPreferencesChange()
    {
        DocumentRenderInvalidated?.Invoke(this, EventArgs.Empty);
        _readingPreferencesRefreshCts?.Cancel();
        var cts = new CancellationTokenSource();
        _readingPreferencesRefreshCts = cts;

        _root.Opacity = 0.9;
        _ = AnimateReadingPreferencesRefreshAsync(cts.Token);
    }

    private async Task AnimateReadingPreferencesRefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(48, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        RebuildFromScratch();
        _root.Opacity = 1;
    }

    /// <summary>
    /// Полная пересборка без переиспользования. Нужна там, где изменился не сам
    /// документ, а способ его отрисовки (шрифты reading preferences, resolver
    /// изображений): содержимое блоков осталось прежним, но готовые контролы
    /// уже не соответствуют новым настройкам.
    /// </summary>
    private void RebuildFromScratch()
    {
        DisposeSelectionFragments();
        _root.Children.Clear();
        Rebuild();
    }

    private void ApplyDocumentPadding()
    {
        _viewport.Padding = DocumentPadding;
    }

    private void DisposeSelectionFragments()
    {
        foreach (var fragment in _selectionFragments)
        {
            fragment.Dispose();
        }

        _selectionFragments.Clear();
        _selectionFragmentPaths.Clear();
        _builtBlocks = [];
    }

    private Control BuildBlock(MarkdownBlock block, string path, bool nested, bool insideQuote = false)
    {
        var control = block switch
        {
            MarkdownHeadingBlock heading => BuildHeading(heading, path),
            MarkdownParagraphBlock paragraph => BuildParagraph(paragraph, path, nested, insideQuote),
            MarkdownQuoteBlock quote => BuildQuote(quote, path),
            MarkdownListBlock list => BuildList(list, path, insideQuote),
            MarkdownHorizontalRuleBlock => BuildHorizontalRule(),
            MarkdownCodeBlock code => BuildCodeBlock(code, path),
            MarkdownTableBlock table => BuildTable(table, path),
            MarkdownImageBlock image => BuildImageBlock(image),
            MarkdownDiagramBlock diagram => BuildDiagramBlock(diagram),
            MarkdownFootnotesBlock footnotes => BuildFootnotes(footnotes, path),
            _ => BuildFallback(block)
        };

        RegisterSourceLineAnchor(block, control);
        return control;
    }

    private void RegisterSourceLineAnchor(MarkdownBlock block, Control control)
    {
        if (block.SourceSpan is not { } sourceSpan)
        {
            return;
        }

        _sourceLineAnchors.Add(new MarkdownSourceLineVisualAnchor(control, sourceSpan));
    }

    private static MarkdownDiagramBlockView BuildDiagramBlock(MarkdownDiagramBlock block)
        => new(block);

    private MarkdownImageView BuildImageBlock(MarkdownImageBlock block)
        => new(
            resolver: ImageSourceResolver,
            url: block.Url,
            altText: block.AltText,
            title: block.Title,
            width: block.Width,
            height: block.Height,
            baseDirectory: Document?.BaseDirectory)
        {
            Margin = new Thickness(0, 12, 0, 22),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MaxWidth = 1200,
        };

    private Control BuildHeading(MarkdownHeadingBlock block, string path)
    {
        var fontSize = GetHeadingFontSize(block.Level);
        var lineHeight = Math.Max(fontSize * 1.25, fontSize + 4);
        var margin = block.Level == 1
            ? new Thickness(0, 0, 0, 10)
            : block.Level == 2
                ? new Thickness(0, 28, 0, 14)
                : new Thickness(0, 18, 0, 10);

        // Design: h1 -> 700, h2+ -> 600. Previous code had this inverted.
        var weight = block.Level == 1 ? FontWeight.Bold : FontWeight.SemiBold;

        // h5 / h6 render with the soft text colour in the design.
        var baseForeground = block.Level >= 5 ? LookupBrush("MmTextSoftBrush") : null;

        var headingControl = BuildSelectionFragment(
            path,
            block.Inlines,
            margin,
            fontSize,
            lineHeight,
            weight,
            FontStyle.Normal,
            fallbackClassName: "mm-md-heading",
            baseForeground: baseForeground);

        RegisterHeadingAnchor(block, headingControl);
        return headingControl;
    }

    private Control BuildParagraph(MarkdownParagraphBlock block, string path, bool nested, bool insideQuote)
    {
        var fontStyle = insideQuote ? FontStyle.Italic : FontStyle.Normal;
        var baseForeground = insideQuote ? LookupBrush("MmTextSoftBrush") : null;

        return BuildSelectionFragment(
            path,
            block.Inlines,
            nested ? new Thickness(0, 0, 0, 10) : new Thickness(0, 0, 0, 18),
            ReadingPreferences.FontSize,
            GetBodyLineHeight(),
            FontWeight.Normal,
            fontStyle,
            fallbackClassName: "mm-md-paragraph",
            baseForeground: baseForeground);
    }

    private Border BuildQuote(MarkdownQuoteBlock block, string path)
    {
        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 0
        };

        var border = new Border
        {
            Classes = { "mm-md-quote" },
            Child = stack
        };

        if (block.AlertKind is { } alertKind)
        {
            var kindClass = GetAlertClass(alertKind);
            border.Classes.Add("mm-md-alert");
            border.Classes.Add(kindClass);
            stack.Children.Add(BuildAlertHeader(alertKind, kindClass, path));
        }

        // Every descendant of a plain quote receives insideQuote: true so
        // nested lists and paragraphs pick up the italic/soft treatment.
        // The body of a GitHub alert is plain text, as on GitHub.
        var insideQuote = block.AlertKind is null;
        for (var index = 0; index < block.Blocks.Count; index++)
        {
            stack.Children.Add(BuildBlock(block.Blocks[index], $"{path}.b{index}", nested: true, insideQuote: insideQuote));
        }

        if (block.AlertKind is not null)
        {
            // Нижний отступ блока — расстояние до следующего блока. У последнего
            // блока alert следующего нет, и отступ оставлял бы под текстом пустое
            // место больше, чем над шапкой.
            RemoveTrailingBottomMargin(stack);
        }

        return border;
    }

    /// <summary>
    /// Убирает нижний отступ у контрола и у последнего контрола внутри него —
    /// абзаца в конце стека, содержимого последнего пункта списка или вложенной
    /// цитаты. Внутренний отступ вложенной цитаты остаётся: он симметричен
    /// верхнему, и полоса цитаты заканчивается чуть ниже текста. В блок кода и
    /// таблицу не заходим — отступы внутри них часть их собственной вёрстки.
    /// </summary>
    private static void RemoveTrailingBottomMargin(Control control)
    {
        var margin = control.Margin;
        control.Margin = new Thickness(margin.Left, margin.Top, margin.Right, 0);

        var last = control switch
        {
            StackPanel { Orientation: Orientation.Vertical, Children.Count: > 0 } stack => stack.Children[^1],
            Grid { RowDefinitions.Count: > 0 } list => list.Children.LastOrDefault(child => Grid.GetRow(child) == list.RowDefinitions.Count - 1),
            Border { Child: Control child } quote when quote.Classes.Contains("mm-md-quote") => child,
            _ => null
        };

        if (last is not null)
        {
            RemoveTrailingBottomMargin(last);
        }
    }

    /// <summary>
    /// Шапка GitHub alert: иконка и заголовок цвета вида. Заголовок — фрагмент
    /// текстового потока (выделяется, ищется и копируется вместе с alert), иконка —
    /// только украшение. Иконку и цвет полосы задают стили по классу вида
    /// (<c>Themes/Controls.axaml</c>).
    /// </summary>
    private StackPanel BuildAlertHeader(MarkdownAlertKind kind, string kindClass, string path)
    {
        var iconSize = Math.Round(ReadingPreferences.FontSize * AlertIconSizeToFontSize);
        var icon = new LucideIcon
        {
            Width = iconSize,
            Height = iconSize,
            VerticalAlignment = VerticalAlignment.Center,
            Classes = { "mm-md-alert-icon", kindClass }
        };

        var title = BuildSelectionFragment(
            $"{path}.a",
            [new MarkdownTextInline(_alertTitles.Get(kind))],
            margin: default,
            ReadingPreferences.FontSize,
            GetBodyLineHeight(),
            FontWeight.SemiBold,
            FontStyle.Normal,
            fallbackClassName: "mm-md-alert-title",
            textWrapping: TextWrapping.NoWrap,
            baseForegroundResourceKey: GetAlertBrushKey(kind));
        title.VerticalAlignment = VerticalAlignment.Center;

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = AlertIconTitleGap,
            Margin = new Thickness(0, 0, 0, AlertHeaderBottomMargin),
            Children = { icon, title }
        };
    }

    private static string GetAlertClass(MarkdownAlertKind kind) => kind switch
    {
        MarkdownAlertKind.Note => "mm-md-alert-note",
        MarkdownAlertKind.Tip => "mm-md-alert-tip",
        MarkdownAlertKind.Important => "mm-md-alert-important",
        MarkdownAlertKind.Warning => "mm-md-alert-warning",
        MarkdownAlertKind.Caution => "mm-md-alert-caution",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    private static string GetAlertBrushKey(MarkdownAlertKind kind) => kind switch
    {
        MarkdownAlertKind.Note => "MmAlertNoteBrush",
        MarkdownAlertKind.Tip => "MmAlertTipBrush",
        MarkdownAlertKind.Important => "MmAlertImportantBrush",
        MarkdownAlertKind.Warning => "MmAlertWarningBrush",
        MarkdownAlertKind.Caution => "MmAlertCautionBrush",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
        => RefreshAlertTitles();

    /// <summary>
    /// Смена языка меняет заголовки alert, а с ними и текстовый поток: документ
    /// с alert пересобирается. Смена языка шлёт несколько уведомлений подряд —
    /// пересборка случается на первом, остальные видят те же заголовки.
    /// </summary>
    private void RefreshAlertTitles()
    {
        var alertTitles = MarkdownAlertTitles.Create(GetLocalizedString);
        if (alertTitles.HasSameTitles(_alertTitles))
        {
            return;
        }

        if (HasAlertTitles(_textMap))
        {
            Rebuild();
            return;
        }

        _alertTitles = alertTitles;
    }

    private static bool HasAlertTitles(MarkdownDocumentTextMap textMap)
        => textMap.Fragments.Any(static fragment => fragment.Kind == MarkdownDocumentTextFragmentKind.AlertTitle);

    /// <summary>
    /// Список — одна сетка на все пункты: строка на пункт, общие колонки маркеров
    /// и колонка текста. Поэтому текст всех пунктов начинается с одной вертикали,
    /// даже если у одних «•», а у других чекбокс, или номера разной ширины
    /// («9.» и «10.»).
    /// </summary>
    /// <remarks>
    /// В маркированном списке колонка маркеров одна: «•» или чекбокс на его месте.
    /// В нумерованном номер остаётся и у пункта с чекбоксом, чтобы нумерация не
    /// прерывалась: номера выровнены вправо, а чекбокс стоит в начале строки
    /// пункта, в своей колонке — она появляется, только если в списке есть пункты
    /// task list. Текст обычного пункта начинается там же, где чекбоксы.
    /// </remarks>
    private Grid BuildList(MarkdownListBlock block, string path, bool insideQuote = false)
    {
        var grid = new Grid
        {
            RowSpacing = 8,
            Margin = new Thickness(0, 0, 0, 18)
        };

        var hasTasks = block.Items.Any(static item => item.IsChecked is not null);
        var hasCheckboxColumn = block.IsOrdered && hasTasks;
        var textIndent = hasTasks ? TaskListItemTextIndent : ListItemTextIndent;
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        if (hasCheckboxColumn)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        }

        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        var contentColumn = grid.ColumnDefinitions.Count - 1;

        for (var index = 0; index < block.Items.Count; index++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var item = block.Items[index];
            var itemPath = $"{path}.i{index}";
            AddListItemMarkers(grid, block, item, index, itemPath);

            if (hasCheckboxColumn && item.IsChecked is null)
            {
                // Текст обычного пункта встаёт туда же, где у соседей чекбокс.
                var content = BuildListItemContent(item, itemPath, insideQuote, TaskCheckboxIndentAfterNumber);
                AddToGrid(grid, content, index, column: 1);
                Grid.SetColumnSpan(content, 2);
            }
            else
            {
                AddToGrid(grid, BuildListItemContent(item, itemPath, insideQuote, textIndent), index, contentColumn);
            }
        }

        return grid;
    }

    private static void AddToGrid(Grid grid, Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }

    private void AddListItemMarkers(Grid grid, MarkdownListBlock list, MarkdownListItem item, int index, string path)
    {
        if (!list.IsOrdered)
        {
            // «•» прижат вправо: пробел после него в тексте маркера как раз
            // ставит точку по центру колонки, то есть под центр чекбоксов.
            var marker = item.IsChecked is { } isChecked
                ? BuildTaskCheckbox(isChecked, $"{path}.t")
                : BuildListMarkerText(list, index, path);
            marker.HorizontalAlignment = HorizontalAlignment.Right;
            AddToGrid(grid, marker, index, column: 0);
            return;
        }

        var number = BuildListMarkerText(list, index, path);
        number.HorizontalAlignment = HorizontalAlignment.Right;
        AddToGrid(grid, number, index, column: 0);

        if (item.IsChecked is { } isOrderedChecked)
        {
            var checkbox = BuildTaskCheckbox(isOrderedChecked, $"{path}.t");
            checkbox.Margin = new Thickness(TaskCheckboxIndentAfterNumber, 0, 0, 0);
            AddToGrid(grid, checkbox, index, column: 1);
        }
    }

    private StackPanel BuildListItemContent(MarkdownListItem item, string path, bool insideQuote, double indent)
    {
        var content = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 0,
            Margin = new Thickness(indent, 0, 0, 0)
        };

        for (var blockIndex = 0; blockIndex < item.Blocks.Count; blockIndex++)
        {
            content.Children.Add(BuildBlock(item.Blocks[blockIndex], $"{path}.b{blockIndex}", nested: true, insideQuote: insideQuote));
        }

        return content;
    }

    private Control BuildListMarkerText(MarkdownListBlock list, int index, string path)
    {
        var bullet = BuildSelectionFragment(
            $"{path}.m",
            [new MarkdownTextInline(MarkdownDocumentTextMap.GetListMarkerText(list, index))],
            margin: default,
            ReadingPreferences.FontSize,
            GetBodyLineHeight(),
            FontWeight.Normal,
            FontStyle.Normal,
            fallbackClassName: "mm-md-list-bullet",
            textWrapping: TextWrapping.NoWrap);

        bullet.VerticalAlignment = VerticalAlignment.Top;
        return bullet;
    }

    private MarkdownTaskCheckboxFragment BuildTaskCheckbox(bool isChecked, string path)
    {
        var checkbox = new MarkdownTaskCheckboxFragment(isChecked)
        {
            IconSize = Math.Round(ReadingPreferences.FontSize * TaskCheckboxSizeToFontSize),
            LineHeight = GetBodyLineHeight(),
            VerticalAlignment = VerticalAlignment.Top
        };

        if (_textMap.TryGetFragment(path, out var fragment))
        {
            checkbox.DocumentRange = fragment.Range;
            RegisterSelectionFragment(checkbox, path);
            checkbox.SelectionRange = new DocumentTextRange(SelectionStart, SelectionEnd);
        }

        return checkbox;
    }

    private static Grid BuildHorizontalRule()
    {
        // Design: 40% wide, horizontally centered. Avalonia has no percentage
        // widths, so we model it as a three-column grid in 3*,4*,3* ratio with
        // the rule in the middle column.
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("3*,4*,3*"),
            Margin = new Thickness(0, 32, 0, 32),
        };
        var line = new Border { Classes = { "mm-md-hr" } };
        Grid.SetColumn(line, 1);
        grid.Children.Add(line);
        return grid;
    }

    /// <summary>
    /// Блок сносок в конце документа: короткая черта слева, как в книге, и под ней
    /// сноски с номерами — как пункты нумерованного списка. Номер сноски — ссылка
    /// обратно к метке в тексте.
    /// </summary>
    private StackPanel BuildFootnotes(MarkdownFootnotesBlock block, string path)
    {
        var rule = new Border { Classes = { "mm-md-hr" } };
        var ruleRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,2*"),
            Margin = new Thickness(0, 14, 0, 16),
            Children = { rule }
        };

        var grid = new Grid { RowSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

        for (var index = 0; index < block.Footnotes.Count; index++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var footnote = block.Footnotes[index];
            var footnotePath = $"{path}.f{index}";
            AddToGrid(grid, BuildFootnoteMarker(footnote.Number, $"{footnotePath}.m"), index, column: 0);

            var content = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 0,
                Margin = new Thickness(ListItemTextIndent, 0, 0, 0)
            };

            for (var blockIndex = 0; blockIndex < footnote.Blocks.Count; blockIndex++)
            {
                content.Children.Add(BuildBlock(footnote.Blocks[blockIndex], $"{footnotePath}.b{blockIndex}", nested: true));
            }

            AddToGrid(grid, content, index, column: 1);
        }

        return new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 0,
            Margin = new Thickness(0, 0, 0, 18),
            Classes = { "mm-md-footnotes" },
            Children = { ruleRow, grid }
        };
    }

    private Control BuildFootnoteMarker(int number, string path)
    {
        var marker = BuildSelectionFragment(
            path,
            Array.Empty<MarkdownInline>(),
            margin: default,
            ReadingPreferences.FontSize,
            GetBodyLineHeight(),
            FontWeight.Normal,
            FontStyle.Normal,
            fallbackClassName: "mm-md-footnote-marker",
            textWrapping: TextWrapping.NoWrap,
            baseForegroundResourceKey: "MmAccentBrush",
            styledText: MarkdownStyledText.ForFootnoteMarker(number));

        marker.HorizontalAlignment = HorizontalAlignment.Right;
        marker.VerticalAlignment = VerticalAlignment.Top;
        return marker;
    }

    private Border BuildCodeBlock(MarkdownCodeBlock block, string path)
    {
        var body = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 8
        };

        TextBlock? infoLabel = null;
        if (!string.IsNullOrWhiteSpace(block.Info))
        {
            infoLabel = new TextBlock
            {
                Text = block.Info,
                UseLayoutRounding = true,
                Classes = { "mm-md-code-info" }
            };
            body.Children.Add(infoLabel);
        }

        var codeLineHeight = Math.Max(16, (ReadingPreferences.FontSize - 2) * 1.5);
        var codeFragment = BuildSelectionFragment(
            path,
            [new MarkdownTextInline(block.Code)],
            margin: default,
            fontSize: Math.Max(12, ReadingPreferences.FontSize - 2),
            lineHeight: codeLineHeight,
            fontWeight: FontWeight.Normal,
            fontStyle: FontStyle.Normal,
            fallbackClassName: "mm-md-codeblock-text",
            baseFontFamily: ResolveMonoFontFamily(),
            textWrapping: TextWrapping.NoWrap,
            baseFontFeatures: MarkdownTextRunPropertiesFactory.CodeFontFeatures);

        body.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = new Border
            {
                Padding = new Thickness(0, 0, 0, CodeBlockHorizontalScrollBarReserve),
                Child = codeFragment
            }
        });

        var contentGrid = new Grid();
        contentGrid.Children.Add(body);

        var copyButton = CreateCodeCopyButton(block.Code);
        copyButton.HorizontalAlignment = HorizontalAlignment.Right;
        copyButton.VerticalAlignment = VerticalAlignment.Top;
        contentGrid.Children.Add(copyButton);

        // Centre the icon on the block's first line: the info label when there
        // is one (its height comes from the font, so it is read after layout),
        // otherwise the first line of code.
        if (infoLabel is null)
        {
            AlignCodeCopyButton(copyButton, codeLineHeight);
        }
        else
        {
            infoLabel.SizeChanged += (_, e) => AlignCodeCopyButton(copyButton, e.NewSize.Height);
        }

        return new Border
        {
            Classes = { "mm-md-codeblock" },
            Child = contentGrid
        };
    }

    /// <summary>
    /// Puts the copy icon on the same line as the block's first line and mirrors
    /// the label's left inset on the right, measured to the icon's ink.
    /// </summary>
    private static void AlignCodeCopyButton(Button button, double firstLineHeight)
        => button.Margin = new Thickness(
            0,
            (firstLineHeight - CodeCopyButtonSize) / 2,
            -CodeCopyIconInkInset,
            0);

    private Button CreateCodeCopyButton(string code)
    {
        // The geometry, its colour and the copy -> check swap on .copied come
        // from the theme.
        var button = new Button
        {
            Classes = { "mm-code-copy-button" },
            Width = CodeCopyButtonSize,
            Height = CodeCopyButtonSize,
            Content = new LucideIcon
            {
                Width = CodeCopyIconSize,
                Height = CodeCopyIconSize
            },
            IsTabStop = true
        };

        var tooltip = GetLocalizedString("CodeCopyTooltip", "Copy code");
        var copiedName = GetLocalizedString("CodeCopiedStatus", "Code copied");
        ToolTip.SetTip(button, tooltip);
        AutomationProperties.SetName(button, tooltip);

        // The check mark is only visual; a screen reader hears the confirmation
        // because the name changes on a live element.
        AutomationProperties.SetLiveSetting(button, AutomationLiveSetting.Polite);

        void ShowCopied(bool copied)
        {
            button.Classes.Set("copied", copied);
            AutomationProperties.SetName(button, copied ? copiedName : tooltip);
        }

        IDisposable? pendingIconReset = null;
        button.Click += async (_, e) =>
        {
            if (await CopyTextToClipboardAsync(code).ConfigureAwait(true))
            {
                ShowCopied(true);
                pendingIconReset?.Dispose();
                pendingIconReset = DispatcherTimer.RunOnce(
                    () => ShowCopied(false),
                    CodeCopyConfirmationDuration);
            }

            e.Handled = true;
        };

        return button;
    }

    private Control BuildTable(MarkdownTableBlock table, string path)
    {
        var columnCount = Math.Max(
            table.Header.Count,
            table.Rows.Count == 0 ? 0 : table.Rows.Max(static row => row.Count));

        if (columnCount == 0)
        {
            return BuildFallback(table);
        }

        var panel = new MarkdownTablePanel(columnCount);

        // Design `.mm-table` switches to the sans stack at 0.92em of body.
        var sansFontFamily = LookupFontFamily("MmDocumentSansFontFamily");
        var bodyCellFontSize = ReadingPreferences.FontSize * 0.92;
        var headerCellFontSize = ReadingPreferences.FontSize * 0.85;

        if (table.Header.Count > 0)
        {
            AddTableRow(
                panel, table, table.Header,
                isHeader: true, isLastDataRow: false,
                pathPrefix: $"{path}.h",
                fontFamily: sansFontFamily,
                headerFontSize: headerCellFontSize,
                bodyFontSize: bodyCellFontSize);
        }

        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            // The last data row gets no bottom border so the table does not
            // end on a line.
            AddTableRow(
                panel, table, table.Rows[rowIndex],
                isHeader: false, isLastDataRow: rowIndex == table.Rows.Count - 1,
                pathPrefix: $"{path}.r{rowIndex}.c",
                fontFamily: sansFontFamily,
                headerFontSize: headerCellFontSize,
                bodyFontSize: bodyCellFontSize);
        }

        return new Border
        {
            Classes = { "mm-md-table" },
            Child = new MarkdownTableHost(panel, TableHorizontalScrollBarReserve),
            // Design `.mm-table` margin is 1.4em top and bottom.
            Margin = new Thickness(0, (int)(ReadingPreferences.FontSize * 1.4), 0, (int)(ReadingPreferences.FontSize * 1.4))
        };
    }

    private void AddTableRow(
        MarkdownTablePanel panel,
        MarkdownTableBlock table,
        IReadOnlyList<MarkdownTableCell> cells,
        bool isHeader,
        bool isLastDataRow,
        string pathPrefix,
        FontFamily fontFamily,
        double headerFontSize,
        double bodyFontSize)
    {
        for (var columnIndex = 0; columnIndex < panel.ColumnCount; columnIndex++)
        {
            var cell = columnIndex < cells.Count
                ? cells[columnIndex]
                : new MarkdownTableCell(Array.Empty<MarkdownInline>());
            var textAlignment = table.GetColumnAlignment(columnIndex) switch
            {
                MarkdownTableColumnAlignment.Center => TextAlignment.Center,
                MarkdownTableColumnAlignment.Right => TextAlignment.Right,
                _ => TextAlignment.Left
            };

            Control content;
            if (isHeader)
            {
                // Design: 0.85em size, semibold, soft colour, 0.05em letter-spacing.
                // Note: design also specifies "text-transform: uppercase", which
                // Avalonia does not support without mutating the characters
                // themselves (and breaking copy semantics). We therefore keep
                // the original case and approximate the visual weight via
                // letter-spacing + soft colour + smaller size.
                content = BuildSelectionFragment(
                    $"{pathPrefix}{columnIndex}",
                    cell.Inlines,
                    margin: default,
                    fontSize: headerFontSize,
                    lineHeight: Math.Max(headerFontSize * 1.45, headerFontSize + 4),
                    fontWeight: FontWeight.SemiBold,
                    fontStyle: FontStyle.Normal,
                    fallbackClassName: "mm-md-table-header",
                    baseFontFamily: fontFamily,
                    textWrapping: TextWrapping.NoWrap,
                    baseForeground: LookupBrush("MmTextSoftBrush"),
                    letterSpacing: headerFontSize * 0.05,
                    textAlignment: textAlignment);
            }
            else
            {
                content = BuildSelectionFragment(
                    $"{pathPrefix}{columnIndex}",
                    cell.Inlines,
                    margin: default,
                    fontSize: bodyFontSize,
                    lineHeight: Math.Max(bodyFontSize * 1.55, bodyFontSize + 4),
                    fontWeight: FontWeight.Normal,
                    fontStyle: FontStyle.Normal,
                    fallbackClassName: "mm-md-table-text",
                    baseFontFamily: fontFamily,
                    textWrapping: TextWrapping.NoWrap,
                    textAlignment: textAlignment);
            }

            var border = new Border
            {
                Classes = { isHeader ? "mm-md-table-header-cell" : "mm-md-table-cell" },
                Child = content
            };
            MarkdownTablePanel.SetIsShrinkable(border, content is MarkdownImageFlowFragment);

            if (!isHeader && isLastDataRow)
            {
                // Suppresses the border-bottom on the final row so the table
                // does not terminate on an orphan divider line.
                border.Classes.Add("mm-md-table-cell-last");
            }

            panel.Children.Add(border);
        }
    }

    private TextBlock BuildFallback(MarkdownBlock block)
    {
        return new TextBlock
        {
            Text = MarkdownDocumentTextMap.ExtractPlainText(block),
            Classes = { "mm-md-paragraph" },
            FontFamily = ResolveBodyFontFamily(),
            FontSize = ReadingPreferences.FontSize,
            LineHeight = GetBodyLineHeight(),
            TextWrapping = TextWrapping.Wrap,
            UseLayoutRounding = true
        };
    }

    private Control BuildSelectionFragment(
        string path,
        IReadOnlyList<MarkdownInline> inlines,
        Thickness margin,
        double fontSize,
        double lineHeight,
        FontWeight fontWeight,
        FontStyle fontStyle,
        string fallbackClassName,
        FontFamily? baseFontFamily = null,
        TextWrapping textWrapping = TextWrapping.Wrap,
        IBrush? baseForeground = null,
        double letterSpacing = 0,
        TextAlignment textAlignment = TextAlignment.Left,
        string? baseForegroundResourceKey = null,
        MarkdownStyledText? styledText = null,
        FontFeatureCollection? baseFontFeatures = null)
    {
        // Готовый текст — у служебных фрагментов, которых нет среди inline документа
        // (номер сноски со ссылкой обратно к метке).
        var styled = styledText ?? MarkdownStyledText.FromInlines(inlines);
        if (styled.Text.Length == 0)
        {
            return new Border
            {
                Height = 0,
                Margin = margin
            };
        }

        var resolvedFontFamily = baseFontFamily ?? ResolveBodyFontFamily();
        if (!_textMap.TryGetFragment(path, out var fragment))
        {
            var fallback = new TextBlock
            {
                Text = styled.Text,
                Margin = margin,
                FontFamily = resolvedFontFamily,
                FontSize = fontSize,
                FontWeight = fontWeight,
                FontStyle = fontStyle,
                LineHeight = lineHeight,
                LetterSpacing = letterSpacing,
                TextWrapping = textWrapping,
                TextAlignment = textAlignment,
                UseLayoutRounding = true,
                Classes = { fallbackClassName }
            };

            if (baseForeground is not null)
            {
                fallback.Foreground = baseForeground;
            }

            if (baseFontFeatures is not null)
            {
                fallback.FontFeatures = baseFontFeatures;
            }

            return fallback;
        }

        if (MarkdownImageFlowFragment.TryCreate(inlines, out var imageItems))
        {
            var imageFlow = new MarkdownImageFlowFragment(imageItems)
            {
                Margin = margin,
                // The flow is as wide as its images, so it is aligned as a whole.
                HorizontalAlignment = textAlignment switch
                {
                    TextAlignment.Center => HorizontalAlignment.Center,
                    TextAlignment.Right => HorizontalAlignment.Right,
                    _ => HorizontalAlignment.Stretch
                },
                DocumentRange = fragment.Range,
                ImageSourceResolver = ImageSourceResolver,
                BaseDirectory = Document?.BaseDirectory,
                BaseFontFamily = resolvedFontFamily,
                BaseFontSize = fontSize,
                BaseLineHeight = lineHeight
            };

            imageFlow.Classes.Add(fallbackClassName);
            RegisterSelectionFragment(imageFlow, path);
            imageFlow.SelectionRange = new DocumentTextRange(SelectionStart, SelectionEnd);
            return imageFlow;
        }

        var control = new MarkdownSelectionTextFragment
        {
            Margin = margin,
            StyledText = styled,
            DocumentRange = fragment.Range,
            BaseFontFamily = resolvedFontFamily,
            BaseFontFeatures = baseFontFeatures,
            BaseFontSize = fontSize,
            BaseFontWeight = fontWeight,
            BaseFontStyle = fontStyle,
            BaseLineHeight = lineHeight,
            BaseForeground = baseForeground,
            BaseForegroundResourceKey = baseForegroundResourceKey,
            BaseLetterSpacing = letterSpacing,
            LayoutTextWrapping = textWrapping,
            LayoutTextAlignment = textAlignment,
            ImageSourceResolver = ImageSourceResolver,
            BaseDirectory = Document?.BaseDirectory,
            Cursor = TryCreateCursor(StandardCursorType.Ibeam)
        };

        control.Classes.Add(fallbackClassName);
        RegisterSelectionFragment(control, path);
        control.SelectionRange = new DocumentTextRange(SelectionStart, SelectionEnd);
        return control;
    }

    private void RegisterSelectionFragment(MarkdownDocumentSelectionFragmentBase fragment, string path)
    {
        _selectionFragments.Add(fragment);
        _selectionFragmentPaths.Add(path);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsPointerInputFromScrollBarChrome(e.Source))
        {
            return;
        }

        if (IsPointerInputFromCodeCopyButton(e.Source))
        {
            return;
        }

        var currentPoint = e.GetCurrentPoint(this);
        if (currentPoint.Properties.IsRightButtonPressed)
        {
            // У перехода по сноске нет адреса, который можно скопировать.
            _contextMenuLink = TryResolveLinkAtDocumentPoint(e.GetPosition(this), out var link) && link.Footnote is null
                ? link
                : null;
            return;
        }

        if (!TryResolveFragment(e.GetPosition(this), out var fragment, out var localPosition))
        {
            return;
        }

        if (!currentPoint.Properties.IsLeftButtonPressed)
        {
            return;
        }

        // Focus via NavigationMethod.Pointer so the act of starting a selection
        // does not raise RequestBringIntoView and make the ScrollViewer jump.
        Focus(NavigationMethod.Pointer);

        if (e.ClickCount >= 3)
        {
            CommitSelection(fragment.DocumentRange, preserveOnRelease: true);
            BeginPointerSession(e, fragment, localPosition, allowLinkActivation: false);
            e.Handled = true;
            return;
        }

        if (e.ClickCount == 2)
        {
            var wordRange = fragment.GetDocumentWordRange(localPosition);
            if (!wordRange.IsEmpty)
            {
                CommitSelection(wordRange, preserveOnRelease: true);
                BeginPointerSession(e, fragment, localPosition, allowLinkActivation: false);
                e.Handled = true;
                return;
            }
        }

        _isPointerPressed = true;
        _isDraggingSelection = false;
        _preserveSelectionOnRelease = false;
        _pointerPressOrigin = e.GetPosition(this);
        _pressedFragment = fragment;
        _pressedLink = fragment.TryGetLinkAt(localPosition, out var pressedLink)
            ? pressedLink
            : null;

        var anchor = fragment.GetDocumentOffset(localPosition);
        SelectionAnchor = anchor;
        SelectionStart = anchor;
        SelectionEnd = anchor;
        ApplySelectionToFragments();

        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isPointerPressed || SelectionAnchor is null)
        {
            return;
        }

        var position = e.GetPosition(this);
        if (!_isDraggingSelection && Point.Distance(position, _pointerPressOrigin) < DragSelectionThreshold)
        {
            return;
        }

        _isDraggingSelection = true;
        var offset = ResolveDocumentOffset(position);
        SetSelection(SelectionAnchor.Value, offset);
        e.Handled = true;
    }

    private async void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isPointerPressed)
        {
            return;
        }

        await TryActivatePressedLinkAsync(e);

        if (!_isDraggingSelection && !_preserveSelectionOnRelease)
        {
            ClearSelection();
        }

        ResetPointerState();
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        ResetPointerState();
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && HasSelection)
        {
            ClearSelection();
            e.Handled = true;
            return;
        }

        if (!HasCommandModifier(e.KeyModifiers))
        {
            return;
        }

        switch (e.Key)
        {
            case Key.A:
                SelectAll();
                e.Handled = true;
                break;

            case Key.C:
                if (HasSelection)
                {
                    await CopySelectionToClipboardAsync();
                    e.Handled = true;
                }
                break;
        }
    }

    private void SetSelection(int firstOffset, int secondOffset)
    {
        var range = DocumentTextRange.FromBounds(firstOffset, secondOffset);
        SelectionStart = range.Start;
        SelectionEnd = range.End;
        ApplySelectionToFragments();
    }

    private void ApplySelectionToFragments()
    {
        var range = new DocumentTextRange(SelectionStart, SelectionEnd);
        foreach (var fragment in _selectionFragments)
        {
            fragment.SelectionRange = range;
        }
    }

    private async Task CopySelectionToClipboardAsync()
    {
        await CopyTextToClipboardAsync(SelectedText).ConfigureAwait(true);
    }

    /// <returns><see langword="true"/> when the text reached the clipboard.</returns>
    private async Task<bool> CopyTextToClipboardAsync(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            return false;
        }

        try
        {
            await ClipboardExtensions.SetTextAsync(
                clipboard,
                text.Replace("\n", Environment.NewLine, StringComparison.Ordinal));
            return true;
        }
        catch (Exception exception)
        {
            // Another process can hold the OS clipboard (on Windows Avalonia gives
            // up with a TimeoutException). Every caller is an async void UI
            // handler, so letting this through would take the app down.
            Logger.TryGet(LogEventLevel.Warning, LogArea.Control)?.Log(
                this,
                "Could not copy text to the clipboard: {Exception}",
                exception);
            return false;
        }
    }

    private ContextMenu BuildContextMenu()
    {
        _copyMenuItem = new MenuItem
        {
            Header = GetLocalizedString("ContextCopy", "Copy"),
            InputGesture = new KeyGesture(Key.C, KeyModifiers.Control)
        };
        _copyMenuItem.Click += OnCopyMenuItemClick;

        _copyLinkMenuItem = new MenuItem
        {
            Header = GetLocalizedString("ContextCopyLink", "Copy link")
        };
        _copyLinkMenuItem.Click += OnCopyLinkMenuItemClick;

        _copyTelegramMarkdownMenuItem = new MenuItem
        {
            Header = GetLocalizedString("ContextCopyTelegramMarkdown", "Copy selection as Telegram Markdown")
        };
        _copyTelegramMarkdownMenuItem.Click += OnCopyTelegramMarkdownMenuItemClick;

        _selectAllMenuItem = new MenuItem
        {
            Header = GetLocalizedString("ContextSelectAll", "Select all"),
            InputGesture = new KeyGesture(Key.A, KeyModifiers.Control)
        };
        _selectAllMenuItem.Click += OnSelectAllMenuItemClick;

        var menu = new ContextMenu();
        menu.Items.Add(_copyMenuItem);
        menu.Items.Add(_copyLinkMenuItem);
        menu.Items.Add(_copyTelegramMarkdownMenuItem);
        menu.Items.Add(_selectAllMenuItem);
        menu.Opening += OnContextMenuOpening;
        menu.Closed += (_, _) =>
        {
            _contextMenuLink = null;
            _contextMenuSelectedLinkUrls = Array.Empty<string>();
        };
        return menu;
    }

    private void OnContextMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _contextMenuSelectedLinkUrls = GetSelectedLinkUrls();
        UpdateContextMenuHeaders();

        // Enable Copy only when there is a selection.
        // Enable Select All only when there is any text to select.
        if (_copyMenuItem is not null)
        {
            _copyMenuItem.IsEnabled = HasSelection;
        }

        if (_copyLinkMenuItem is not null)
        {
            _copyLinkMenuItem.IsEnabled = _contextMenuSelectedLinkUrls.Count > 0 || _contextMenuLink.HasValue;
        }

        if (_copyTelegramMarkdownMenuItem is not null)
        {
            _copyTelegramMarkdownMenuItem.IsEnabled = HasSelection && Document is { Blocks.Count: > 0 };
        }

        if (_selectAllMenuItem is not null)
        {
            _selectAllMenuItem.IsEnabled = _textMap.Text.Length > 0;
        }
    }

    private void UpdateContextMenuHeaders()
    {
        if (_copyMenuItem is not null)
        {
            _copyMenuItem.Header = GetLocalizedString("ContextCopy", "Copy");
        }

        if (_copyLinkMenuItem is not null)
        {
            _copyLinkMenuItem.Header = _contextMenuSelectedLinkUrls.Count > 1
                ? GetLocalizedString("ContextCopyLinks", "Copy links")
                : GetLocalizedString("ContextCopyLink", "Copy link");
        }

        if (_copyTelegramMarkdownMenuItem is not null)
        {
            _copyTelegramMarkdownMenuItem.Header = GetLocalizedString(
                "ContextCopyTelegramMarkdown",
                "Copy selection as Telegram Markdown");
        }

        if (_selectAllMenuItem is not null)
        {
            _selectAllMenuItem.Header = GetLocalizedString("ContextSelectAll", "Select all");
        }
    }

    private async void OnCopyMenuItemClick(object? sender, RoutedEventArgs e)
    {
        if (!HasSelection)
        {
            return;
        }

        await CopySelectionToClipboardAsync();
    }

    private void OnSelectAllMenuItemClick(object? sender, RoutedEventArgs e)
    {
        Focus(NavigationMethod.Pointer);
        SelectAll();
    }

    private async void OnCopyLinkMenuItemClick(object? sender, RoutedEventArgs e)
    {
        if (_contextMenuSelectedLinkUrls.Count > 1)
        {
            await CopyTextToClipboardAsync(string.Join("\n", _contextMenuSelectedLinkUrls)).ConfigureAwait(true);
            return;
        }

        if (_contextMenuLink is { } link)
        {
            await CopyTextToClipboardAsync(link.Url).ConfigureAwait(true);
            return;
        }

        if (_contextMenuSelectedLinkUrls.Count == 1)
        {
            await CopyTextToClipboardAsync(_contextMenuSelectedLinkUrls[0]).ConfigureAwait(true);
        }
    }

    private IReadOnlyList<string> GetSelectedLinkUrls()
    {
        if (!HasSelection || Document is not { Blocks.Count: > 0 } document)
        {
            return Array.Empty<string>();
        }

        return TelegramMarkdownFormatter.GetSelectionLinkUrls(
            document,
            new DocumentTextRange(SelectionStart, SelectionEnd),
            _alertTitles.Get);
    }

    private async void OnCopyTelegramMarkdownMenuItemClick(object? sender, RoutedEventArgs e)
    {
        if (!HasSelection || Document is not { Blocks.Count: > 0 } document)
        {
            return;
        }

        var selectionRange = new DocumentTextRange(SelectionStart, SelectionEnd);
        var markdown = TelegramMarkdownFormatter.FormatSelection(document, selectionRange, _alertTitles.Get);
        var html = TelegramMarkdownFormatter.FormatSelectionHtml(document, selectionRange, _alertTitles.Get);
        await CopyTelegramMarkdownToClipboardAsync(markdown, html).ConfigureAwait(true);
    }

    private async Task CopyTelegramMarkdownToClipboardAsync(string markdown, string htmlFragment)
    {
        if (string.IsNullOrEmpty(markdown) && string.IsNullOrEmpty(htmlFragment))
        {
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            return;
        }

        var item = new DataTransferItem();
        if (!string.IsNullOrEmpty(markdown))
        {
            item.SetText(markdown.Replace("\n", Environment.NewLine, StringComparison.Ordinal));
        }

        if (!string.IsNullOrEmpty(htmlFragment))
        {
            item.Set(HtmlClipboardFormat, Encoding.UTF8.GetBytes(CreateHtmlClipboardDocument(htmlFragment)));
            item.Set(WindowsHtmlClipboardFormat, CreateWindowsHtmlClipboardPayload(htmlFragment));
        }

        var dataTransfer = new DataTransfer();
        dataTransfer.Add(item);
        await clipboard.SetDataAsync(dataTransfer).ConfigureAwait(true);
        await clipboard.FlushAsync().ConfigureAwait(true);
    }

    private static string CreateHtmlClipboardDocument(string htmlFragment)
        => "<!DOCTYPE html><html><head><meta charset=\"utf-8\"></head><body>" + htmlFragment + "</body></html>";

    private static byte[] CreateWindowsHtmlClipboardPayload(string htmlFragment)
    {
        const string startFragmentMarker = "<!--StartFragment-->";
        const string endFragmentMarker = "<!--EndFragment-->";
        const string headerTemplate = "Version:0.9\r\nStartHTML:0000000000\r\nEndHTML:0000000000\r\nStartFragment:0000000000\r\nEndFragment:0000000000\r\n";

        var htmlPrefix = "<!DOCTYPE html><html><head><meta charset=\"utf-8\"></head><body>" + startFragmentMarker;
        var htmlSuffix = endFragmentMarker + "</body></html>";
        var html = htmlPrefix + htmlFragment + htmlSuffix;

        var startHtml = Encoding.UTF8.GetByteCount(headerTemplate);
        var startFragment = startHtml + Encoding.UTF8.GetByteCount(htmlPrefix);
        var endFragment = startFragment + Encoding.UTF8.GetByteCount(htmlFragment);
        var endHtml = startHtml + Encoding.UTF8.GetByteCount(html);

        var header = string.Create(
            CultureInfo.InvariantCulture,
            $"Version:0.9\r\nStartHTML:{startHtml:D10}\r\nEndHTML:{endHtml:D10}\r\nStartFragment:{startFragment:D10}\r\nEndFragment:{endFragment:D10}\r\n");

        return Encoding.UTF8.GetBytes(header + html);
    }

    private async Task TryActivatePressedLinkAsync(PointerReleasedEventArgs e)
    {
        if (_pressedFragment is null)
        {
            return;
        }

        var releasePosition = e.GetPosition(_pressedFragment);
        MarkdownLinkSpan? releasedLink = _pressedFragment.TryGetLinkAt(releasePosition, out var hitLink)
            ? hitLink
            : null;

        if (!MarkdownLinkActivationPolicy.CanActivateLink(
                _isDraggingSelection,
                SelectionAnchor,
                SelectionStart,
                SelectionEnd,
                _pressedLink,
                releasedLink))
        {
            return;
        }

        var pressedLink = _pressedLink!.Value;

        if (pressedLink.Footnote is { } footnote)
        {
            NavigateFootnote(_pressedFragment, pressedLink, footnote);
            return;
        }

        if (TryScrollToHeadingAnchor(pressedLink.Url))
        {
            return;
        }

        if (MarkdownLocalFileLinkResolver.TryResolve(pressedLink.Url, Document?.BaseDirectory, out var targetPath))
        {
            MarkdownFileLinkRequested?.Invoke(
                this,
                new MarkdownFileLinkRequestedEventArgs(pressedLink.Url, targetPath));
            return;
        }

        if (!Uri.TryCreate(pressedLink.Url, UriKind.Absolute, out var uri))
        {
            return;
        }

        var launcher = TopLevel.GetTopLevel(this)?.Launcher;
        if (launcher is null)
        {
            return;
        }

        await launcher.LaunchUriAsync(uri);
    }

    private void RegisterHeadingAnchor(MarkdownHeadingBlock block, Control headingControl)
        => _headingAnchorRegistrations.Add((block, headingControl));

    internal bool HasHeadingAnchor(string linkTarget)
        => MarkdownHeadingAnchorSlugger.TryNormalizeFragment(linkTarget, out var anchor)
            && _headingAnchorTargets.ContainsKey(anchor);

    private bool TryScrollToHeadingAnchor(string linkTarget)
    {
        if (!MarkdownHeadingAnchorSlugger.TryNormalizeFragment(linkTarget, out var anchor)
            || !_headingAnchorTargets.TryGetValue(anchor, out var target))
        {
            return false;
        }

        return TryScrollTargetIntoView(target);
    }

    /// <summary>
    /// Переход по сноске, как по якорю заголовка: метка в тексте прокручивает к
    /// сноске, номер сноски — обратно к метке, с которой пришли, а если пришли не по
    /// метке — к первой ссылке на эту сноску.
    /// </summary>
    private void NavigateFootnote(
        MarkdownDocumentSelectionFragmentBase source,
        MarkdownLinkSpan link,
        MarkdownFootnoteLinkTarget footnote)
    {
        if (footnote.IsBackReference)
        {
            TryScrollToFootnoteReference(footnote.Number);
            return;
        }

        if (TryScrollToFootnote(footnote.Number))
        {
            _footnoteReturnTarget = source is MarkdownSelectionTextFragment fragment
                ? (fragment, link)
                : null;
        }
    }

    private bool TryScrollToFootnote(int number)
        => TryFindFootnoteLink(new MarkdownFootnoteLinkTarget(number, IsBackReference: true), out var marker, out _)
            && TryScrollTargetIntoView(marker);

    private bool TryScrollToFootnoteReference(int number)
    {
        if (_footnoteReturnTarget is { } returnTarget
            && returnTarget.Link.Footnote?.Number == number)
        {
            return TryScrollTextIntoView(returnTarget.Fragment, returnTarget.Link.Range.Start);
        }

        return TryFindFootnoteLink(new MarkdownFootnoteLinkTarget(number, IsBackReference: false), out var fragment, out var reference)
            && TryScrollTextIntoView(fragment, reference.Range.Start);
    }

    /// <summary>
    /// Первая в документе ссылка сноски — метка в тексте или номер в блоке сносок.
    /// Ищется в момент перехода, поэтому ничего не стоит при построении документа.
    /// </summary>
    private bool TryFindFootnoteLink(
        MarkdownFootnoteLinkTarget target,
        [NotNullWhen(true)] out MarkdownSelectionTextFragment? fragment,
        out MarkdownLinkSpan link)
    {
        foreach (var candidate in _selectionFragments)
        {
            if (candidate is not MarkdownSelectionTextFragment textFragment)
            {
                continue;
            }

            foreach (var candidateLink in textFragment.StyledText.Links)
            {
                if (candidateLink.Footnote == target)
                {
                    fragment = textFragment;
                    link = candidateLink;
                    return true;
                }
            }
        }

        fragment = null;
        link = default;
        return false;
    }

    private bool TryScrollTextIntoView(MarkdownSelectionTextFragment fragment, int localOffset)
        => TryScrollTargetIntoView(
            fragment,
            fragment.TryGetLineTopForLocalOffset(localOffset, out var lineTop) ? lineTop : 0);

    private bool TryScrollTargetIntoView(Control target, double targetOffsetY = 0)
    {
        var scrollViewer = this.FindAncestorOfType<ScrollViewer>();
        if (scrollViewer is null)
        {
            return false;
        }

        var targetPoint = target.TranslatePoint(new Point(0, targetOffsetY), scrollViewer);
        if (targetPoint is null)
        {
            return false;
        }

        const double topInset = 24;
        var nextOffsetY = Math.Clamp(
            scrollViewer.Offset.Y + targetPoint.Value.Y - topInset,
            0,
            scrollViewer.ScrollBarMaximum.Y);

        scrollViewer.Offset = new Vector(scrollViewer.Offset.X, nextOffsetY);
        return true;
    }

    private void CommitSelection(DocumentTextRange range, bool preserveOnRelease)
    {
        if (range.IsEmpty)
        {
            ClearSelection();
            return;
        }

        SelectionAnchor = range.Start;
        SelectionStart = range.Start;
        SelectionEnd = range.End;
        _preserveSelectionOnRelease = preserveOnRelease;
        ApplySelectionToFragments();
    }

    private void BeginPointerSession(
        PointerPressedEventArgs e,
        MarkdownDocumentSelectionFragmentBase fragment,
        Point localPosition,
        bool allowLinkActivation)
    {
        _isPointerPressed = true;
        _isDraggingSelection = false;
        _pointerPressOrigin = e.GetPosition(this);
        _pressedFragment = fragment;
        _pressedLink = allowLinkActivation && fragment.TryGetLinkAt(localPosition, out var pressedLink)
            ? pressedLink
            : null;
        e.Pointer.Capture(this);
    }

    private int ResolveDocumentOffset(Point position)
    {
        if (!TryResolveFragment(position, out var fragment, out var localPoint))
        {
            return 0;
        }

        return fragment.GetDocumentOffset(localPoint);
    }

    private bool TryResolveFragment(
        Point position,
        out MarkdownDocumentSelectionFragmentBase fragment,
        out Point localPoint)
    {
        fragment = null!;
        localPoint = default;

        if (_selectionFragments.Count == 0)
        {
            return false;
        }

        var fragments = new List<MarkdownDocumentSelectionFragmentBase>(_selectionFragments.Count);
        var candidates = new List<MarkdownFragmentHitTestCandidate>(_selectionFragments.Count);

        foreach (var candidateFragment in _selectionFragments)
        {
            var translated = this.TranslatePoint(position, candidateFragment);
            if (translated is null)
            {
                continue;
            }

            fragments.Add(candidateFragment);
            candidates.Add(new MarkdownFragmentHitTestCandidate(
                new Rect(0, 0, Math.Max(candidateFragment.Bounds.Width, 1), Math.Max(candidateFragment.Bounds.Height, 1)),
                translated.Value));
        }

        var bestIndex = MarkdownFragmentHitTester.FindBestIndex(candidates);
        if (bestIndex < 0)
        {
            return false;
        }

        fragment = fragments[bestIndex];
        localPoint = ClampPointToFragment(fragment, candidates[bestIndex].LocalPoint);
        return true;
    }

    private bool TryResolveLinkAtDocumentPoint(Point documentPoint, out MarkdownLinkSpan link)
    {
        link = default;
        if (!TryResolveFragment(documentPoint, out var fragment, out var localPoint))
        {
            return false;
        }

        return fragment.TryGetLinkAt(localPoint, out link);
    }

    private static Point ClampPointToFragment(MarkdownDocumentSelectionFragmentBase fragment, Point point)
    {
        var width = Math.Max(fragment.Bounds.Width, 1);
        var height = Math.Max(fragment.Bounds.Height, 1);
        return new Point(
            Math.Clamp(point.X, 0, width),
            Math.Clamp(point.Y, 0, height - 1));
    }

    internal static bool IsPointerInputFromScrollBarChrome(object? source)
    {
        if (source is not Control control)
        {
            return false;
        }

        return control is ScrollBar || control.FindAncestorOfType<ScrollBar>() is not null;
    }

    private static bool IsPointerInputFromCodeCopyButton(object? source)
    {
        if (source is not Control control)
        {
            return false;
        }

        return control.Classes.Contains("mm-code-copy-button")
            || control.FindAncestorOfType<Button>()?.Classes.Contains("mm-code-copy-button") == true;
    }

    private static bool HasCommandModifier(KeyModifiers modifiers)
        => modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta);

    private static Cursor? TryCreateCursor(StandardCursorType cursorType)
    {
        try
        {
            return new Cursor(cursorType);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private void ResetPointerState()
    {
        _isPointerPressed = false;
        _isDraggingSelection = false;
        _preserveSelectionOnRelease = false;
        _pointerPressOrigin = default;
        _pressedFragment = null;
        _pressedLink = null;
    }

    private FontFamily ResolveBodyFontFamily() => ReadingPreferences.FontFamily switch
    {
        FontFamilyMode.Sans => LookupFontFamily("MmDocumentSansFontFamily"),
        FontFamilyMode.Mono => LookupFontFamily("MmDocumentMonoFontFamily"),
        _ => LookupFontFamily("MmDocumentSerifFontFamily")
    };

    private FontFamily ResolveSansFontFamily() => LookupFontFamily("MmDocumentSansFontFamily");

    private FontFamily ResolveMonoFontFamily() => LookupFontFamily("MmDocumentMonoFontFamily");

    private FontFamily LookupFontFamily(string resourceKey)
    {
        if (this.TryFindResource(resourceKey, ActualThemeVariant, out var value) && value is FontFamily family)
        {
            return family;
        }

        // Fallbacks mirror the minimal tail of the stacks in Themes/Typography.axaml
        // so that rendering stays sensible if the ResourceDictionary is not yet attached.
        return resourceKey switch
        {
            "MmDocumentSerifFontFamily" => new FontFamily("Georgia, Cambria, serif"),
            "MmDocumentSansFontFamily" => new FontFamily("Segoe UI, system-ui, sans-serif"),
            "MmDocumentMonoFontFamily" => new FontFamily("Consolas, Menlo, monospace"),
            _ => FontFamily.Default
        };
    }

    private double GetBodyLineHeight() => Math.Max(ReadingPreferences.FontSize * ReadingPreferences.LineHeight, ReadingPreferences.FontSize + 4);

    private double GetHeadingFontSize(int level)
    {
        var baseSize = ReadingPreferences.FontSize;
        return level switch
        {
            1 => baseSize * 2.1,
            2 => baseSize * 1.5,
            3 => baseSize * 1.2,
            4 => baseSize * 1.05,
            5 => baseSize * 0.95,
            6 => baseSize * 0.95,
            _ => baseSize
        };
    }

    private IBrush? LookupBrush(string resourceKey)
        => this.TryFindResource(resourceKey, ActualThemeVariant, out var value) && value is IBrush brush
            ? brush
            : null;

    private static ILocalizationService? TryGetLocalization()
        => Avalonia.Application.Current?.Resources.TryGetResource("Localization", null, out var resource) == true
            ? resource as ILocalizationService
            : null;

    private static string GetLocalizedString(string key, string fallback)
    {
        if (TryGetLocalization() is { } localization)
        {
            var value = localization[key];
            return string.IsNullOrWhiteSpace(value) || value.StartsWith("[[", StringComparison.Ordinal)
                ? fallback
                : value;
        }

        return fallback;
    }
}

internal readonly record struct MarkdownSourceLineVisualAnchor(Control Control, MarkdownSourceSpan SourceSpan);

/// <summary>
/// Source-line anchor stored relative to its top-level block's start line, so a
/// reused block can be re-anchored after an edit shifted it up or down.
/// </summary>
internal readonly record struct BuiltSourceAnchor(Control Control, int RelativeStartLine, int RelativeEndLine);

/// <summary>
/// Everything one top-level block contributed to the last render, kept so the
/// block can be re-adopted verbatim when the next parse produces an equal block.
/// </summary>
internal sealed class BuiltTopLevelBlock
{
    public required MarkdownBlock Block { get; set; }

    public required Control Control { get; init; }

    public required MarkdownDocumentSelectionFragmentBase[] Fragments { get; init; }

    /// <summary>Fragment paths with the leading <c>b{index}</c> segment removed.</summary>
    public required string[] FragmentRelativePaths { get; init; }

    public required BuiltSourceAnchor[] SourceAnchors { get; init; }

    public required (MarkdownHeadingBlock Block, Control Control)[] HeadingAnchors { get; init; }
}

internal readonly record struct MarkdownSourceLineAnchorSnapshot(int StartLine, int EndLine, double Y);

/// <summary>
/// One knot of the monotone source-line ↔ document-offset map used by edit-mode
/// scroll synchronization.
/// </summary>
internal readonly record struct MarkdownSourcePositionPoint(double SourceLine, double Y);
