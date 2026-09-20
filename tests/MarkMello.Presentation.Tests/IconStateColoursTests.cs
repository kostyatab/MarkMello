using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using MarkMello.Presentation.Views;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Иконка берёт цвет из Foreground кнопки, а Fluent сам перекрашивает его в
/// :pointerover, :pressed, :disabled и :checked. Здесь зафиксированы цвета
/// состояний у ролей кнопок — в обеих темах, с настоящими Fluent и стилями
/// приложения.
/// </summary>
[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class IconStateColoursTests
{
    private const string Soft = "MmTextSoftBrush";
    private const string Text = "MmTextBrush";
    private const string Faint = "MmTextFaintBrush";

    private readonly AvaloniaHeadlessFixture _fixture;

    public IconStateColoursTests(AvaloniaHeadlessFixture fixture) => _fixture = fixture;

    public static TheoryData<string, string, bool, string, string> Cases()
    {
        (string Classes, bool IsToggle, string States, string Expected)[] cases =
        [
            // Кнопки строки окна и шапки сайдбара: под курсором, нажатая и с открытой
            // карточкой иконка темнеет до цвета текста — акцента в строке нет.
            ("mm-icon-button mm-row-button", false, "", Soft),
            ("mm-icon-button mm-row-button", false, ":pointerover", Text),
            ("mm-icon-button mm-row-button", false, ":pointerover :pressed", Text),
            ("mm-icon-button mm-row-button", false, ":pressed", Text),
            ("mm-icon-button mm-row-button", false, ":disabled", Faint),
            ("mm-icon-button mm-row-button", false, ":disabled :pointerover", Faint),
            ("mm-icon-button mm-row-button", true, "", Soft),
            ("mm-icon-button mm-row-button", true, ":pointerover", Text),
            ("mm-icon-button mm-row-button", true, ":pressed", Text),
            ("mm-icon-button mm-row-button", true, ":checked", Text),
            ("mm-icon-button mm-row-button", true, ":checked :pointerover", Text),
            // ✕ в шапке окна «Настройки».
            ("mm-icon-button mm-settings-close", false, "", Soft),
            ("mm-icon-button mm-settings-close", false, ":pointerover", Text),
            ("mm-icon-button mm-settings-close", false, ":pointerover :pressed", Text),
            ("mm-icon-button mm-settings-close", false, ":pressed", Text),
            ("mm-icon-button mm-settings-close", false, ":disabled", Faint),
            // Кнопки карточки поиска и панели редактора — та же роль, другой размер.
            ("mm-icon-button mm-find-button", false, "", Soft),
            ("mm-icon-button mm-find-button", false, ":pointerover", Text),
            ("mm-icon-button mm-find-button", false, ":pressed", Text),
            ("mm-icon-button mm-editor-toolbar-button", false, "", Soft),
            ("mm-icon-button mm-editor-toolbar-button", false, ":pointerover", Text),
            ("mm-icon-button mm-editor-toolbar-button", false, ":pointerover :pressed", Text),
            ("mm-icon-button mm-editor-toolbar-button", false, ":disabled", Faint),
            // Тон «голая» (крестик вкладки и списка «ещё N»): подложки нет, на курсор
            // отвечает только иконка. Сам mm-tab-close до показа вкладкой невидим,
            // поэтому тон проверяется на роли без класса места.
            ("mm-icon-button mm-icon-button-bare", false, "", Soft),
            ("mm-icon-button mm-icon-button-bare", false, ":pointerover", Text),
            ("mm-icon-button mm-icon-button-bare", false, ":pressed", Text),
            // Тон «на заливке»: «копировать» у блока кода в покое того же тона, что подпись языка.
            ("mm-icon-button mm-icon-button-raised mm-code-copy-button", false, "", Faint),
            ("mm-icon-button mm-icon-button-raised mm-code-copy-button", false, ":pointerover", Text),
            ("mm-icon-button mm-icon-button-raised mm-code-copy-button", false, ":focus-visible", Text),
            ("mm-icon-button mm-icon-button-raised mm-code-copy-button", false, ":pressed", Text),
            // Карточка Aa: сегменты темы с иконкой и кнопки «− / +» размера текста.
            ("mm-card-segment", true, "", Soft),
            ("mm-card-segment", true, ":pointerover", Text),
            ("mm-card-segment", true, ":pointerover :pressed", Text),
            ("mm-card-segment", true, ":checked", Text),
            ("mm-card-segment", true, ":checked :pointerover", Text),
            ("mm-icon-button mm-icon-button-raised mm-card-step", false, "", Soft),
            ("mm-icon-button mm-icon-button-raised mm-card-step", false, ":pointerover", Text),
            ("mm-icon-button mm-icon-button-raised mm-card-step", false, ":pointerover :pressed", Text),
            ("mm-icon-button mm-icon-button-raised mm-card-step", false, ":disabled", Faint),
            ("mm-icon-button mm-icon-button-raised mm-card-step", false, ":disabled :pointerover", Faint),
        ];

        var data = new TheoryData<string, string, bool, string, string>();
        foreach (var theme in new[] { "Light", "Dark" })
        {
            foreach (var (classes, isToggle, states, expected) in cases)
            {
                data.Add(theme, classes, isToggle, states, expected);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public Task IconColourFollowsTheButtonState(string theme, string classes, bool isToggle, string states, string expectedBrushKey)
    {
        return _fixture.Session.Dispatch(() =>
        {
            var icon = new LucideIcon { Width = 14, Height = 14 };
            Button button = isToggle ? new ToggleButton() : new Button();
            foreach (var name in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                button.Classes.Add(name);
            }

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
