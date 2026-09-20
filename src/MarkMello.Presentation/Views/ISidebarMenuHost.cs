using Avalonia;

namespace MarkMello.Presentation.Views;

/// <summary>
/// Окно, которое показывает меню сайдбара карточками внутри себя (ADR-0009 Rule 4).
/// Сайдбар знает, от чего раскрывается меню, — от кнопки или от точки клика, — а окно
/// держит слой карточек, считает позицию и следит за фокусом.
/// </summary>
internal interface ISidebarMenuHost
{
    /// <summary>
    /// Запомнить, от чего раскрывается следующее меню: <paramref name="anchor"/> — рамка
    /// кнопки или точка клика в координатах <paramref name="source"/>,
    /// <paramref name="alignRight"/> — карточка прижимается к её правому краю, а не к левому.
    /// </summary>
    void AnchorSidebarMenu(Visual source, Rect anchor, bool alignRight);

    /// <summary>Перевести фокус в открытую карточку — для меню, вызванного с клавиатуры.</summary>
    void FocusSidebarMenu();
}
