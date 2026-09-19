using CommunityToolkit.Mvvm.ComponentModel;
using MarkMello.Application.UseCases;
using MarkMello.Domain;

namespace MarkMello.Presentation.ViewModels;

/// <summary>
/// Одна открытая вкладка. Держит документ, его позицию прокрутки и признак
/// принадлежности папке: файл, открытый поверх папки, даёт вкладку, но в дереве
/// не подсвечивается (ADR-0007 Rule 4).
/// </summary>
public sealed partial class DocumentTabViewModel : ObservableObject, IDisposable
{
    public DocumentTabViewModel(string? path, string title)
    {
        Path = path;
        _title = string.IsNullOrWhiteSpace(title) ? string.Empty : title;
    }

    /// <summary>Путь файла. <c>null</c> у нового документа, который ещё не сохраняли.</summary>
    public string? Path { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    [NotifyPropertyChangedFor(nameof(AccessibleTitle))]
    private string _title;

    /// <summary>
    /// Имя родительской папки. Появляется только у вкладок, чьи имена совпали,
    /// и снимается, когда конфликт исчезает.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    [NotifyPropertyChangedFor(nameof(HasDisambiguator))]
    [NotifyPropertyChangedFor(nameof(DisambiguatorSuffix))]
    [NotifyPropertyChangedFor(nameof(AccessibleTitle))]
    private string? _disambiguator;

    public bool HasDisambiguator => !string.IsNullOrEmpty(Disambiguator);

    /// <summary>Хвост имени во вкладке: « · docs». Пустой, пока имя ни с кем не совпало.</summary>
    public string DisambiguatorSuffix => HasDisambiguator ? $" · {Disambiguator}" : string.Empty;

    /// <summary>Пометка состояния в заголовке вкладки: «(удалён)» у пропавшего файла.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    [NotifyPropertyChangedFor(nameof(StateSuffixText))]
    [NotifyPropertyChangedFor(nameof(IsDeletedFromDisk))]
    [NotifyPropertyChangedFor(nameof(AccessibleTitle))]
    private string? _stateSuffix;

    /// <summary>Пометка с пробелом перед ней — вкладка дописывает её к имени курсивом.</summary>
    public string StateSuffixText => string.IsNullOrEmpty(StateSuffix) ? string.Empty : $" {StateSuffix}";

    /// <summary>
    /// Пометку ставит только удаление файла снаружи: вкладка с правками остаётся,
    /// а её иконка меняется на перечёркнутый файл.
    /// </summary>
    public bool IsDeletedFromDisk => !string.IsNullOrEmpty(StateSuffix);

    /// <summary>Файл изменился на диске, а во вкладке есть правки — ждём решения пользователя.</summary>
    [ObservableProperty]
    private bool _hasExternalChange;

    /// <summary>
    /// Снимок устарел: файл изменился, пока вкладка была в фоне. Перечитаем при возврате,
    /// а не в момент чужого сохранения — на фоновую вкладку никто не смотрит.
    /// </summary>
    public bool NeedsReload { get; set; }

    public string DisplayTitle => string.IsNullOrEmpty(StateSuffix)
        ? Title
        : $"{Title} {StateSuffix}";

    /// <summary>
    /// Всё, что видно во вкладке, одной строкой — её читает экранный диктор. Без « · папка»
    /// две вкладки README.md звучали бы одинаково, хотя на экране различаются.
    /// </summary>
    public string AccessibleTitle => Title + DisambiguatorSuffix + StateSuffixText;

    /// <summary>Полный путь в тултипе: имя без пути не отвечает на вопрос «какой из двух README».</summary>
    [ObservableProperty]
    private string _tooltip = string.Empty;

    [ObservableProperty]
    private bool _isDirty;

    /// <summary>Файл лежит внутри открытой папки. У файлов, открытых поверх неё, — false.</summary>
    [ObservableProperty]
    private bool _belongsToWorkspace;

    [ObservableProperty]
    private bool _isActive;

    /// <summary>Позиция прокрутки: сохраняется при уходе с вкладки и восстанавливается при возврате.</summary>
    public double ScrollOffset { get; set; }

    /// <summary>
    /// Editor-сессия этой вкладки. Ленивая и живёт до закрытия вкладки: уход на соседнюю
    /// не должен терять несохранённые правки (ADR-0007 Rule 4).
    /// </summary>
    public EditorSessionViewModel? EditorSession { get; set; }

    /// <summary>Вкладка была в режиме правки — возвращаемся в него при активации.</summary>
    public bool IsEditMode { get; set; }

    public MarkdownSource? Document { get; private set; }

    /// <summary>
    /// Файл не открылся: вкладка показывает экран ошибки вместо документа. Такая вкладка
    /// не пишется в сессию, а Esc и ✕ её закрывают.
    /// </summary>
    public OpenDocumentResult? LoadError { get; private set; }

    public bool IsLoadError => LoadError is not null;

    /// <summary>
    /// Вкладка, которая была активной до неудачного открытия: закрытие вкладки ошибки
    /// возвращает к ней, а не к соседке по полосе.
    /// </summary>
    public DocumentTabViewModel? ReturnTab { get; private set; }

    public RenderedMarkdownDocument RenderedDocument { get; private set; } = RenderedMarkdownDocument.Empty;

    public void ApplyDocument(MarkdownSource? document, RenderedMarkdownDocument rendered)
    {
        Document = document;
        RenderedDocument = rendered;
        LoadError = null;
        ReturnTab = null;

        if (document is not null)
        {
            Path = document.Path;
            Title = document.FileName;
        }
    }

    /// <summary>
    /// Файл вкладки не прочитался. Снимок документа уходит: показывать старый текст
    /// под экраном ошибки значило бы выдавать его за содержимое файла.
    /// </summary>
    public void ApplyLoadError(OpenDocumentResult error, DocumentTabViewModel? returnTab)
    {
        ArgumentNullException.ThrowIfNull(error);

        LoadError = error;
        ReturnTab = ReferenceEquals(returnTab, this) ? null : returnTab;
        Document = null;
        RenderedDocument = RenderedMarkdownDocument.Empty;
        ScrollOffset = 0;
        NeedsReload = false;
    }

    /// <summary>Путь сменился после «Сохранить как»: вкладка следует за файлом.</summary>
    public void Retarget(string path, string title)
    {
        Path = path;
        Title = title;
    }

    /// <summary>Сессия закрывается вместе со вкладкой, а не при уходе на соседнюю.</summary>
    public void Dispose()
    {
        EditorSession?.Dispose();
        EditorSession = null;
    }
}
