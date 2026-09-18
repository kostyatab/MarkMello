using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using MarkMello.Presentation.Views;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Шеврон ComboBox языка — Lucide вместо залитого глифа Fluent: шаблонная часть
/// та же (её цвет по-прежнему задают состояния Fluent), и список открывается
/// как раньше.
/// </summary>
[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class LanguageSelectChevronTests
{
    private static readonly string[] Languages = ["English", "Русский"];

    private readonly AvaloniaHeadlessFixture _fixture;

    public LanguageSelectChevronTests(AvaloniaHeadlessFixture fixture) => _fixture = fixture;

    [Fact]
    public Task LanguageSelectShowsALucideChevronAndStillOpens()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var select = new ComboBox
            {
                Classes = { "mm-language-select" },
                ItemsSource = Languages,
                SelectedIndex = 0
            };
            var window = ThemedTestWindow.Create(ThemeVariant.Light, select);
            window.Show();
            window.UpdateLayout();

            var glyph = select.GetVisualDescendants().OfType<PathIcon>().Single(icon => icon.Name == "DropDownGlyph");
            var chevron = Assert.Single(glyph.GetVisualDescendants().OfType<LucideIcon>());

            Assert.True(window.TryFindResource("LucideChevronDownGeometry", out var geometry));
            Assert.Same(geometry, chevron.Data);
            Assert.Same(glyph.Foreground, chevron.Foreground);
            Assert.Equal(new Size(12, 12), chevron.Bounds.Size);

            var centre = select.TranslatePoint(new Point(select.Bounds.Width / 2, select.Bounds.Height / 2), window)!.Value;
            window.MouseDown(centre, MouseButton.Left);
            window.MouseUp(centre, MouseButton.Left);

            Assert.True(select.IsDropDownOpen);

            window.Close();
        }, CancellationToken.None);
    }
}
