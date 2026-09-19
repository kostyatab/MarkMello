using System.Globalization;
using Avalonia.Input;

namespace MarkMello.Presentation.Localization;

/// <summary>
/// Подпись сочетания клавиш для меню, тултипов и клавиш-плашек. На macOS — символы
/// в порядке Apple ⌃⌥⇧⌘ без разделителей (⇧⌘O), на Windows и Linux — слова через «+»
/// (Ctrl+Shift+O). Платформа приходит параметром — значением
/// <c>IPlatformServices.PlatformName</c>, а не из <see cref="OperatingSystem"/>: так подписи
/// всех трёх платформ проверяются тестами на любой машине. Чистая функция без ввода-вывода.
/// </summary>
public static class ShortcutLabel
{
    private const string MacOSPlatformName = "macOS";

    /// <summary>
    /// Сочетание с командной клавишей платформы: ⌘ на macOS, Ctrl на Windows и Linux —
    /// та же пара, что в <c>MainWindow.KeyBindings</c> (Ctrl+O и Cmd+O).
    /// </summary>
    public static KeyGesture Command(Key key, string platformName, KeyModifiers extra = KeyModifiers.None)
        => new(key, (IsMacOS(platformName) ? KeyModifiers.Meta : KeyModifiers.Control) | extra);

    /// <summary>Сочетание одной строкой: «⇧⌘O» или «Ctrl+Shift+O».</summary>
    public static string Format(KeyGesture gesture, string platformName)
        => string.Join(IsMacOS(platformName) ? string.Empty : "+", Keys(gesture, platformName));

    /// <summary>
    /// Клавиши сочетания по отдельности — для подсказок, где каждая клавиша нарисована
    /// своей плашкой: «⌘», «O» или «Ctrl», «O».
    /// </summary>
    public static IReadOnlyList<string> Keys(KeyGesture gesture, string platformName)
    {
        ArgumentNullException.ThrowIfNull(gesture);

        var isMacOS = IsMacOS(platformName);
        var modifiers = gesture.KeyModifiers;
        var keys = new List<string>(5);

        if (isMacOS)
        {
            AddIf(keys, modifiers, KeyModifiers.Control, "⌃");
            AddIf(keys, modifiers, KeyModifiers.Alt, "⌥");
            AddIf(keys, modifiers, KeyModifiers.Shift, "⇧");
            AddIf(keys, modifiers, KeyModifiers.Meta, "⌘");
        }
        else
        {
            AddIf(keys, modifiers, KeyModifiers.Control, "Ctrl");
            AddIf(keys, modifiers, KeyModifiers.Shift, "Shift");
            AddIf(keys, modifiers, KeyModifiers.Alt, "Alt");
            AddIf(keys, modifiers, KeyModifiers.Meta, string.Equals(platformName, "Windows", StringComparison.Ordinal) ? "Win" : "Super");
        }

        keys.Add(KeyName(gesture.Key, isMacOS));
        return keys;
    }

    private static bool IsMacOS(string platformName)
        => string.Equals(platformName, MacOSPlatformName, StringComparison.Ordinal);

    private static void AddIf(List<string> keys, KeyModifiers modifiers, KeyModifiers flag, string label)
    {
        if ((modifiers & flag) != 0)
        {
            keys.Add(label);
        }
    }

    private static string KeyName(Key key, bool isMacOS) => key switch
    {
        >= Key.A and <= Key.Z => ((char)('A' + (key - Key.A))).ToString(),
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.F1 and <= Key.F24 => "F" + (key - Key.F1 + 1).ToString(CultureInfo.InvariantCulture),
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.OemPlus => "+",
        Key.OemMinus => "-",
        Key.Left => "←",
        Key.Right => "→",
        Key.Up => "↑",
        Key.Down => "↓",
        Key.Space => "Space",
        Key.Tab => isMacOS ? "⇥" : "Tab",
        Key.Enter => isMacOS ? "↵" : "Enter",
        Key.Back => isMacOS ? "⌫" : "Backspace",
        Key.Delete => isMacOS ? "⌦" : "Del",
        Key.Escape => isMacOS ? "⎋" : "Esc",
        _ => key.ToString()
    };
}
