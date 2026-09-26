using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace MarkMello.Presentation.Views;

/// <summary>
/// Кнопка обновления в строке окна (ADR-0004, «Update Model»). Надпись — «Обновить»,
/// «Загрузка · N %», «Установить» — выезжает при наведении и при фокусе с клавиатуры,
/// а в покое кнопка — квадрат с иконкой.
/// </summary>
public partial class UpdateNotificationButton : UserControl
{
    private const string ExpandedClass = "mm-expanded";

    private bool _hasKeyboardFocus;

    public UpdateNotificationButton()
    {
        InitializeComponent();

        UpdateHalo.PropertyChanged += OnHaloPropertyChanged;
        UpdateLabel.PropertyChanged += OnLabelPropertyChanged;
        UpdateButton.GotFocus += OnButtonGotFocus;
        UpdateButton.LostFocus += OnButtonLostFocus;
    }

    /// <summary>Раскрыта ли надпись: курсор над кнопкой или фокус пришёл с клавиатуры.</summary>
    internal bool IsExpanded => UpdateHalo.IsPointerOver || _hasKeyboardFocus;

    private void OnHaloPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsPointerOverProperty)
        {
            UpdateExpansion();
        }
    }

    /// <summary>Проценты меняют ширину надписи — раскрытая плашка подстраивается под неё.</summary>
    private void OnLabelPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextBlock.TextProperty && IsExpanded)
        {
            UpdateExpansion();
        }
    }

    /// <summary>Фокус мышью надпись не раскрывает — только обход с клавиатуры, как :focus-visible.</summary>
    private void OnButtonGotFocus(object? sender, FocusChangedEventArgs e)
    {
        _hasKeyboardFocus = e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional;
        UpdateExpansion();
    }

    private void OnButtonLostFocus(object? sender, RoutedEventArgs e)
    {
        _hasKeyboardFocus = false;
        UpdateExpansion();
    }

    private void UpdateExpansion()
    {
        var expanded = IsExpanded;
        UpdateHalo.Classes.Set(ExpandedClass, expanded);
        UpdateLabelHost.Width = expanded ? MeasureLabelWidth() : 0;
    }

    /// <summary>Полная ширина надписи с отступом справа — вне ограничения свёрнутой плашки.</summary>
    private double MeasureLabelWidth()
    {
        UpdateLabel.Measure(Size.Infinity);
        var width = UpdateLabel.DesiredSize.Width;
        UpdateLabel.InvalidateMeasure();
        return Math.Ceiling(width);
    }
}
