using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using MarkMello.Domain.Outline;

namespace MarkMello.Presentation.Views.Markdown.Outline;

/// <summary>
/// Слой оглавления поверх документа: рельс у правого края по центру высоты и
/// карточка, которая по наведению ложится прямо на рельс. Рельс и карточка
/// создаются лениво — на быстром пути открытия документа здесь пустая панель.
/// </summary>
internal sealed class DocumentOutlineLayer : Panel
{
    /// <summary>От правого края области документа до правого края зоны рельса.</summary>
    public const double RailRightMargin = 12;

    /// <summary>
    /// От правого края области документа до правого края карточки: карточка на 4 px
    /// правее зоны рельса и накрывает её целиком.
    /// </summary>
    public const double CardRightMargin = 8;

    public const double CardWidth = 256;

    /// <summary>Карточка не выше этой доли области документа — дальше прокручивается.</summary>
    public const double CardMaxHeightFraction = 0.7;

    private const double CardVerticalInset = 12;

    public static readonly TimeSpan OpenDelay = TimeSpan.FromMilliseconds(150);

    /// <summary>Запас на перевод курсора и дрожание руки, пока карточка не закрылась.</summary>
    public static readonly TimeSpan CloseDelay = TimeSpan.FromMilliseconds(200);

    private readonly DispatcherTimer _openTimer;
    private readonly DispatcherTimer _closeTimer;
    private DocumentOutlineRail? _rail;
    private DocumentOutlineCard? _card;
    private IReadOnlyList<DocumentOutlineEntry> _entries = [];
    private int _currentIndex;
    private bool _isCardContentStale = true;
    private string? _accessibleName;

    public DocumentOutlineLayer()
    {
        _openTimer = new DispatcherTimer { Interval = OpenDelay };
        _openTimer.Tick += OnOpenTimerTick;
        _closeTimer = new DispatcherTimer { Interval = CloseDelay };
        _closeTimer.Tick += OnCloseTimerTick;
        IsVisible = false;
    }

    /// <summary>Пункт выбран на рельсе или в карточке: его индекс в оглавлении.</summary>
    public event EventHandler<int>? EntryInvoked;

    /// <summary>
    /// Открыт другой оверлей (меню, Aa, поиск, модальный диалог) — карточка по
    /// наведению не открывается: одновременно открыт не больше одного оверлея.
    /// </summary>
    public Func<bool>? IsCardSuppressed { get; set; }

    /// <summary>Сколько места справа занимает рельс вместе с отступом от края.</summary>
    public static double RailFootprint => DocumentOutlineRail.RailWidth + RailRightMargin;

    public bool IsCardOpen => _card is { IsVisible: true };

    internal DocumentOutlineRail? Rail => _rail;

    internal DocumentOutlineCard? Card => _card;

    public string? AccessibleName
    {
        get => _accessibleName;
        set
        {
            _accessibleName = value;
            if (_rail is not null)
            {
                AutomationProperties.SetName(_rail, value);
            }
        }
    }

    public void Show(IReadOnlyList<DocumentOutlineEntry> entries, int currentIndex)
    {
        // Пересборка идёт на каждое изменение размера документа; те же пункты не
        // пересоздаются — иначе под курсором мигала бы подсветка и гасла подсказка.
        if (_rail is null || !entries.SequenceEqual(_entries))
        {
            _entries = entries;
            EnsureRail().SetEntries(entries.Select(static entry => entry.Level).ToList(), currentIndex);
            _isCardContentStale = true;
        }

        IsVisible = true;
        SetCurrentIndex(currentIndex);
        if (IsCardOpen)
        {
            UpdateCardContent();
        }
    }

    public void Hide()
    {
        CloseCard();
        IsVisible = false;
    }

    public void SetCurrentIndex(int currentIndex)
    {
        _currentIndex = currentIndex;
        _rail?.SetCurrentIndex(currentIndex);
        if (IsCardOpen)
        {
            _card!.SetCurrentIndex(currentIndex);
        }
    }

