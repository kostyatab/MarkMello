using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media.TextFormatting;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Интерфейс набран одной гарнитурой — Inter из сборки. Подпись, которой шрифт
/// не задан явно, берёт его у темы Fluent, а та ссылается на незарегистрированный
/// fonts:Inter и без нашей подмены падает в системный шрифт.
/// </summary>
[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class UiFontFamilyTests
{
    private readonly AvaloniaHeadlessFixture _fixture;

    public UiFontFamilyTests(AvaloniaHeadlessFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public Task PlainTextBlockIsSetInInter()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var label = new TextBlock { Text = "Тема" };

            AssertSetInInter(label, label);
        }, CancellationToken.None);
    }

    [Fact]
    public Task CardSegmentLabelIsSetInInter()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var label = new TextBlock { Text = "Средняя" };
            var segment = new ToggleButton { Content = label, IsChecked = true };
            segment.Classes.Add("mm-card-segment");

            AssertSetInInter(segment, label);
        }, CancellationToken.None);
    }

    [Fact]
    public Task StringContentOfButtonIsSetInInter()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var button = new Button { Content = "Настройки…" };

            AssertSetInInter(button, () => button.GetVisualDescendants().OfType<TextBlock>().Single());
        }, CancellationToken.None);
    }

    private static void AssertSetInInter(Control content, TextBlock label)
        => AssertSetInInter(content, () => label);

    private static void AssertSetInInter(Control content, Func<TextBlock> findLabel)
    {
        var window = ThemedTestWindow.Create(ThemeVariant.Light, content);
        window.Show();
        window.UpdateLayout();

        var label = findLabel();
        var runs = label.TextLayout.TextLines.SelectMany(line => line.TextRuns).OfType<ShapedTextRun>().ToList();
        Assert.NotEmpty(runs);
        foreach (var run in runs)
        {
            var glyphTypeface = run.GlyphRun.GlyphTypeface;
            var familyName = string.IsNullOrEmpty(glyphTypeface.TypographicFamilyName)
                ? glyphTypeface.FamilyName
                : glyphTypeface.TypographicFamilyName;
            Assert.Equal("Inter", familyName);
        }

        window.Close();
    }
}
