using Avalonia.Input;
using MarkMello.Presentation.Localization;

namespace MarkMello.Presentation.Tests;

public sealed class ShortcutLabelTests
{
    [Theory]
    [InlineData("macOS", "⇧⌘O")]
    [InlineData("Windows", "Ctrl+Shift+O")]
    [InlineData("Linux", "Ctrl+Shift+O")]
    public void OpenFolderShortcutFollowsPlatform(string platformName, string expected)
    {
        var gesture = ShortcutLabel.Command(Key.O, platformName, KeyModifiers.Shift);

        Assert.Equal(expected, ShortcutLabel.Format(gesture, platformName));
    }

    [Theory]
    [InlineData("macOS", "⌘,")]
    [InlineData("Windows", "Ctrl+,")]
    [InlineData("Linux", "Ctrl+,")]
    public void SettingsShortcutShowsCommaAsCharacter(string platformName, string expected)
    {
        var gesture = ShortcutLabel.Command(Key.OemComma, platformName);

        Assert.Equal(expected, ShortcutLabel.Format(gesture, platformName));
    }

    [Theory]
    [InlineData("macOS", "⌘W")]
    [InlineData("Windows", "Ctrl+W")]
    [InlineData("Linux", "Ctrl+W")]
    public void CloseTabShortcutFollowsPlatform(string platformName, string expected)
    {
        var gesture = ShortcutLabel.Command(Key.W, platformName);

        Assert.Equal(expected, ShortcutLabel.Format(gesture, platformName));
    }

    [Theory]
    [InlineData("macOS", new[] { "⌘", "O" })]
    [InlineData("Windows", new[] { "Ctrl", "O" })]
    [InlineData("Linux", new[] { "Ctrl", "O" })]
    public void KeysSplitShortcutIntoSeparateCaps(string platformName, string[] expected)
    {
        var gesture = ShortcutLabel.Command(Key.O, platformName);

        Assert.Equal(expected, ShortcutLabel.Keys(gesture, platformName));
    }

    [Theory]
    [InlineData("macOS", "⌃⌥⇧⌘K")]
    [InlineData("Windows", "Ctrl+Shift+Alt+Win+K")]
    [InlineData("Linux", "Ctrl+Shift+Alt+Super+K")]
    public void ModifiersKeepPlatformOrder(string platformName, string expected)
    {
        var gesture = new KeyGesture(
            Key.K,
            KeyModifiers.Meta | KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Control);

        Assert.Equal(expected, ShortcutLabel.Format(gesture, platformName));
    }

    [Theory]
    [InlineData(Key.Back, "⌫", "Backspace")]
    [InlineData(Key.Enter, "↵", "Enter")]
    [InlineData(Key.Tab, "⇥", "Tab")]
    [InlineData(Key.Escape, "⎋", "Esc")]
    [InlineData(Key.D0, "0", "0")]
    [InlineData(Key.F5, "F5", "F5")]
    public void SpecialKeysUseMacSymbolsOnlyOnMacOS(Key key, string macOS, string others)
    {
        var gesture = new KeyGesture(key);

        Assert.Equal(macOS, ShortcutLabel.Format(gesture, "macOS"));
        Assert.Equal(others, ShortcutLabel.Format(gesture, "Windows"));
        Assert.Equal(others, ShortcutLabel.Format(gesture, "Linux"));
    }

    [Fact]
    public void UnknownPlatformFallsBackToWordLabels()
    {
        var gesture = ShortcutLabel.Command(Key.E, "Unknown");

        Assert.Equal("Ctrl+E", ShortcutLabel.Format(gesture, "Unknown"));
    }
}
