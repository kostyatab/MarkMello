using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace MarkMello.Presentation.Views;

/// <summary>
/// Общая рамка модальных диалогов (ADR-0009 Rule 10): скрим на всё окно — сайдбар,
/// вкладки и строку, — карточка по центру, верхние 44 px скрима тянут окно. Шаблон
/// лежит в <c>Themes/Controls.axaml</c>, содержимое карточки задаёт диалог.
/// </summary>
/// <remarks>
/// Рамка держит фокус внутри вопроса: при появлении отдаёт его кнопке с
/// <see cref="IsInitialFocusProperty"/>, Tab не выходит за карточку, а после закрытия
/// фокус возвращается туда, где был, — в редактор, если спрашивали из правки.
/// </remarks>
public sealed class ModalDialogFrame : ContentControl
{
    /// <summary>Кнопка, которая получает фокус, когда диалог появляется или меняет вопрос.</summary>
    public static readonly AttachedProperty<bool> IsInitialFocusProperty =
        AvaloniaProperty.RegisterAttached<ModalDialogFrame, InputElement, bool>("IsInitialFocus");

    /// <summary>
    /// Смена значения возвращает фокус на начальную кнопку: диалог задал новый вопрос.
    /// Удаление переспрашивает про безвозвратное — и Enter снова должен попадать
    /// в безопасный ответ, а не в кнопку, которую только что нажали.
    /// </summary>
    public static readonly StyledProperty<object?> FocusResetKeyProperty =
        AvaloniaProperty.Register<ModalDialogFrame, object?>(nameof(FocusResetKey));

    /// <summary>
    /// Показывать ли кольцо на начальной кнопке. У вопроса оно говорит, что сработает по
    /// Enter; окну «Настройки» подтверждать нечего — фокус встаёт внутрь карточки без кольца,
    /// а кольцо появится, когда пользователь сам пойдёт по кнопкам Tab'ом.
    /// </summary>
    public static readonly StyledProperty<bool> ShowsInitialFocusRingProperty =
        AvaloniaProperty.Register<ModalDialogFrame, bool>(nameof(ShowsInitialFocusRing), defaultValue: true);

    private IInputElement? _focusBeforeDialog;

    public bool ShowsInitialFocusRing
    {
        get => GetValue(ShowsInitialFocusRingProperty);
        set => SetValue(ShowsInitialFocusRingProperty, value);
    }

    public object? FocusResetKey
    {
        get => GetValue(FocusResetKeyProperty);
        set => SetValue(FocusResetKeyProperty, value);
    }

    public static bool GetIsInitialFocus(InputElement element) => element.GetValue(IsInitialFocusProperty);

    public static void SetIsInitialFocus(InputElement element, bool value) => element.SetValue(IsInitialFocusProperty, value);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _focusBeforeDialog = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        FocusInitialElement();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == FocusResetKeyProperty && IsLoaded)
        {
            FocusInitialElement();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        // Рамка уходит раньше своих кнопок, поэтому фокус переезжает до того, как
        // кнопка с ним исчезнет и окно останется вообще без фокуса.
        var previous = _focusBeforeDialog;
        _focusBeforeDialog = null;

        if (previous is Visual visual && visual.IsAttachedToVisualTree())
        {
            previous.Focus();
        }
    }

    /// <summary>
    /// Фокус как с клавиатуры: кольцо сразу показывает, что сработает по Enter. Без кольца —
    /// обычный фокус: Tab всё равно ходит внутри карточки.
    /// </summary>
    private void FocusInitialElement()
        => this.GetVisualDescendants()
            .OfType<InputElement>()
            .FirstOrDefault(static element => GetIsInitialFocus(element) && element.IsEffectivelyVisible)
            ?.Focus(ShowsInitialFocusRing ? NavigationMethod.Tab : NavigationMethod.Unspecified);
}
