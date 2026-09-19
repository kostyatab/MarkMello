using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using MarkMello.Application.UseCases;
using MarkMello.Domain;
using MarkMello.Domain.Workspace;
using MarkMello.Presentation.Localization;
using MarkMello.Presentation.ViewModels;
using MarkMello.Presentation.Views;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Полоса вкладок по спецификации A-Tabs (ADR-0009 Rule 3): плашки с заливкой по
/// состоянию, иконка или крестик слева, точка несохранённого справа, «+» после
/// вкладок. Биндинги на команды идут через $parent[ItemsControl] и в unit-тестах
/// view-model не проверяются вовсе.
/// </summary>
[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class TabStripViewTests
{
    private const string OutsideButtonName = "Outside";

    private static readonly string Root = TestPaths.At("docs");
    private static readonly string First = TestPaths.At("docs", "first.md");
    private static readonly string Second = TestPaths.At("docs", "second.md");
    private static readonly string LongName = TestPaths.At("docs", "installation-guide-for-windows-and-macos.md");
    private static readonly string GuidesReadme = TestPaths.At("docs", "guides", "README.md");
    private static readonly string ApiReadme = TestPaths.At("docs", "api", "README.md");

    // Шестнадцать вкладок: при 700 px в «ещё N» уходят двенадцать — двузначное число, самая широкая подпись.
    private static readonly string[] ManyDocuments = [.. Enumerable.Range(1, 16).Select(index => TestPaths.At("docs", $"chapter-{index}.md"))];

    private readonly AvaloniaHeadlessFixture _fixture;

    public TabStripViewTests(AvaloniaHeadlessFixture fixture) => _fixture = fixture;

    [Fact]
    public Task StripShowsTheVisibleTabsInOrder()
    {
        return _fixture.RunAsync(async () =>
        {
            var viewModel = CreateViewModel();
            await viewModel.OpenPathAsync(First);
            await viewModel.OpenPathAsync(Second);
            var window = Show(viewModel, ThemeVariant.Light);

            var host = window.GetVisualDescendants().OfType<ItemsControl>().Single(control => control.Name == "VisibleTabsHost");
            Assert.Same(viewModel.OpenDocuments.VisibleTabs, host.ItemsSource);
            Assert.Equal(
                ["first.md", "second.md"],
                Tabs(window).Select(tab => ((DocumentTabViewModel)tab.DataContext!).Title));

            window.Close();
        });
    }

    /// <summary>
    /// Вкладка — плашка 180×30 с радиусом 8 и без рамок; заливка идёт от состояния:
    /// покой — MmTabBrush, наведение — MmTabHoverBrush, активная — MmTabActiveBrush.
    /// Имя — 13 px, у активной — 500 и цвет текста, у остальных — Soft.
    /// </summary>
    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public Task TabIsAFilledPillThatFollowsItsState(string theme)
    {
        return _fixture.RunAsync(async () =>
        {
            var viewModel = CreateViewModel();
            await viewModel.OpenPathAsync(First);
            await viewModel.OpenPathAsync(Second);
            var window = Show(viewModel, theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light);

            var inactive = Tab(window, "first.md");
            var active = Tab(window, "second.md");

            foreach (var tab in new[] { inactive, active })
            {
                Assert.Equal(new Size(180, 30), tab.Bounds.Size);
                Assert.Equal(new CornerRadius(8), tab.CornerRadius);
                Assert.Equal(default, tab.BorderThickness);
                Assert.Equal(13, Name(tab).FontSize);
                Assert.Equal(TextTrimming.CharacterEllipsis, Name(tab).TextTrimming);
            }

            Assert.Same(Resource(window, "MmTabActiveBrush"), active.Background);
            Assert.Same(Resource(window, "MmTextBrush"), Name(active).Foreground);
            Assert.Equal(FontWeight.Medium, Name(active).FontWeight);

            Assert.Same(Resource(window, "MmTabBrush"), inactive.Background);
            Assert.Same(Resource(window, "MmTextSoftBrush"), Name(inactive).Foreground);
            Assert.Equal(FontWeight.Normal, Name(inactive).FontWeight);

            ((IPseudoClasses)inactive.Classes).Add(":pointerover");
            ((IPseudoClasses)active.Classes).Add(":pointerover");

            Assert.Same(Resource(window, "MmTabHoverBrush"), inactive.Background);
            Assert.Same(Resource(window, "MmTextSoftBrush"), Name(inactive).Foreground);
            Assert.Same(Resource(window, "MmTabActiveBrush"), active.Background);

            // Акцентной полосы у активной вкладки больше нет.
            Assert.DoesNotContain(
                window.GetVisualDescendants().OfType<Border>(),
                border => border.Classes.Contains("mm-tab-active-bar"));

            window.Close();
        });
    }

    /// <summary>
    /// Слева — иконка файла 16; у активной вкладки и под курсором на её месте крестик 14.
    /// Точка несохранённого справа, и крестик её не подменяет.
    /// </summary>
    [Fact]
    public Task CloseButtonTakesTheIconPlaceAndTheDotStaysOnTheRight()
    {
        return _fixture.RunAsync(async () =>
        {
            var viewModel = CreateViewModel();
            await viewModel.OpenPathAsync(First);
            await viewModel.OpenPathAsync(Second);
            var window = Show(viewModel, ThemeVariant.Light);

            var inactive = Tab(window, "first.md");
            var active = Tab(window, "second.md");

            Assert.True(FileIcon(inactive).IsEffectivelyVisible);
            Assert.False(CloseButton(inactive).IsVisible);
            Assert.Equal(new Size(16, 16), FileIcon(inactive).GetVisualDescendants().OfType<LucideIcon>().First(icon => icon.IsVisible).Bounds.Size);

            Assert.False(FileIcon(active).IsVisible);
            Assert.True(CloseButton(active).IsEffectivelyVisible);
            Assert.Equal(new Size(14, 14), CloseButton(active).GetVisualDescendants().OfType<LucideIcon>().Single().Bounds.Size);

            ((IPseudoClasses)inactive.Classes).Add(":pointerover");
            window.UpdateLayout();

            Assert.False(FileIcon(inactive).IsVisible);
            Assert.True(CloseButton(inactive).IsEffectivelyVisible);

            // Крестик слева, точка справа: у активной несохранённой вкладки видны оба.
            Assert.False(Dot(active).IsVisible);
            ((DocumentTabViewModel)active.DataContext!).IsDirty = true;
            window.UpdateLayout();

            Assert.True(Dot(active).IsEffectivelyVisible);
            Assert.True(CloseButton(active).IsEffectivelyVisible);
            Assert.Equal(new Size(7, 7), Dot(active).Bounds.Size);
            Assert.Same(Resource(window, "MmAccentBrush"), Dot(active).Fill);
            Assert.True(Dot(active).TranslatePoint(default, active)!.Value.X > Name(active).TranslatePoint(default, active)!.Value.X);

            window.Close();
        });
    }

    [Fact]
    public Task ClosingATabLeavesTheOther()
    {
        return _fixture.RunAsync(async () =>
        {
            var viewModel = CreateViewModel();
            await viewModel.OpenPathAsync(First);
            await viewModel.OpenPathAsync(Second);
            var window = Show(viewModel, ThemeVariant.Light);

            var close = CloseButton(Tab(window, "second.md"));
            close.Command!.Execute(close.CommandParameter);
            window.UpdateLayout();

            Assert.Equal(["first.md"], viewModel.OpenDocuments.Tabs.Select(tab => tab.Title));
            window.Close();
        });
    }

    /// <summary>Длинное имя не гаснет маской, а обрывается многоточием в конце.</summary>
    [Fact]
    public Task LongNameEndsWithAnEllipsis()
    {
        return _fixture.RunAsync(async () =>
        {
            var viewModel = CreateViewModel();
            await viewModel.OpenPathAsync(LongName);
            var window = Show(viewModel, ThemeVariant.Light);

            var name = Name(Tab(window, "installation-guide-for-windows-and-macos.md"));

            Assert.True(name.TextLayout.TextLines.Single().HasCollapsed);

            window.Close();
        });
    }

    /// <summary>
    /// К имени одноимённого файла дописывается « · папка» тусклым цветом, к удалённому
    /// с диска — «(удалён)» курсивом; иконка удалённого — перечёркнутый файл.
    /// </summary>
    [Fact]
    public Task NameCarriesTheFolderSuffixAndTheDeletedMark()
    {
        return _fixture.RunAsync(async () =>
        {
            var viewModel = CreateViewModel();
            await viewModel.OpenPathAsync(GuidesReadme);
            await viewModel.OpenPathAsync(ApiReadme);
            var guides = viewModel.OpenDocuments.FindByPath(GuidesReadme)!;
            guides.StateSuffix = "(deleted)";
            var window = Show(viewModel, ThemeVariant.Light);

            var tab = Tabs(window).Single(candidate => ReferenceEquals(candidate.DataContext, guides));
            var runs = Name(tab).Inlines!.OfType<Run>().ToList();

            Assert.Equal(["README.md", " · guides", " (deleted)"], runs.Select(run => run.Text));
            Assert.Same(Resource(window, "MmTextFaintBrush"), runs[1].Foreground);
            Assert.Equal(FontStyle.Italic, runs[2].FontStyle);

            // Экранный диктор читает то же, что видно: без « · папка» одноимённые вкладки не различить.
            Assert.Equal("README.md · guides (deleted)", AutomationProperties.GetName(tab));
            var api = Tabs(window).Single(candidate => ReferenceEquals(candidate.DataContext, viewModel.OpenDocuments.FindByPath(ApiReadme)));
            Assert.Equal("README.md · api", AutomationProperties.GetName(api));

            Assert.True(window.TryFindResource("LucideFileXGeometry", out var fileX));
            var shownIcon = FileIcon(tab).GetVisualDescendants().OfType<LucideIcon>().Single(icon => icon.IsVisible);
            Assert.Same(fileX, shownIcon.Data);

            window.Close();
        });
    }

    /// <summary>
    /// «+» стоит сразу после вкладок и делает то же, что ⌘N; в папке без вкладок
    /// в строке остаётся только он.
    /// </summary>
    [Fact]
    public Task NewDocumentButtonFollowsTheTabs()
    {
        return _fixture.RunAsync(async () =>
        {
            var viewModel = CreateViewModel();
            await viewModel.OpenFolderPathAsync(Root);
            await viewModel.OpenPathAsync(First);
            var window = Show(viewModel, ThemeVariant.Light);

            var plus = window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "NewDocumentButton");
            var lastTab = Tabs(window).Last();

            Assert.Same(viewModel.CreateNewDocumentCommand, plus.Command);
            Assert.Equal("New document (Ctrl+N)", ToolTip.GetTip(plus));
            Assert.Equal(new Size(30, 30), plus.Bounds.Size);
            Assert.Equal(
                lastTab.TranslatePoint(new Point(lastTab.Bounds.Width, 0), window)!.Value.X + OpenDocumentsViewModel.TabSpacing,
                plus.TranslatePoint(default, window)!.Value.X);

            foreach (var tab in viewModel.OpenDocuments.Tabs.ToList())
            {
                await viewModel.OpenDocuments.CloseCommand.ExecuteAsync(tab);
            }

            window.UpdateLayout();

            Assert.True(viewModel.ShowsTabStrip);
            Assert.Empty(Tabs(window));
            Assert.True(plus.IsEffectivelyVisible);
            Assert.Equal(0, plus.TranslatePoint(default, window.GetVisualDescendants().OfType<TabStripView>().Single())!.Value.X);

            window.Close();
        });
    }

    /// <summary>
    /// Когда вкладки не помещаются даже по 120, лишние уходят в «ещё N», а «ещё N» и «+»
    /// остаются внутри полосы — запаса под них хватает, «+» не уезжает под кнопки справа.
    /// </summary>
    [Theory]
    [InlineData(AppLanguage.English)]
    [InlineData(AppLanguage.Russian)]
    public Task OverflowKeepsTheButtonsInsideTheStrip(AppLanguage language)
    {
        return _fixture.RunAsync(async () =>
        {
            var viewModel = CreateViewModel(language);
            foreach (var path in ManyDocuments)
            {
                await viewModel.OpenPathAsync(path);
            }

            var window = Show(viewModel, ThemeVariant.Light, width: 700);
            var strip = window.GetVisualDescendants().OfType<TabStripView>().Single();
            var overflow = window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "TabsOverflowButton");
            var plus = window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "NewDocumentButton");

            Assert.True(viewModel.OpenDocuments.HasOverflow);
            Assert.True(overflow.IsEffectivelyVisible);
            Assert.All(Tabs(window), tab => Assert.Equal(OpenDocumentsViewModel.MinimumTabWidth, tab.Bounds.Width));
            Assert.Equal(30, overflow.Bounds.Height);
            Assert.True(
                plus.TranslatePoint(new Point(plus.Bounds.Width, 0), strip)!.Value.X <= strip.Bounds.Width,
                $"«+» ends at {plus.TranslatePoint(new Point(plus.Bounds.Width, 0), strip)!.Value.X}, strip is {strip.Bounds.Width}; overflow button is {overflow.Bounds.Width}");

            window.Close();
        });
    }

    /// <summary>
    /// Вкладка встаёт в обход по Tab и показывает кольцо фокуса; Enter её открывает.
    /// Клик мышью фокус вкладке не отдаёт — он остаётся там, где был, в документе.
    /// </summary>
    [Fact]
    public Task KeyboardFocusShowsARingAndAClickDoesNotTakeFocus()
    {
        return _fixture.RunAsync(async () =>
        {
            var viewModel = CreateViewModel();
            await viewModel.OpenPathAsync(First);
            await viewModel.OpenPathAsync(Second);
            var window = Show(viewModel, ThemeVariant.Light);

            var first = Tab(window, "first.md");
            var second = Tab(window, "second.md");

            Assert.True(first.Focus(NavigationMethod.Tab));
            window.UpdateLayout();
            Assert.True(FocusRing(first).IsVisible);

            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.Equal(First, viewModel.OpenDocuments.ActiveTab!.Path);

            // Фокус где-то вне полосы — как у документа, который читают.
            var outside = window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == OutsideButtonName);
            Assert.True(outside.Focus());

            var centre = second.TranslatePoint(new Point(second.Bounds.Width / 2, second.Bounds.Height / 2), window)!.Value;
            window.MouseDown(centre, MouseButton.Left);
            window.MouseUp(centre, MouseButton.Left);
            window.UpdateLayout();

            Assert.Equal(Second, viewModel.OpenDocuments.ActiveTab!.Path);
            Assert.Same(outside, window.FocusManager!.GetFocusedElement());
            Assert.True(second.Focusable);
            Assert.False(FocusRing(second).IsVisible);

            window.Close();
        });
    }

    private static Window Show(ShellViewModel viewModel, ThemeVariant theme, double width = 900)
    {
        // Кнопка под полосой — чтобы тесту было куда положить посторонний фокус.
        var window = ThemedTestWindow.Create(
            theme,
            new StackPanel { Children = { new TabStripView(), new Button { Name = OutsideButtonName } } });
        window.Resources.MergedDictionaries.Add(
            Assert.IsAssignableFrom<IResourceProvider>(
                Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(new Uri("avares://MarkMello.Presentation/Themes/Typography.axaml"))));
        window.Width = width;
        window.DataContext = viewModel;
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static List<Border> Tabs(Window window)
        => window.GetVisualDescendants()
            .OfType<Border>()
            .Where(border => border.Classes.Contains("mm-tab") && border.IsVisible)
            .ToList();

    private static Border Tab(Window window, string title)
        => Tabs(window).Single(tab => ((DocumentTabViewModel)tab.DataContext!).Title == title);

    private static TextBlock Name(Border tab)
        => tab.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Classes.Contains("mm-tab-name"));

    private static Panel FileIcon(Border tab)
        => tab.GetVisualDescendants().OfType<Panel>().Single(panel => panel.Classes.Contains("mm-tab-file-icon"));

    private static Button CloseButton(Border tab)
        => tab.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("mm-tab-close"));

    private static Ellipse Dot(Border tab)
        => tab.GetVisualDescendants().OfType<Ellipse>().Single(dot => dot.Classes.Contains("mm-tab-dot"));

    private static Border FocusRing(Border tab)
        => tab.GetVisualDescendants().OfType<Border>().Single(ring => ring.Classes.Contains("mm-tab-focus-ring"));

    private static object? Resource(Window window, string key)
    {
        Assert.True(window.TryFindResource(key, window.ActualThemeVariant, out var value), key);
        return value;
    }

    private static ShellViewModel CreateViewModel(AppLanguage language = AppLanguage.English)
    {
        var loader = new StubDocumentLoader();
        foreach (var path in new[] { First, Second, LongName, GuidesReadme, ApiReadme }.Concat(ManyDocuments))
        {
            loader.Sources[path] = new MarkdownSource(path, System.IO.Path.GetFileName(path), "# doc");
        }

        var fileSystem = new FakeWorkspaceFileSystem();
        fileSystem.AddDirectory(
            Root,
            WorkspaceEntry.ForFile(First, "first.md"),
            WorkspaceEntry.ForFile(Second, "second.md"));

        return new ShellViewModel(
            new OpenDocumentUseCase(loader),
            new SaveDocumentUseCase(new RecordingDocumentSaver()),
            new StubFilePicker(),
            new StubCommandLineActivation(),
            new LocalizationService(language),
            new InMemorySettingsStore(),
            new RecordingThemeService(),
            new RecordingStartupMetrics(),
            new RenderMarkdownDocumentUseCase(new TestMarkdownRenderer(), new FakeDiagramRenderService()),
            new StubUpdateService(),
            new OpenFolderUseCase(fileSystem),
            new ExpandFolderNodeUseCase(fileSystem),
            new SearchWorkspaceFilesUseCase(fileSystem),
            new WorkspaceFileOperationsUseCase(fileSystem, new FakePlatformServices()),
            new FakePlatformServices(),
            static () => new FakeWorkspaceWatcher(),
            new RecordingWindowLauncher());
    }
}
