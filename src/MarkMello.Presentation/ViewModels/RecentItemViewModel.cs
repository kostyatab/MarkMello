using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkMello.Domain.Recent;

namespace MarkMello.Presentation.ViewModels;

/// <summary>
/// Строка «Недавних» на стартовом экране: иконка файла или папки, имя, папка с <c>~</c>
/// и дата открытия. Пропавшая с диска запись приглушена, пока её не уберут.
/// </summary>
public sealed partial class RecentItemViewModel : ObservableObject
{
    private readonly Func<RecentItemViewModel, Task> _open;

    public RecentItemViewModel(
        RecentEntry entry,
        string name,
        string location,
        string openedLabel,
        Func<RecentItemViewModel, Task> open)
    {
        _open = open;
        Entry = entry;
        Name = name;
        Location = location;
        OpenedLabel = openedLabel;
    }

    public RecentEntry Entry { get; }

    public string Path => Entry.Path;

    public bool IsFolder => Entry.Kind == RecentEntryKind.Folder;

    public bool IsFile => !IsFolder;

    public string Name { get; }

    public string Location { get; }

    public string OpenedLabel { get; }

    [ObservableProperty]
    private bool _isMissing;

    /// <summary>Клик и Enter по строке. Решает shell: открыть или спросить, убрать ли пропавшую.</summary>
    [RelayCommand]
    private Task OpenAsync() => _open(this);
}
