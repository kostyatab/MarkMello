using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Иконки интерфейса — только Lucide: геометрии лежат в <c>Themes/Icons.axaml</c>,
/// рисует их <c>LucideIcon</c>. Эти тесты читают исходники разметки и ловят то,
/// что сборка пропускает: ссылку на несуществующую геометрию (DynamicResource
/// молча даёт пустую иконку), иконку без размера (она схлопывается в 0×0) и
/// иконку, нарисованную прямо во view.
/// </summary>
[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed partial class IconMarkupTests
{
    private const double LucideGridSize = 24;
    private const string DividerClass = "mm-setting-divider";

    private static readonly XNamespace AvaloniaNamespace = "https://github.com/avaloniaui";

    // Геометрия и холст во view — всегда самописная иконка.
    private static readonly string[] HandDrawnIconElements = ["Path", "PathIcon", "Canvas", "Polyline", "Polygon", "Arc", "Sector"];

    private readonly AvaloniaHeadlessFixture _fixture;

    public IconMarkupTests(AvaloniaHeadlessFixture fixture) => _fixture = fixture;

    [Fact]
    public Task EveryIconGeometryUsedInMarkupIsDefinedAndParses()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var referenced = ViewAndThemeMarkup()
                .SelectMany(file => GeometryReference().Matches(File.ReadAllText(file)))
                .Select(match => match.Groups["key"].Value)
                .ToHashSet(StringComparer.Ordinal);

            Assert.NotEmpty(referenced);

            var icons = LoadIcons();
            foreach (var key in referenced)
            {
                Assert.True(icons.TryGetResource(key, null, out var value), $"{key} is not defined in Icons.axaml");
                Assert.IsAssignableFrom<Geometry>(value);
            }
        }, CancellationToken.None);
    }

    /// <summary>
    /// Каждая геометрия словаря лежит в сетке Lucide 24×24. Ловит ошибку склейки
    /// нескольких &lt;path&gt; одной иконки: ведущее "m" становится относительным
    /// к предыдущему контуру, и часть иконки уезжает за сетку.
    /// </summary>
    [Fact]
    public Task EveryIconGeometryIsALucideGeometryOnItsGrid()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var icons = LoadIcons();

            Assert.NotEmpty(icons.Keys);
            foreach (var key in icons.Keys)
            {
                Assert.Matches("^Lucide[A-Za-z0-9]+Geometry$", Assert.IsType<string>(key));
                Assert.True(icons.TryGetResource(key, null, out var value));

                var bounds = Assert.IsAssignableFrom<Geometry>(value).Bounds;
                Assert.True(bounds.Width > 0 || bounds.Height > 0, $"{key} is empty");
                Assert.True(
                    bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= LucideGridSize && bounds.Bottom <= LucideGridSize,
                    $"{key} leaves the 24x24 grid: {bounds}");
            }
        }, CancellationToken.None);
    }

    [Fact]
    public void ViewsDrawNoIconsOfTheirOwn()
    {
        var viewsDirectory = Path.Combine(PresentationSourceDirectory(), "Views");
        var views = MarkupIn(viewsDirectory);

        Assert.NotEmpty(views);

        var offences = views
            .SelectMany(file => FindHandDrawnIcons(XDocument.Load(file, LoadOptions.SetLineInfo), Path.GetRelativePath(viewsDirectory, file)))
            .ToList();

        Assert.True(offences.Count == 0, "Icons must be LucideIcon with a geometry from Icons.axaml:" + Environment.NewLine + string.Join(Environment.NewLine, offences));
    }

    /// <summary>
    /// Своего размера у <c>LucideIcon</c> нет: без Width/Height в StackPanel или
    /// Auto-колонке она молча пропадает.
    /// </summary>
    [Fact]
    public void EveryIconInMarkupHasAnExplicitSize()
    {
        var presentation = PresentationSourceDirectory();
        var unsized = ViewAndThemeMarkup()
            .SelectMany(file => FindUnsizedIcons(XDocument.Load(file, LoadOptions.SetLineInfo), Path.GetRelativePath(presentation, file)))
            .ToList();

        Assert.True(unsized.Count == 0, "LucideIcon needs an explicit Width and Height:" + Environment.NewLine + string.Join(Environment.NewLine, unsized));
    }

    [Theory]
    [InlineData("""<Path Data="M0,0 L1,1"/>""")]
    [InlineData("""<Canvas Width="14" Height="14"><Ellipse Width="6" Height="6"/></Canvas>""")]
    [InlineData("""<PathIcon Data="M0,0 L1,1"/>""")]
    [InlineData("""<Polyline Points="0,0 5,5 10,0"/>""")]
    [InlineData("""<Grid Width="14" Height="14"><Ellipse Width="6" Height="6" Stroke="Black"/></Grid>""")]
    [InlineData("""<Ellipse Width="6" Height="6"><Ellipse.Stroke><SolidColorBrush Color="Black"/></Ellipse.Stroke></Ellipse>""")]
    [InlineData("""<Rectangle Width="8" Height="8" Stroke="Black"/>""")]
    [InlineData("""<Line StartPoint="7,0" EndPoint="7,2" Stroke="Black"/>""")]
    [InlineData("""<views:LucideIcon Data="M0,0 L1,1"/>""")]
    [InlineData("""<views:LucideIcon><views:LucideIcon.Data><StreamGeometry>M0,0 L1,1</StreamGeometry></views:LucideIcon.Data></views:LucideIcon>""")]
    [InlineData("""<Button Classes="titlebar"><Path Data="M0,0 L1,1"/></Button>""")]
    public void TheGuardCatchesAnIconDrawnInAView(string markup)
    {
        Assert.NotEmpty(FindHandDrawnIcons(View(markup), "SomeView.axaml"));
    }

    [Theory]
    [InlineData("""<views:LucideIcon Data="{DynamicResource LucideXGeometry}" Width="12" Height="12"/>""")]
    [InlineData("""<Ellipse Classes="mm-tab-dirty" Width="6" Height="6"/>""")]
    [InlineData("""<Ellipse Width="5" Height="5" Fill="{DynamicResource MmAccentBrush}"/>""")]
    [InlineData("""<Line Classes="mm-setting-divider" StartPoint="0,0" EndPoint="1,0"/>""")]
    public void TheGuardLetsLucideIconsDotsAndDividersThrough(string markup)
    {
        Assert.Empty(FindHandDrawnIcons(View(markup), "SomeView.axaml"));
    }

    [Theory]
    [InlineData("""<views:LucideIcon Data="{DynamicResource LucideXGeometry}"/>""", false)]
    [InlineData("""<views:LucideIcon Data="{DynamicResource LucideXGeometry}" Width="12"/>""", false)]
    [InlineData("""<views:LucideIcon Data="{DynamicResource LucideXGeometry}" Width="12" Height="12"/>""", true)]
    public void TheSizeCheckWantsBothWidthAndHeight(string markup, bool sized)
    {
        Assert.Equal(sized, FindUnsizedIcons(View(markup), "SomeView.axaml").Count == 0);
    }

    /// <summary>
    /// Кнопки заголовка окна Windows остаются системными глифами (constitution §9) —
    /// но только они и только в главном окне.
    /// </summary>
    [Fact]
    public void TheGuardAllowsOnlyTheWindowsTitleBarButtons()
    {
        var titleBar = View("""<Button Classes="titlebar close"><Path Data="M0,0 L10,10 M10,0 L0,10"/></Button>""");

        Assert.Empty(FindHandDrawnIcons(titleBar, "MainWindow.axaml"));
        Assert.NotEmpty(FindHandDrawnIcons(titleBar, "SomeView.axaml"));
    }

    private static List<string> FindHandDrawnIcons(XDocument view, string fileName)
    {
        var offences = new List<string>();
        foreach (var element in view.Descendants())
        {
            var name = element.Name.LocalName;
            var line = ((IXmlLineInfo)element).LineNumber;

            if (element.Name.Namespace == AvaloniaNamespace
                && (HandDrawnIconElements.Contains(name) || IsOutlinedShape(element))
                && !IsWindowsTitleBarGlyph(element, fileName))
            {
                offences.Add($"{fileName}:{line}: <{name}>");
            }
            else if (name == "LucideIcon.Data"
                || (name == "LucideIcon" && element.Attribute("Data") is { } data && !IconResource().IsMatch(data.Value)))
            {
                offences.Add($"{fileName}:{line}: LucideIcon with an inline geometry");
            }
        }

        return offences;
    }

    /// <summary>
    /// Простые фигуры бывают и не иконками: залитая точка «не сохранено», разделитель.
    /// Иконки Lucide — обводки, поэтому иконку из фигур выдаёт обводка.
    /// </summary>
    private static bool IsOutlinedShape(XElement element) => element.Name.LocalName switch
    {
        // Линия — всегда обводка; вне иконок это только разделитель.
        "Line" => !HasClass(element, DividerClass),
        "Ellipse" or "Rectangle" => element.Attribute("Stroke") is not null
            || element.Element(AvaloniaNamespace + (element.Name.LocalName + ".Stroke")) is not null,
        _ => false
    };

    private static bool IsWindowsTitleBarGlyph(XElement element, string fileName)
        => fileName == "MainWindow.axaml"
            && element.Ancestors(AvaloniaNamespace + "Button").Any(button => HasClass(button, "titlebar"));

    private static List<string> FindUnsizedIcons(XDocument markup, string fileName)
        => markup.Descendants()
            .Where(element => element.Name.LocalName == "LucideIcon"
                && (element.Attribute("Width") is null || element.Attribute("Height") is null))
            .Select(element => $"{fileName}:{((IXmlLineInfo)element).LineNumber}: LucideIcon without Width/Height")
            .ToList();

    private static bool HasClass(XElement element, string className)
        => (element.Attribute("Classes")?.Value ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains(className);

    private static IEnumerable<string> ViewAndThemeMarkup()
    {
        var presentation = PresentationSourceDirectory();
        return MarkupIn(Path.Combine(presentation, "Views"))
            .Concat(MarkupIn(Path.Combine(presentation, "Themes")));
    }

    // С подпапками: view в Views/<папка>/ проверяется так же, как в корне.
    private static string[] MarkupIn(string directory)
        => Directory.GetFiles(directory, "*.axaml", SearchOption.AllDirectories);

    private static XDocument View(string content)
        => XDocument.Parse(
            $"""<UserControl xmlns="https://github.com/avaloniaui" xmlns:views="using:MarkMello.Presentation.Views">{content}</UserControl>""",
            LoadOptions.SetLineInfo);

    private static ResourceDictionary LoadIcons()
        => Assert.IsType<ResourceDictionary>(
            AvaloniaXamlLoader.Load(new Uri("avares://MarkMello.Presentation/Themes/Icons.axaml")));

    /// <summary>
    /// Исходники берутся из репозитория: тесты запускаются из bin, и путь ищется
    /// подъёмом до MarkMello.sln — одинаково на Windows, macOS и Linux.
    /// </summary>
    private static string PresentationSourceDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MarkMello.sln")))
            {
                return Path.Combine(directory.FullName, "src", "MarkMello.Presentation");
            }
        }

        throw new InvalidOperationException("MarkMello.sln was not found above " + AppContext.BaseDirectory);
    }

    [GeneratedRegex(@"\{(?:Dynamic|Static)Resource\s+(?<key>Lucide[A-Za-z0-9]+Geometry)\s*\}")]
    private static partial Regex GeometryReference();

    [GeneratedRegex(@"^\{(?:Dynamic|Static)Resource\s+Lucide[A-Za-z0-9]+Geometry\s*\}$")]
    private static partial Regex IconResource();
}
