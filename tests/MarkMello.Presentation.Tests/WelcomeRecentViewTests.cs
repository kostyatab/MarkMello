using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MarkMello.Domain;
using MarkMello.Domain.Recent;
using MarkMello.Presentation.Views;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Блок «Недавние» стартового экрана (A-Welcome): пустой список блока не показывает,
/// строки проходятся Tab, Enter открывает запись.
/// </summary>
[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class WelcomeRecentViewTests
{
    private static readonly string Notes = TestPaths.At("docs", "notes.md");
    private static readonly string Other = TestPaths.At("elsewhere", "other.md");

    private readonly AvaloniaHeadlessFixture _fixture;

    public WelcomeRecentViewTests(AvaloniaHeadlessFixture fixture) => _fixture = fixture;

    [Fact]
    public Task EmptyListHasNoRecentBlock()
    {
        return _fixture.RunAsync(async () =>
        {
            var harness = RecentItemsTests.CreateHarness();
            await harness.ViewModel.InitializeAsync();
            var (window, view) = Show(harness, ThemeVariant.Light);

            Assert.False(view.GetControl<Border>("RecentBlock").IsVisible);
            window.Close();
        });
    }

    [Fact]
    public Task RowsFollowTheListAndBlockUsesTabColourInBothThemes()
    {
        return _fixture.RunAsync(async () =>
        {
            foreach (var (theme, colour) in new[] { (ThemeVariant.Light, "#F3EFEA"), (ThemeVariant.Dark, "#2A2522") })
            {
                var harness = CreateHarnessWithTwoEntries();
                await harness.ViewModel.InitializeAsync();
                var (window, view) = Show(harness, theme);

                var block = view.GetControl<Border>("RecentBlock");
                Assert.True(block.IsVisible);
                Assert.Equal(Color.Parse(colour), Assert.IsAssignableFrom<ISolidColorBrush>(block.Background).Color);
                Assert.Equal(2, Rows(view).Count);
                window.Close();
            }
        });
    }

    [Fact]
    public Task TabMovesThroughRowsAndEnterOpensTheFocusedOne()
    {
        return _fixture.RunAsync(async () =>
        {
            var harness = CreateHarnessWithTwoEntries();
            harness.Loader.Sources[Notes] = new MarkdownSource(Notes, "notes.md", "# notes");
            await harness.ViewModel.InitializeAsync();
            var (window, view) = Show(harness, ThemeVariant.Light);

            view.GetControl<Button>("RecentClearButton").Focus(NavigationMethod.Tab);
            PressKey(window, Key.Tab, PhysicalKey.Tab);
            Assert.Same(Rows(view)[0], window.FocusManager!.GetFocusedElement());

            PressKey(window, Key.Tab, PhysicalKey.Tab);
            Assert.Same(Rows(view)[1], window.FocusManager!.GetFocusedElement());

            PressKey(window, Key.Enter, PhysicalKey.Enter);
            for (var attempt = 0; attempt < 50 && harness.ViewModel.CurrentDocumentPath is null; attempt++)
            {
                await Task.Delay(5);
                Dispatcher.UIThread.RunJobs();
            }

            Assert.Equal(Notes, harness.ViewModel.CurrentDocumentPath);
            window.Close();
        });
    }

    /// <summary>
    /// Быстрый путь: окно, которое открывает файл из Finder или командной строки, стартовый
    /// экран не строит вовсе — ни на первом кадре, ни после.
    /// </summary>
    [Fact]
    public Task StartupWithFileNeverBuildsWelcomeScreen()
    {
        return _fixture.RunAsync(async () =>
        {
            var harness = CreateHarnessWithTwoEntries();
            harness.CommandLine.ActivationPath = Other;
            var window = ShowMainWindow(harness);

            Assert.Empty(window.GetVisualDescendants().OfType<WelcomeView>());

            await harness.ViewModel.InitializeAsync();
            Render(window);

            Assert.Empty(window.GetVisualDescendants().OfType<WelcomeView>());
            Assert.Equal(0, harness.Probe.CallCount);
            window.Hide();
        });
    }

    [Fact]
    public Task StartupWithoutArgumentsBuildsWelcomeScreenOnce()
    {
        return _fixture.RunAsync(async () =>
        {
            var harness = CreateHarnessWithTwoEntries();
            var window = ShowMainWindow(harness);

            await harness.ViewModel.InitializeAsync();
            Render(window);

            Assert.Single(window.GetVisualDescendants().OfType<WelcomeView>());

            // Диск спрашивается уже после отрисовки стартового экрана.
            for (var attempt = 0; attempt < 50 && harness.Probe.CallCount == 0; attempt++)
            {
                await Task.Delay(5);
                Render(window);
            }

            Assert.Equal(1, harness.Probe.CallCount);
            window.Hide();
        });
    }

    /// <summary>
    /// В низком окне стартовый экран с полным списком прокручивается, а не обрезается:
    /// логотип сверху и подсказка снизу остаются достижимыми.
    /// </summary>
    [Fact]
    public Task FullListInLowWindowScrollsInsteadOfClipping()
    {
        return _fixture.RunAsync(async () =>
        {
            var harness = RecentItemsTests.CreateHarness();
            harness.Settings.Recent = Enumerable.Range(0, RecentEntryList.Limit)
                .Select(index => new RecentEntry(TestPaths.At("docs", $"{index}.md"), RecentEntryKind.File, RecentItemsTests.Now))
                .ToList();
            await harness.ViewModel.InitializeAsync();
            var (window, view) = Show(harness, ThemeVariant.Light);
            window.Height = 400;
            Render(window);

            var scroller = view.GetVisualDescendants().OfType<ScrollViewer>().First();
            var content = Assert.IsAssignableFrom<Control>(scroller.Content);
            Assert.True(scroller.Extent.Height > scroller.Viewport.Height);
            Assert.True(content.TranslatePoint(default, scroller)!.Value.Y >= 0);
            window.Close();
        });
    }

    [Fact]
    public Task ContentStaysCentredInTallWindow()
    {
        return _fixture.RunAsync(async () =>
        {
            var harness = CreateHarnessWithTwoEntries();
            await harness.ViewModel.InitializeAsync();
            var (window, view) = Show(harness, ThemeVariant.Light);

            var scroller = view.GetVisualDescendants().OfType<ScrollViewer>().First();
            var content = Assert.IsAssignableFrom<Control>(scroller.Content);
            var top = content.TranslatePoint(default, scroller)!.Value.Y;
            var bottom = scroller.Bounds.Height - top - content.Bounds.Height;
            Assert.True(top > 24);
            Assert.InRange(top - bottom, -2, 2);
            window.Close();
        });
    }

    /// <summary>
    /// Кнопку зажали и увели курсор: она остаётся нажатой, но уже не под курсором.
    /// Раньше у основной кнопки и «Очистить» не было цвета на этот случай, и Fluent
    /// закрашивал их серым.
    /// </summary>
    [Fact]
    public Task PressedButtonsKeepTheirColourWhenPointerLeaves()
    {
        return _fixture.RunAsync(async () =>
        {
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                var harness = CreateHarnessWithTwoEntries();
                await harness.ViewModel.InitializeAsync();
                var (window, view) = Show(harness, theme);

                var buttons = view.GetVisualDescendants().OfType<Button>().ToList();
                var primary = buttons.Single(static button => button.Classes.Contains("mm-dialog-primary"));
                var chip = buttons.First(static button => button.Classes.Contains("mm-dialog-secondary"));
                var clear = view.GetControl<Button>("RecentClearButton");

                AssertPressedBackground(window, primary, "MmTextBrush");
                AssertPressedBackground(window, chip, "MmTabHoverBrush");
                AssertPressedBackground(window, clear, null);
                window.Close();
            }
        });
    }

    private static void AssertPressedBackground(Window window, Button button, string? expectedBrushKey)
    {
        ((IPseudoClasses)button.Classes).Set(":pressed", true);
        Render(window);

        var presenter = button.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().First();
        var actual = presenter.Background as ISolidColorBrush;
        if (expectedBrushKey is null)
        {
            Assert.True(actual is null || actual.Color.A == 0, $"{button.Name}: {actual?.Color}");
        }
        else
        {
            Assert.True(window.TryFindResource(expectedBrushKey, window.ActualThemeVariant, out var expected));
            Assert.Equal(Assert.IsAssignableFrom<ISolidColorBrush>(expected).Color, actual?.Color);
        }

        ((IPseudoClasses)button.Classes).Set(":pressed", false);
        Render(window);
    }

    private static MainWindow ShowMainWindow(RecentItemsTests.RecentHarness harness)
    {
        var window = new MainWindow { RequestedThemeVariant = ThemeVariant.Light };
        window.Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
        window.Styles.Add(Assert.IsAssignableFrom<IStyle>(LoadTheme("Controls.axaml")));
        window.Resources.MergedDictionaries.Add(Assert.IsAssignableFrom<Avalonia.Controls.IResourceProvider>(LoadTheme("Colors.axaml")));
        window.Resources.MergedDictionaries.Add(Assert.IsAssignableFrom<Avalonia.Controls.IResourceProvider>(LoadTheme("Icons.axaml")));
        window.DataContext = harness.ViewModel;
        window.Show();
        Render(window);
        return window;
    }

    private static object LoadTheme(string themeFile)
        => Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(new Uri("avares://MarkMello.Presentation/Themes/" + themeFile));

    private static RecentItemsTests.RecentHarness CreateHarnessWithTwoEntries()
    {
        var harness = RecentItemsTests.CreateHarness();
        harness.Settings.Recent =
        [
            new RecentEntry(Other, RecentEntryKind.File, RecentItemsTests.Now),
            new RecentEntry(Notes, RecentEntryKind.File, RecentItemsTests.Now.AddDays(-2))
        ];
        return harness;
    }

    private static List<Button> Rows(WelcomeView view)
        => view.GetVisualDescendants().OfType<Button>().Where(static button => button.Classes.Contains("recent-row")).ToList();

    private static (Window Window, WelcomeView View) Show(RecentItemsTests.RecentHarness harness, ThemeVariant theme)
    {
        var view = new WelcomeView { DataContext = harness.ViewModel };
        var window = ThemedTestWindow.Create(theme, view);
        window.Width = 900;
        window.Height = 800;
        window.Show();
        Render(window);
        return (window, view);
    }

    private static void PressKey(Window window, Key key, PhysicalKey physicalKey)
    {
        window.KeyPress(key, RawInputModifiers.None, physicalKey, null);
        Render(window);
    }

    private static void Render(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}
