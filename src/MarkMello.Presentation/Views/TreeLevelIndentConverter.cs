using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace MarkMello.Presentation.Views;

/// <summary>
/// Отступ строки дерева по её уровню: 8px у корня и +16px на уровень.
/// Штатный отступ Fluent считает свой конвертер, и переопределить его иначе нельзя:
/// вместе с ним пришлось бы потерять и сам отступ вложенности.
/// </summary>
public sealed class TreeLevelIndentConverter : IValueConverter
{
    public const double RootIndent = 8;
    public const double LevelIndent = 16;

    public static TreeLevelIndentConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var level = value is int depth && depth > 0 ? depth : 0;

        return new Thickness(RootIndent + (level * LevelIndent), 0, 0, 0);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
