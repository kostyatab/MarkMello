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
/// состоянию, иконка или крестик слева, точка несохранённого в правом верхнем углу, «+» после
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
                Assert.Equal(TextTrimming.None, Name(tab).TextTrimming);
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
    /// Слева — иконка файла 16 в ячейке 18; у активной вкладки и под курсором на её месте
    /// крестик 14. Имя — через 4 px от ячейки. Точка несохранённого — 6 px в правом
    /// верхнем углу вкладки; крестик её не подменяет, и имя от неё не сдвигается.
    /// </summary>
    [Fact]
    public Task CloseButtonTakesTheIconPlaceAndTheDotSitsInTheTopRightCorner()
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
            Assert.Equal(new Rect(8, 6, 18, 18), IconCell(active));

            ((IPseudoClasses)inactive.Classes).Add(":pointerover");
            window.UpdateLayout();

            Assert.False(FileIcon(inactive).IsVisible);
            Assert.True(CloseButton(inactive).IsEffectivelyVisible);

            Assert.False(Dot(active).IsVisible);
            var cleanName = NameBox(active);

            // 8 · ячейка 18 · 4 · имя · 8.
            Assert.Equal(30, cleanName.X);
            Assert.Equal(active.Bounds.Width - 8, cleanName.Right);

            ((DocumentTabViewModel)active.DataContext!).IsDirty = true;
            window.UpdateLayout();

            // Крестик и точка видны вместе; точка кликов не ловит.
            Assert.True(Dot(active).IsEffectivelyVisible);
            Assert.True(CloseButton(active).IsEffectivelyVisible);
            Assert.False(Dot(active).IsHitTestVisible);
            Assert.Same(Resource(window, "MmAccentBrush"), Dot(active).Fill);
            Assert.Equal(new Rect(active.Bounds.Width - 12, 6, 6, 6), DotBox(active));
            Assert.Equal(cleanName, NameBox(active));

            // И у вкладки с иконкой: та же точка, то же место имени.
            ((DocumentTabViewModel)inactive.DataContext!).IsDirty = true;
            ((IPseudoClasses)inactive.Classes).Remove(":pointerover");
            window.UpdateLayout();

            Assert.True(FileIcon(inactive).IsEffectivelyVisible);
            Assert.True(Dot(inactive).IsEffectivelyVisible);
            Assert.Equal(new Rect(inactive.Bounds.Width - 12, 6, 6, 6), DotBox(inactive));
            Assert.Equal(new Rect(30, cleanName.Y, inactive.Bounds.Width - 38, cleanName.Height), NameBox(inactive));

            window.Close();
        });
    }

    /// <summary>Нажатие по точке проходит во вкладку и открывает её.</summary>
    [Fact]
    public Task PressOnTheDotActivatesTheTab()
    {
        return _fixture.RunAsync(async () =>
        {
            var viewModel = CreateViewModel();
            await viewModel.OpenPathAsync(First);
            await viewModel.OpenPathAsync(Second);
            var window = Show(viewModel, ThemeVariant.Light);

            var inactive = Tab(window, "first.md");
            ((DocumentTabViewModel)inactive.DataContext!).IsDirty = true;
            window.UpdateLayout();

            var centre = inactive.TranslatePoint(DotBox(inactive).Center, window)!.Value;
            window.MouseDown(centre, MouseButton.Left);
            window.MouseUp(centre, MouseButton.Left);
            window.UpdateLayout();

            Assert.Equal(First, viewModel.OpenDocuments.ActiveTab!.Path);

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

    /// <summary>
    /// Длинное имя не обрывается многоточием, а затухает у правого края на 24 px;
    /// короткое — без маски. Затухание одно на всех состояниях вкладки.
    /// </summary>
    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public Task LongNameFadesOutInsteadOfAnEllipsis(string theme)
    {
        return _fixture.RunAsync(async () =>
        {
            var viewModel = CreateViewModel();
            await viewModel.OpenPathAsync(LongName);
            await viewModel.OpenPathAsync(First);
            var window = Show(viewModel, theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light);

            var longTab = Tab(window, "installation-guide-for-windows-and-macos.md");
            var shortTab = Tab(window, "first.md");

            Assert.False(Name(longTab).TextLayout.TextLines.Single().HasCollapsed);
            Assert.Equal(TrailingFadeDecorator.FadeWidth, Fade(longTab).Fade);
            AssertMaskMatchesFade(Fade(longTab));

            // Обрезка и маска идут по границам декоратора, а они шире строки сверху,
            // снизу и слева: срезается только правый край, выступающие глифы целы.
            var fade = Fade(longTab);
            var line = new Rect(Name(longTab).TranslatePoint(default, fade)!.Value, Name(longTab).Bounds.Size);
            Assert.True(line.X > 0 && line.Y > 0 && line.Bottom < fade.Bounds.Height, $"line {line} in {fade.Bounds}");
            Assert.True(line.Right > fade.Bounds.Width);

            Assert.Equal(0, Fade(shortTab).Fade);
            Assert.Null(Fade(shortTab).OpacityMask);

            // Неактивная, под курсором и активная — одна и та же маска.
            var mask = Fade(longTab).OpacityMask;
            ((IPseudoClasses)longTab.Classes).Add(":pointerover");
            window.UpdateLayout();
            Assert.Same(mask, Fade(longTab).OpacityMask);

            await viewModel.OpenDocuments.ActivateCommand.ExecuteAsync((DocumentTabViewModel)longTab.DataContext!);
            window.UpdateLayout();
            Assert.Contains("active", longTab.Classes);
            Assert.Equal(TrailingFadeDecorator.FadeWidth, Fade(longTab).Fade);
            AssertMaskMatchesFade(Fade(longTab));

            window.Close();
        });
    }

    /// <summary>
    /// Затухание следует за шириной вкладки и текстом: при сжатии до 120 маска
    /// пересчитывается под новую ширину, суффикс или новое имя, которые не
    /// помещаются, её добавляют, а снятый суффикс — убирает. Ширина маски — не больше скрытого остатка.
    /// </summary>
    [Fact]
    public Task FadeFollowsTheTabWidthAndTheName()
    {
        return _fixture.RunAsync(async () =>
        {
            var viewModel = CreateViewModel();
            await viewModel.OpenPathAsync(LongName);
            await viewModel.OpenPathAsync(First);
            await viewModel.OpenPathAsync(Second);
            var window = Show(viewModel, ThemeVariant.Light);

            var longTab = Tab(window, "installation-guide-for-windows-and-macos.md");
            Assert.Equal(OpenDocumentsViewModel.PreferredTabWidth, longTab.Bounds.Width);
            AssertMaskMatchesFade(Fade(longTab));

            // Полоса сжалась: три вкладки по 120, «+» и промежутки — ровно 408. Второй
            // проход: ширину вкладок пересчитывает размер полосы после первого.
            window.GetVisualDescendants().OfType<TabStripView>().Single().Width = 408;
            window.UpdateLayout();
            window.UpdateLayout();
            longTab = Tab(window, "installation-guide-for-windows-and-macos.md");

            Assert.Equal(OpenDocumentsViewModel.MinimumTabWidth, longTab.Bounds.Width);
            Assert.Equal(OpenDocumentsViewModel.MinimumTabWidth - 38, NameBox(longTab).Width);
            AssertMaskMatchesFade(Fade(longTab));

            var first = Tab(window, "first.md");
            var tab = (DocumentTabViewModel)first.DataContext!;
            Assert.Null(Fade(first).OpacityMask);

            tab.StateSuffix = "(deleted from disk long ago)";
            window.UpdateLayout();
            Assert.True(Fade(first).Fade > 0);
            AssertMaskMatchesFade(Fade(first));

            tab.StateSuffix = null;
            window.UpdateLayout();
            Assert.Equal(0, Fade(first).Fade);
            Assert.Null(Fade(first).OpacityMask);

            tab.Title = "first-chapter-renamed-at-length.md";
            window.UpdateLayout();
            AssertMaskMatchesFade(Fade(first));

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

    private static TrailingFadeDecorator Fade(Border tab)
        => tab.GetVisualDescendants().OfType<TrailingFadeDecorator>().Single();

    /// <summary>
    /// Место имени во вкладке — та ширина, в которой оно видно: декоратор без поля,
    /// которое он отдаёт наружу под выступающие глифы.
    /// </summary>
    private static Rect NameBox(Border tab)
    {
        var fade = Fade(tab);
        return new Rect(fade.TranslatePoint(default, tab)!.Value, fade.Bounds.Size).Deflate(fade.Padding);
    }

    /// <summary>
    /// Ширина затухания — не больше скрытого остатка имени, и маска гаснет ровно на
    /// этой ширине у правого края.
    /// </summary>
    private static void AssertMaskMatchesFade(TrailingFadeDecorator fade)
    {
        var visible = fade.Bounds.Width - fade.Padding.Left - fade.Padding.Right;
        var hidden = fade.Child!.Bounds.Width - visible;
        Assert.True(hidden > 0, $"name fits: {fade.Child.Bounds.Width} in {visible}");
        Assert.Equal(Math.Min(hidden, TrailingFadeDecorator.FadeWidth), fade.Fade, 3);

        var mask = Assert.IsType<LinearGradientBrush>(fade.OpacityMask);
        Assert.Equal(1 - fade.Fade / fade.Bounds.Width, mask.GradientStops[1].Offset, 3);
        Assert.Equal(Colors.Transparent, mask.GradientStops[^1].Color);
    }

    private static Panel FileIcon(Border tab)
        => tab.GetVisualDescendants().OfType<Panel>().Single(panel => panel.Classes.Contains("mm-tab-file-icon"));

    private static Button CloseButton(Border tab)
        => tab.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("mm-tab-close"));

    private static Ellipse Dot(Border tab)
        => tab.GetVisualDescendants().OfType<Ellipse>().Single(dot => dot.Classes.Contains("mm-tab-dot"));

    private static Rect DotBox(Border tab) => Box(Dot(tab), tab);

    /// <summary>Ячейка, которую делят иконка файла и крестик.</summary>
    private static Rect IconCell(Border tab) => Box((Control)CloseButton(tab).GetVisualParent()!, tab);

    private static Rect Box(Control control, Border tab)
        => new(control.TranslatePoint(default, tab)!.Value, control.Bounds.Size);

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
