using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using MarkMello.Presentation.Views;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Иконка берёт цвет из Foreground кнопки, а Fluent сам перекрашивает его в
/// :pointerover, :pressed, :disabled и :checked. Здесь зафиксированы цвета
/// состояний, какими они были у самописных иконок, — в обеих темах, с настоящими
/// Fluent и стилями приложения.
/// </summary>
[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class IconStateColoursTests
{
    private const string Soft = "MmTextSoftBrush";
    private const string Text = "MmTextBrush";
    private const string Faint = "MmTextFaintBrush";
    private const string Accent = "MmAccentBrush";

    private readonly AvaloniaHeadlessFixture _fixture;

    public IconStateColoursTests(AvaloniaHeadlessFixture fixture) => _fixture = fixture;

    public static TheoryData<string, string, bool, string, string> Cases()
    {
        (string ClassName, bool IsToggle, string States, string Expected)[] cases =
        [
            // Кнопки find bar: наведение темнее, нажатие — акцент.
            ("topbar-ghost", false, "", Soft),
            ("topbar-ghost", false, ":pointerover", Text),
            ("topbar-ghost", false, ":pointerover :pressed", Accent),
            ("topbar-ghost", false, ":disabled", Soft),
            // Кнопки строки окна и шапки сайдбара: под курсором, нажатая и с открытой
            // карточкой иконка темнеет до цвета текста — акцента в строке нет.
            ("mm-row-button", false, "", Soft),
            ("mm-row-button", false, ":pointerover", Text),
            ("mm-row-button", false, ":pointerover :pressed", Text),
            ("mm-row-button", false, ":disabled", Faint),
            ("mm-row-button", true, "", Soft),
            ("mm-row-button", true, ":pointerover", Text),
            ("mm-row-button", true, ":pressed", Text),
            ("mm-row-button", true, ":checked", Text),
            ("mm-row-button", true, ":checked :pointerover", Text),
            ("welcome-secondary", false, "", Soft),
            ("welcome-secondary", false, ":pointerover", Text),
            ("welcome-secondary", false, ":pointerover :pressed", Text),
            // ✕ в шапке окна «Настройки».
            ("mm-settings-close", false, "", Soft),
            ("mm-settings-close", false, ":pointerover", Text),
            ("mm-settings-close", false, ":pointerover :pressed", Text),
            ("mm-settings-close", false, ":pressed", Soft),
            ("mm-editor-toolbar-button", false, "", Soft),
            ("mm-editor-toolbar-button", false, ":pointerover", Text),
            ("mm-editor-toolbar-button", false, ":pointerover :pressed", Text),
            ("mm-code-copy-button", false, "", Faint),
            ("mm-code-copy-button", false, ":pointerover", Text),
            ("mm-code-copy-button", false, ":focus-visible", Text),
            ("mm-code-copy-button", false, ":pressed", Faint),
            // Карточка Aa: сегменты темы с иконкой и кнопки «− / +» размера текста.
            ("mm-card-segment", true, "", Soft),
            ("mm-card-segment", true, ":pointerover", Text),
            ("mm-card-segment", true, ":pointerover :pressed", Text),
            ("mm-card-segment", true, ":checked", Text),
            ("mm-card-segment", true, ":checked :pointerover", Text),
            ("mm-card-step", false, "", Soft),
            ("mm-card-step", false, ":pointerover", Text),
            ("mm-card-step", false, ":pointerover :pressed", Text),
            ("mm-card-step", false, ":disabled", Faint),
        ];

        var data = new TheoryData<string, string, bool, string, string>();
        foreach (var theme in new[] { "Light", "Dark" })
        {
            foreach (var (className, isToggle, states, expected) in cases)
            {
                data.Add(theme, className, isToggle, states, expected);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public Task IconColourFollowsTheButtonState(string theme, string className, bool isToggle, string states, string expectedBrushKey)
    {
        return _fixture.Session.Dispatch(() =>
        {
            var icon = new LucideIcon { Width = 14, Height = 14 };
            Button button = isToggle ? new ToggleButton() : new Button();
            button.Classes.Add(className);
            button.Content = icon;

            var window = ThemedTestWindow.Create(theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light, button);
            window.Show();
            window.UpdateLayout();

            foreach (var state in states.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                switch (state)
                {
                    case ":checked":
                        ((ToggleButton)button).IsChecked = true;
                        break;
                    case ":disabled":
                        button.IsEnabled = false;
                        break;
                    default:
                        ((IPseudoClasses)button.Classes).Add(state);
                        break;
                }
            }

            Assert.True(window.TryFindResource(expectedBrushKey, window.ActualThemeVariant, out var expected));
            Assert.Same(Assert.IsAssignableFrom<IBrush>(expected), icon.Foreground);

            window.Close();
        }, CancellationToken.None);
    }
}
