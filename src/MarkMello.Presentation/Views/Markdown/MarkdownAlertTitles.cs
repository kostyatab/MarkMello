using MarkMello.Domain;

namespace MarkMello.Presentation.Views.Markdown;

/// <summary>
/// Заголовки GitHub alerts на языке интерфейса — снимок на момент сборки документа.
/// </summary>
/// <remarks>
/// Заголовок входит в текстовый поток (ADR-0001): по нему считаются offset'ы
/// выделения и поиска, и он попадает в буфер обмена вместе с текстом alert.
/// Поэтому текстовая карта, шапки alert и копирование берут заголовки из одного
/// снимка, а при смене языка документ пересобирается с новым.
/// </remarks>
internal sealed class MarkdownAlertTitles
{
    private readonly string[] _titles;

    private MarkdownAlertTitles(string[] titles)
    {
        _titles = titles;
    }

    /// <param name="localize">
    /// Строка по ключу локализации; второй аргумент — значение, если строки нет.
    /// </param>
    public static MarkdownAlertTitles Create(Func<string, string, string> localize)
    {
        ArgumentNullException.ThrowIfNull(localize);

        var kinds = Enum.GetValues<MarkdownAlertKind>();
        var titles = new string[kinds.Length];
        foreach (var kind in kinds)
        {
            titles[(int)kind] = localize(GetLocalizationKey(kind), MarkdownDocumentTextMap.GetDefaultAlertTitle(kind));
        }

        return new MarkdownAlertTitles(titles);
    }

    public string Get(MarkdownAlertKind kind) => _titles[(int)kind];

    public bool HasSameTitles(MarkdownAlertTitles? other)
        => other is not null && _titles.AsSpan().SequenceEqual(other._titles);

    internal static string GetLocalizationKey(MarkdownAlertKind kind) => kind switch
    {
        MarkdownAlertKind.Note => "AlertNote",
        MarkdownAlertKind.Tip => "AlertTip",
        MarkdownAlertKind.Important => "AlertImportant",
        MarkdownAlertKind.Warning => "AlertWarning",
        MarkdownAlertKind.Caution => "AlertCaution",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };
}
