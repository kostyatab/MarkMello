namespace MarkMello.Presentation.ViewModels;

/// <summary>Вид ошибки на экране A-LoadError: от него зависят иконка и кнопка «Повторить».</summary>
public enum LoadErrorKind
{
    None,
    NotFound,
    AccessDenied,
    UnsupportedType,
    ReadFailure,
    Folder
}
