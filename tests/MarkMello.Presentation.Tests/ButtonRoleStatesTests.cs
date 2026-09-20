using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Состояния ролей кнопок (MM-42). Кнопку зажали и увели курсор: она остаётся
/// нажатой, но уже не под курсором — и без своего цвета на этот случай Fluent
/// закрашивает её серым. Здесь у каждой роли проверены нажатие без наведения и
/// выключенная кнопка — в том числе под курсором, — в светлой и тёмной теме.
/// </summary>
[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class ButtonRoleStatesTests
{
    /// <summary>Роли и их фон: null — прозрачный (роль сама говорит «без заливки»).</summary>
    public static TheoryData<string, string, string?, string?> Cases()
    {
        (string Classes, string? Pressed, string? Disabled)[] roles =
        [
            ("mm-action-primary", "MmTextBrush", "MmTextBrush"),
            ("mm-action-secondary", "MmTabHoverBrush", "MmTabBrush"),
            ("mm-action-destructive", "MmAccentBrush", "MmAccentBrush"),
            ("mm-action-secondary mm-action-raised", "MmTabHoverBrush", "MmElevatedBackgroundBrush"),
            ("mm-icon-button mm-row-button", "MmTabActiveBrush", null),
            ("mm-icon-button mm-icon-button-raised mm-card-step", "MmElevatedBackgroundBrush", null),
            ("mm-icon-button mm-icon-button-bare", null, null),
            ("mm-list-item mm-menu-command", "MmTabActiveBrush", null),
            ("mm-list-item recent-row", "MmTabActiveBrush", null),
            ("mm-row-primary", "MmTextBrush", "MmTextBrush"),
            ("mm-tab-overflow", "MmTabActiveBrush", "MmTabBrush"),
            ("titlebar", "MmTabActiveBrush", null),
            ("mm-link", null, null),
            ("mm-link mm-link-quiet", null, null),
        ];

        var data = new TheoryData<string, string, string?, string?>();
        foreach (var theme in new[] { "Light", "Dark" })
        {
            foreach (var (classes, pressed, disabled) in roles)
            {
                data.Add(theme, classes, pressed, disabled);
            }
        }

        return data;
    }

    private readonly AvaloniaHeadlessFixture _fixture;

    public ButtonRoleStatesTests(AvaloniaHeadlessFixture fixture) => _fixture = fixture;

    [Theory]
    [MemberData(nameof(Cases))]
    public Task PressedAndDisabledStayInThePalette(string theme, string classes, string? pressedBrushKey, string? disabledBrushKey)
    {
        return _fixture.Session.Dispatch(() =>
        {
            var button = new Button { Content = "Button" };
            foreach (var name in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                button.Classes.Add(name);
            }

            var window = ThemedTestWindow.Create(theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light, button);
            window.Show();
            window.UpdateLayout();

            // Нажатие без наведения: :pressed есть, :pointerover нет.
            ((IPseudoClasses)button.Classes).Add(":pressed");
            window.UpdateLayout();
            AssertBackground(window, button, pressedBrushKey, classes + " :pressed");
            ((IPseudoClasses)button.Classes).Remove(":pressed");

            button.IsEnabled = false;
            window.UpdateLayout();
            AssertBackground(window, button, disabledBrushKey, classes + " :disabled");

            // Выключенная кнопка под курсором: Avalonia продолжает ставить :pointerover
            // выключенному контролу, и правило наведения не должно перебить выключенное.
            ((IPseudoClasses)button.Classes).Add(":pointerover");
            window.UpdateLayout();
            AssertBackground(window, button, disabledBrushKey, classes + " :disabled :pointerover");
            ((IPseudoClasses)button.Classes).Remove(":pointerover");

            window.Close();
        }, CancellationToken.None);
    }

    private static void AssertBackground(Window window, Button button, string? expectedBrushKey, string what)
    {
        var presenter = button.GetVisualDescendants().OfType<ContentPresenter>().First();
        var actual = presenter.Background as ISolidColorBrush;
        if (expectedBrushKey is null)
        {
            Assert.True(actual is null || actual.Color.A == 0, $"{what}: {actual?.Color}");
            return;
        }

        Assert.True(window.TryFindResource(expectedBrushKey, window.ActualThemeVariant, out var expected));
        Assert.Equal(Assert.IsAssignableFrom<ISolidColorBrush>(expected).Color, actual?.Color);
    }

    /// <summary>
    /// Ссылка подчёркивается под курсором, приглушённая («Очистить» в «Недавних») —
    /// только темнеет: тон отличается цветом, а не поведением.
    /// </summary>
    [Theory]
    [InlineData("mm-link", true)]
    [InlineData("mm-link mm-link-quiet", false)]
    public Task LinkUnderlinesOnlyInTheAccentTone(string classes, bool underlined)
    {
        return _fixture.Session.Dispatch(() =>
        {
            var button = new Button { Content = "Link" };
            foreach (var name in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                button.Classes.Add(name);
            }

            var window = ThemedTestWindow.Create(ThemeVariant.Light, button);
            window.Show();
            window.UpdateLayout();

            ((IPseudoClasses)button.Classes).Add(":pointerover");
            window.UpdateLayout();

            var presenter = button.GetVisualDescendants().OfType<ContentPresenter>().First();
            var decorations = presenter.GetValue(TextBlock.TextDecorationsProperty);
            Assert.Equal(underlined, decorations is { Count: > 0 });

            window.Close();
        }, CancellationToken.None);
    }
}
