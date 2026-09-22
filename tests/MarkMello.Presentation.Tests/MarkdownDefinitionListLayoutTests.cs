using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using MarkMello.Domain;
using MarkMello.Infrastructure.Markdown;
using MarkMello.Presentation.Views;
using MarkMello.Presentation.Views.Markdown;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Список определений — как списки: термин полужирным прямым, определение с
/// отступом 1.6em в .15em от термина, следующий термин — через .75em, определения
/// одного термина — через .25em, абзацы определения — через .5em.
/// </summary>
[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class MarkdownDefinitionListLayoutTests
{
    private const double Tolerance = 0.5;

    // Дробные просветы в em раскладка округляет до пикселя.
    private const double PixelTolerance = 1;

    private const string Markdown = """
        Before

        Markdown
        :   Plain text markup.

        Front matter
        :   Metadata at the top.
        :   Second definition.

        Long term
        :   First paragraph.

            Second paragraph.
        """;

    private readonly AvaloniaHeadlessFixture _fixture;

    public MarkdownDefinitionListLayoutTests(AvaloniaHeadlessFixture fixture) => _fixture = fixture;

    [Fact]
    public Task DefinitionListFollowsTheSpacingOfLists()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var (window, view) = Show(Markdown);

            Assert.Equal(14 * 1, Gap(view, "Before", "Markdown"), PixelTolerance);
            Assert.Equal(14 * 0.15, Gap(view, "Markdown", "Plain text markup."), PixelTolerance);
            Assert.Equal(14 * 0.75, Gap(view, "Plain text markup.", "Front matter"), PixelTolerance);
            Assert.Equal(14 * 0.25, Gap(view, "Metadata at the top.", "Second definition."), PixelTolerance);
            Assert.Equal(14 * 0.75, Gap(view, "Second definition.", "Long term"), PixelTolerance);
            Assert.Equal(14 * 0.5, Gap(view, "First paragraph.", "Second paragraph."), PixelTolerance);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public Task DefinitionIsIndentedAndTheTermIsSemiBoldUpright()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var (window, view) = Show(Markdown);

            var term = Text(view, "Markdown");
            var definition = Text(view, "Plain text markup.");
            Assert.Equal(Left(term, view) + 14 * 1.6, Left(definition, view), Tolerance);
            Assert.Equal(Left(Text(view, "Before"), view), Left(term, view), Tolerance);
            Assert.Equal(Left(definition, view), Left(Text(view, "Second paragraph."), view), Tolerance);

            Assert.Equal(FontWeight.SemiBold, term.BaseFontWeight);
            Assert.Equal(FontStyle.Normal, term.BaseFontStyle);
            Assert.Equal(FontWeight.Normal, definition.BaseFontWeight);

            window.Close();
        }, CancellationToken.None);
    }

    private static (Window Window, MarkdownDocumentView View) Show(string markdown)
    {
        var view = new MarkdownDocumentView
        {
            ReadingPreferences = ReadingPreferences.Default with { FontSize = 14 },
            Document = new MarkdigMarkdownDocumentRenderer().Render(markdown)
        };

        var window = ThemedTestWindow.Create(ThemeVariant.Light, view);
        window.Width = 600;
        window.Height = 800;
        window.Show();
        window.UpdateLayout();
        return (window, view);
    }

    /// <summary>Расстояние от низа одного текста до верха следующего.</summary>
    private static double Gap(MarkdownDocumentView view, string upper, string lower)
    {
        var upperText = Text(view, upper);
        var lowerText = Text(view, lower);
        return lowerText.TranslatePoint(default, view)!.Value.Y
            - upperText.TranslatePoint(new Point(0, upperText.Bounds.Height), view)!.Value.Y;
    }

    private static double Left(Control control, Visual relativeTo)
        => control.TranslatePoint(default, relativeTo)!.Value.X;

    private static MarkdownSelectionTextFragment Text(MarkdownDocumentView view, string text)
        => view.GetVisualDescendants().OfType<MarkdownSelectionTextFragment>()
            .Single(fragment => fragment.StyledText.Text == text);
}