    public void CloseCard()
    {
        _openTimer.Stop();
        _closeTimer.Stop();
        if (_card is not null)
        {
            _card.IsVisible = false;
        }
    }

    internal void OpenCard()
    {
        _openTimer.Stop();
        _closeTimer.Stop();
        if (!IsVisible || _entries.Count == 0 || IsCardSuppressed?.Invoke() == true)
        {
            return;
        }

        var card = EnsureCard();
        UpdateCardContent();
        card.IsVisible = true;
        card.ScrollCurrentIntoViewOnNextLayout();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_card is not null && !double.IsInfinity(availableSize.Height))
        {
            _card.MaxHeight = Math.Max(0, availableSize.Height * CardMaxHeightFraction);
        }

        return base.MeasureOverride(availableSize);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var railCenter = finalSize.Height / 2;
        if (_rail is not null)
        {
            var size = _rail.DesiredSize;
            var top = (finalSize.Height - size.Height) / 2;
            _rail.Arrange(new Rect(finalSize.Width - RailRightMargin - size.Width, top, size.Width, size.Height));
        }

        if (_card is not null)
        {
            var size = _card.DesiredSize;
            var maxTop = Math.Max(CardVerticalInset, finalSize.Height - size.Height - CardVerticalInset);
            var top = Math.Clamp(railCenter - size.Height / 2, CardVerticalInset, maxTop);
            _card.Arrange(new Rect(finalSize.Width - CardRightMargin - size.Width, top, size.Width, size.Height));
        }

        return finalSize;
    }

    private DocumentOutlineRail EnsureRail()
    {
        if (_rail is not null)
        {
            return _rail;
        }

        var rail = new DocumentOutlineRail();
        AutomationProperties.SetName(rail, _accessibleName);
        rail.PointerEntered += OnRailPointerEntered;
        rail.PointerExited += OnPointerLeftOutline;
        rail.EntryInvoked += OnEntryInvoked;
        Children.Add(rail);
        _rail = rail;
        return rail;
    }

    private DocumentOutlineCard EnsureCard()
    {
        if (_card is not null)
        {
            return _card;
        }

        var card = new DocumentOutlineCard { Width = CardWidth, IsVisible = false };
        card.PointerEntered += OnCardPointerEntered;
        card.PointerExited += OnPointerLeftOutline;
        card.EntryInvoked += OnEntryInvoked;

        // Карточка ложится поверх рельса — она последней в дереве.
        Children.Add(card);
        _card = card;
        InvalidateMeasure();
        return card;
    }

    private void UpdateCardContent()
    {
        if (_card is null)
        {
            return;
        }

        if (_isCardContentStale)
        {
            _card.SetEntries(_entries, _currentIndex);
            _isCardContentStale = false;
        }
        else
        {
            _card.SetCurrentIndex(_currentIndex);
        }
    }

    private void OnRailPointerEntered(object? sender, PointerEventArgs e)
    {
        _closeTimer.Stop();
        if (!IsCardOpen && IsCardSuppressed?.Invoke() != true)
        {
            _openTimer.Start();
        }
    }

    private void OnCardPointerEntered(object? sender, PointerEventArgs e)
        => _closeTimer.Stop();

    private void OnPointerLeftOutline(object? sender, PointerEventArgs e)
    {
        _openTimer.Stop();
        if (IsCardOpen)
        {
            _closeTimer.Stop();
            _closeTimer.Start();
        }
    }

    private void OnOpenTimerTick(object? sender, EventArgs e)
    {
        _openTimer.Stop();
        if (_rail is { IsPointerOver: true })
        {
            OpenCard();
        }
    }

    private void OnCloseTimerTick(object? sender, EventArgs e)
    {
        _closeTimer.Stop();
        if (_rail is { IsPointerOver: true } || _card is { IsPointerOver: true })
        {
            return;
        }

        CloseCard();
    }

    private void OnEntryInvoked(object? sender, int index)
    {
        CloseCard();
        EntryInvoked?.Invoke(this, index);
    }
}
