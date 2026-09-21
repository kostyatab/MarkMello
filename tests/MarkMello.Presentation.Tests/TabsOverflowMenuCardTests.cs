using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MarkMello.Application.Abstractions;
using MarkMello.Application.UseCases;
using MarkMello.Domain;
using MarkMello.Presentation.Localization;
using MarkMello.Presentation.ViewModels;
using MarkMello.Presentation.Views;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Список скрытых вкладок «ещё N» — карточка внутри окна, как меню сайдбара и ⋯
/// (ADR-0009 Rule 4), а не Flyout: одна поверхность с общей тенью, под кнопкой по её
/// правому краю, открытие и закрытие через <see cref="ShellOverlayKind"/>.
/// </summary>
[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class TabsOverflowMenuCardTests
{
    private const string Panel = "TabsOverflowMenuPanel";

    // Шестнадцать вкладок в окне 900: часть уходит в «ещё N».
    private static readonly string[] Documents = [.. Enumerable.Range(1, 16).Select(index => TestPaths.At("docs", $"chapter-{index}.md"))];

    private readonly AvaloniaHeadlessFixture _fixture;

    public TabsOverflowMenuCardTests(AvaloniaHeadlessFixture fixture) => _fixture = fixture;

    /// <summary>Кнопка открывает и закрывает меню; другое меню его сменяет, и наоборот.</summary>
    [Fact]
    public Task ToggleOpensClosesAndReplacesOtherOverlays()
    {
        return _fixture.RunAsync(async () =>
        {
            var viewModel = await CreateViewModelWithOverflowAsync();

            viewModel.ToggleTabsOverflowMenuCommand.Execute(null);
            Assert.True(viewModel.IsTabsOverflowMenuOpen);
            Assert.True(viewModel.HasOpenOverlay);
            Assert.Same(viewModel, viewModel.TabsOverflowMenuOverlayContent);

            viewModel.ToggleTabsOverflowMenuCommand.Execute(null);
            Assert.False(viewModel.IsTabsOverflowMenuOpen);
            Assert.Null(viewModel.TabsOverflowMenuOverlayContent);

            viewModel.ToggleTabsOverflowMenuCommand.Execute(null);
            viewModel.ToggleFolderMenuCommand.Execute(null);
            Assert.False(viewModel.IsTabsOverflowMenuOpen);
            Assert.True(viewModel.IsFolderMenuOpen);

            viewModel.ToggleTabsOverflowMenuCommand.Execute(null);
            Assert.False(viewModel.IsFolderMenuOpen);
            Assert.True(viewModel.IsTabsOverflowMenuOpen);

            viewModel.ToggleAppMenuCommand.Execute(null);
            Assert.False(viewModel.IsTabsOverflowMenuOpen);
            Assert.True(viewModel.IsAppMenuOpen);

            viewModel.ToggleTabsOverflowMenuCommand.Execute(null);
            Assert.False(viewModel.IsAppMenuOpen);

            viewModel.CloseOverlayCommand.Execute(null);
            Assert.False(viewModel.IsTabsOverflowMenuOpen);
        });
    }

    /// <summary>Скрытых вкладок не осталось — меню закрывается само: его кнопка пропала.</summary>
    [Fact]
    public Task MenuClosesWhenTheOverflowIsGone()
    {
        return _fixture.RunAsync(async () =>
        {
            var viewModel = await CreateViewModelWithOverflowAsync();
            viewModel.ToggleTabsOverflowMenuCommand.Execute(null);

            viewModel.OpenDocuments.AvailableWidth = 10_000;

            Assert.False(viewModel.OpenDocuments.HasOverflow);
            Assert.False(viewModel.IsTabsOverflowMenuOpen);
        });
    }

    /// <summary>
    /// ✕ у скрытой вкладки с правками открывает вопрос «Сохранить?» — меню под ним
    /// закрывается: одновременно открыт не больше одного оверлея (ADR-0009 Rule 4).
    /// </summary>
    [Fact]
    public Task DirtyPromptClosesTheMenu()
    {
        return _fixture.RunAsync(async () =>
        {
            var viewModel = CreateViewModel();
            foreach (var path in Documents)
            {
                await viewModel.OpenPathAsync(path);
            }

            // Черновик с правками — последняя вкладка; активной становится первая,
            // и черновик уходит в «ещё N». Первая тоже в правке: переход на черновик
            // режим не меняет, и закрыть меню за вопросом больше некому.
            await viewModel.CreateNewDocumentCommand.ExecuteAsync(null);
            var draft = viewModel.OpenDocuments.ActiveTab!;
            viewModel.EditorSession!.SourceText = "# draft";
            await viewModel.OpenPathAsync(Documents[0]);
            await viewModel.ToggleEditModeCommand.ExecuteAsync(null);
            Assert.True(viewModel.IsEditMode);

            viewModel.OpenDocuments.AvailableWidth = 700;
            Assert.Contains(draft, viewModel.OpenDocuments.OverflowTabs);

            viewModel.ToggleTabsOverflowMenuCommand.Execute(null);
            await viewModel.OpenDocuments.CloseCommand.ExecuteAsync(draft);

            Assert.True(viewModel.IsDirtyPromptOpen);
            Assert.False(viewModel.IsTabsOverflowMenuOpen);
        });
    }

    /// <summary>
    /// Одна карточка на общей поверхности меню: фон, рамка и тень из палитры, никакой
    /// обёртки Fluent вокруг — в обеих темах.
    /// </summary>
    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public Task MenuIsOneCardOnTheSharedSurface(string theme)
    {
        return _fixture.RunAsync(async () =>
        {
            var (window, viewModel) = await ShowAsync(theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light);

            Click(window, OverflowButton(window));

            Assert.True(viewModel.IsTabsOverflowMenuOpen);
            var card = Card(window);
            Assert.Equal(Resource(window, "MmCardShadow"), card.BoxShadow);
            Assert.Same(Resource(window, "MmElevatedBackgroundBrush"), card.Background);
            Assert.Same(Resource(window, "MmBorderBrush"), card.BorderBrush);
            Assert.Equal(new CornerRadius(12), card.CornerRadius);
            Assert.False(card.ClipToBounds);
            Assert.Equal(1, card.Opacity);
            Assert.Empty(window.GetVisualDescendants().OfType<FlyoutPresenter>());
            Assert.Single(
                window.GetControl<ContentControl>(Panel).GetVisualDescendants().OfType<Border>(),
                static border => border.BoxShadow.Count > 0);
            Assert.Equal(viewModel.OpenDocuments.OverflowTabs.Count, Rows(window).Count);

            // Пункты — те же, что в меню сайдбара и ⋯: 26 px, отступ 10, радиус 7, шрифт 13.
            Assert.All(
                [.. Rows(window), CloseOthers(window)],
                static item =>
                {
                    Assert.Contains("mm-menu-command", item.Classes);
                    Assert.Equal(26, item.Bounds.Height);
                    Assert.Equal(new Thickness(10, 0), item.Padding);
                    Assert.Equal(new CornerRadius(7), item.CornerRadius);
                    Assert.Equal(13, item.FontSize);
                });

            window.Hide();
        });
    }

    /// <summary>
    /// Карточка встаёт под кнопкой «ещё N» в зазоре 6 и правым краем по её правому краю,
    /// как прежний <c>BottomEdgeAlignedRight</c>, — и с рамкой окна тоже.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task CardLinesUpWithTheRightEdgeOfItsButton(bool windowBorder)
    {
        return _fixture.RunAsync(async () =>
        {
            var (window, viewModel) = await ShowAsync();
            if (windowBorder)
            {
                viewModel.WindowBorderMode = WindowBorderMode.On;
                Render(window);
                Assert.Equal(1, window.GetControl<Panel>("SidebarMenuHost").TranslatePoint(default, window)!.Value.X, 0);
            }

            Click(window, OverflowButton(window));
            AssertCardUnderButton(window, "opened");

            // Кнопка сдвигается при открытом меню — окно шире, другое число вкладок
            // помещается, — и карточка идёт за ней, а не остаётся на старом месте.
            window.Width = 1100;
            Render(window);
            Render(window);

            Assert.True(viewModel.IsTabsOverflowMenuOpen);
            AssertCardUnderButton(window, "wider window");

            window.Hide();
        });
    }

    /// <summary>
    /// У левого края окна карточка сдвигается внутрь, а не обрезается; пока она не
    /// измерена, стоит правым краем по кнопке — края окна поправит раскладка.
    /// </summary>
    [Fact]
    public void CardStaysInsideTheWindow()
    {
        var limits = new Rect(8, 8, 884, 584);
        var card = new Size(236, 200);

        // Под кнопкой: правый край карточки — правый край кнопки, зазор 6 снизу.
        Assert.Equal(
            new Thickness(0, 43, 400, 0),
            MainWindow.CalculateTrailingMenuMargin(new Rect(420, 7, 80, 30), card, limits, 900));

        // Кнопка у левого края: карточка не уходит за край окна.
        Assert.Equal(
            new Thickness(0, 43, 900 - 244, 0),
            MainWindow.CalculateTrailingMenuMargin(new Rect(40, 7, 80, 30), card, limits, 900));

        // Не измерена — только по кнопке.
        Assert.Equal(
            new Thickness(0, 43, 780, 0),
            MainWindow.CalculateTrailingMenuMargin(new Rect(40, 7, 80, 30), default, limits, 900));
    }

    /// <summary>
    /// Клик по скрытой вкладке делает её активной и закрывает меню; «Закрыть все, кроме
    /// активной» — тоже закрывает.
    /// </summary>
    [Fact]
    public Task ItemsRunTheirCommandAndCloseTheMenu()
    {
        return _fixture.RunAsync(async () =>
        {
            var (window, viewModel) = await ShowAsync();

            Click(window, OverflowButton(window));
            var hidden = viewModel.OpenDocuments.OverflowTabs[0];
            Click(window, Rows(window)[0]);

            Assert.Same(hidden, viewModel.OpenDocuments.ActiveTab);
            Assert.False(viewModel.IsTabsOverflowMenuOpen);
            Assert.Null(window.GetControl<ContentControl>(Panel).Content);

            Click(window, OverflowButton(window));
            Click(window, CloseOthers(window));

            Assert.False(viewModel.IsTabsOverflowMenuOpen);
            Assert.Single(viewModel.OpenDocuments.Tabs);

            window.Hide();
        });
    }

    /// <summary>
    /// ✕ закрывает скрытую вкладку, а меню остаётся — можно закрыть несколько подряд;
    /// под курсором он подменяет точку несохранённого. Когда переполнение кончилось,
    /// меню закрывается само.
    /// </summary>
    [Fact]
    public Task CloseButtonKeepsTheMenuOpenUntilTheOverflowIsGone()
    {
        return _fixture.RunAsync(async () =>
        {
            var (window, viewModel) = await ShowAsync();

            // Фокус до меню — на «+»: мышь фокус кнопке «ещё N» не отдаёт, поэтому после
            // ✕ последней скрытой вкладки он возвращается сюда, а не на пропавшую кнопку.
            var plus = Trigger(window, "NewDocumentButton");
            Assert.True(plus.Focus());
            Click(window, OverflowButton(window));
            Assert.False(OverflowButton(window).IsFocused);

            var row = Rows(window)[0];
            var hidden = Assert.IsType<DocumentTabViewModel>(row.DataContext);
            var close = CloseButton(row);
            Assert.Equal(0, close.Opacity);

            window.MouseMove(Center(window, row));
            Render(window);
            Assert.Equal(1, close.Opacity);

            Assert.False(close.Focusable);
            Click(window, close);

            Assert.DoesNotContain(hidden, viewModel.OpenDocuments.Tabs);
            Assert.True(viewModel.IsTabsOverflowMenuOpen);

            // Фокус остался в карточке, хотя его строку ✕ унёс: стрелки живы.
            Assert.Contains(Rows(window), static row => row.IsFocused);

            // Подпись кнопки укорачивается («ещё 10» → «ещё 9»), карточка держится за её край.
            while (viewModel.OpenDocuments.HasOverflow)
            {
                AssertCardUnderButton(window, $"{viewModel.OpenDocuments.OverflowTabs.Count} hidden");
                Click(window, CloseButton(Rows(window)[0]));
            }

            Assert.False(viewModel.IsTabsOverflowMenuOpen);
            Assert.Null(window.GetControl<ContentControl>(Panel).Content);
            Assert.True(plus.IsFocused);

            window.Hide();
        });
    }

    /// <summary>
    /// Точка несохранённого и ✕ делят одно место: без курсора видна точка, под курсором
    /// строки — только ✕. У сохранённой вкладки точки нет вовсе. В обеих темах.
    /// </summary>
    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public Task CloseButtonReplacesTheDirtyDotUnderThePointer(string theme)
    {
        return _fixture.RunAsync(async () =>
        {
            var (window, viewModel) = await ShowAsync(theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light);

            // Черновик с правками уходит в «ещё N»: активной становится первая вкладка.
            await viewModel.CreateNewDocumentCommand.ExecuteAsync(null);
            var draft = viewModel.OpenDocuments.ActiveTab!;
            viewModel.EditorSession!.SourceText = "# draft";
            await viewModel.OpenPathAsync(Documents[0]);
            Render(window);
            Assert.True(draft.IsDirty);
            Assert.Contains(draft, viewModel.OpenDocuments.OverflowTabs);

            Click(window, OverflowButton(window));
            var row = Rows(window).Single(candidate => candidate.DataContext == draft);
            var dot = DirtyDot(row);
            var close = CloseButton(row);
            var accent = Resource(window, "MmAccentBrush");

            Assert.True(dot.IsEffectivelyVisible);
            Assert.Equal(1, dot.Opacity);
            Assert.Equal(0, close.Opacity);
            Assert.Same(accent, dot.Fill);

            window.MouseMove(Center(window, row));
            Render(window);

            Assert.Equal(0, dot.Opacity);
            Assert.Equal(1, close.Opacity);
            Assert.Same(accent, dot.Fill);

            window.MouseMove(new Point(window.Bounds.Width - 40, window.Bounds.Height - 40));
            Render(window);

            Assert.Equal(1, dot.Opacity);
            Assert.Equal(0, close.Opacity);

            var saved = Rows(window).First(row => row.DataContext != draft);
            Assert.False(DirtyDot(saved).IsVisible);
            Assert.Equal(0, CloseButton(saved).Opacity);

            window.Hide();
        });
    }

    /// <summary>Повторный клик по кнопке, Esc и клик мимо закрывают карточку.</summary>
    [Fact]
    public Task CardClosesOnSecondClickEscapeAndClickOutside()
    {
        return _fixture.RunAsync(async () =>
        {
            var (window, viewModel) = await ShowAsync();

            Click(window, OverflowButton(window));
            Assert.True(viewModel.IsTabsOverflowMenuOpen);
            Assert.Contains("open", OverflowButton(window).Classes);
            Click(window, OverflowButton(window));
            Assert.False(viewModel.IsTabsOverflowMenuOpen);

            Click(window, OverflowButton(window));
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Render(window);
            Assert.False(viewModel.IsTabsOverflowMenuOpen);

            Click(window, OverflowButton(window));
            Click(window, new Point(window.Bounds.Width - 40, window.Bounds.Height - 40));
            Assert.False(viewModel.IsTabsOverflowMenuOpen);

            // ⋯ сменяет «ещё N», и «ещё N» сменяет ⋯.
            Click(window, OverflowButton(window));
            Click(window, Trigger(window, "AppMenuTriggerButton"));
            Assert.False(viewModel.IsTabsOverflowMenuOpen);
            Assert.True(viewModel.IsAppMenuOpen);

            Click(window, OverflowButton(window));
            Assert.True(viewModel.IsTabsOverflowMenuOpen);
            Assert.False(viewModel.IsAppMenuOpen);

            window.Hide();
        });
    }

    /// <summary>
    /// Клавиатура как у остальных карточек: фокус в первом пункте, стрелки ходят по
    /// пунктам мимо ✕, после Esc фокус возвращается туда, где был.
    /// </summary>
    [Fact]
    public Task KeyboardWalksTheItemsAndGivesTheFocusBack()
    {
        return _fixture.RunAsync(async () =>
        {
            var (window, viewModel) = await ShowAsync();
            var button = OverflowButton(window);
            Assert.True(button.Focus());

            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Render(window);

            Assert.True(viewModel.IsTabsOverflowMenuOpen);
            var rows = Rows(window);
            Assert.True(rows[0].IsFocused);

            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Render(window);
            Assert.True(rows[1].IsFocused);

            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            Render(window);
            Assert.True(CloseOthers(window).IsFocused);

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Render(window);

            Assert.False(viewModel.IsTabsOverflowMenuOpen);
            Assert.True(button.IsFocused);

            window.Hide();
        });
    }

    /// <summary>Правый верхний угол карточки — в зазоре 6 под правым нижним углом кнопки.</summary>
    private static void AssertCardUnderButton(Window window, string? what = null)
    {
        var button = OverflowButton(window);
        var buttonCorner = button.TranslatePoint(new Point(button.Bounds.Width, button.Bounds.Height), window)!.Value;
        var card = window.GetControl<ContentControl>(Panel);
        Assert.True(card.Bounds.Width > 0);
        var cardCorner = card.TranslatePoint(new Point(card.Bounds.Width, 0), window)!.Value;
        Assert.True(
            Math.Abs(buttonCorner.X - cardCorner.X) < 0.5 && Math.Abs(buttonCorner.Y + 6 - cardCorner.Y) < 0.5,
            $"{what}: button bottom-right {buttonCorner}, card top-right {cardCorner}");
    }

    private static Button OverflowButton(Window window) => Trigger(window, "TabsOverflowButton");

    private static Button Trigger(Window window, string name)
        => window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == name);

    private static List<Button> Rows(Window window)
        => [.. window.GetControl<ContentControl>(Panel)
            .GetVisualDescendants()
            .OfType<Button>()
            .Where(static button => button.DataContext is DocumentTabViewModel && button.Classes.Contains("mm-tab-overflow-item"))];

    private static Button CloseOthers(Window window)
        => window.GetControl<ContentControl>(Panel)
            .GetVisualDescendants()
            .OfType<Button>()
            .Single(static button => button.Name == "TabsOverflowCloseOthers");

    private static Button CloseButton(Button row)
        => row.GetVisualDescendants().OfType<Button>().Single(static button => button.Classes.Contains("mm-tab-overflow-close"));

    private static Ellipse DirtyDot(Button row)
        => row.GetVisualDescendants().OfType<Ellipse>().Single(static dot => dot.Classes.Contains("mm-tab-dirty"));

    private static Border Card(Window window)
        => window.GetControl<ContentControl>(Panel)
            .GetVisualDescendants()
            .OfType<Border>()
            .Single(static border => border.Classes.Contains("mm-sidebar-menu-panel"));

    private static Point Center(Window window, Control control)
        => control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

    private static void Click(Window window, Control control) => Click(window, Center(window, control));

    private static void Click(Window window, Point point)
    {
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Render(window);
    }

    private static void Render(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static object? Resource(Window window, string key)
    {
        Assert.True(window.TryFindResource(key, window.ActualThemeVariant, out var value), key);
        return value;
    }

    private static async Task<ShellViewModel> CreateViewModelWithOverflowAsync()
    {
        var viewModel = CreateViewModel();
        foreach (var path in Documents)
        {
            await viewModel.OpenPathAsync(path);
        }

        viewModel.OpenDocuments.AvailableWidth = 700;
        Assert.True(viewModel.OpenDocuments.HasOverflow);
        return viewModel;
    }

    private static async Task<(MainWindow Window, ShellViewModel ViewModel)> ShowAsync(ThemeVariant? theme = null)
    {
        var viewModel = CreateViewModel();
        foreach (var path in Documents)
        {
            await viewModel.OpenPathAsync(path);
        }

        var window = new MainWindow(
            viewModel,
            StartupSmokeTestOptions.Disabled,
            new InMemorySettingsStore(),
            new RecordingStartupMetrics())
        {
            RequestedThemeVariant = theme ?? ThemeVariant.Light,
            Width = 900,
            Height = 600
        };
        window.Styles.Add(new FluentTheme());
        window.Styles.Add(Assert.IsAssignableFrom<IStyle>(LoadTheme("Controls.axaml")));
        window.Resources.MergedDictionaries.Add(Assert.IsAssignableFrom<IResourceProvider>(LoadTheme("Colors.axaml")));
        window.Resources.MergedDictionaries.Add(Assert.IsAssignableFrom<IResourceProvider>(LoadTheme("Typography.axaml")));
        window.Resources.MergedDictionaries.Add(Assert.IsAssignableFrom<IResourceProvider>(LoadTheme("Icons.axaml")));
        window.Show();
        Render(window);
        Assert.True(viewModel.OpenDocuments.HasOverflow);
        return (window, viewModel);
    }

    private static object LoadTheme(string themeFile)
        => AvaloniaXamlLoader.Load(new Uri("avares://MarkMello.Presentation/Themes/" + themeFile));

    private static ShellViewModel CreateViewModel()
    {
        var loader = new StubDocumentLoader();
        foreach (var path in Documents)
        {
            loader.Sources[path] = new MarkdownSource(path, System.IO.Path.GetFileName(path), "# doc");
        }

        var fileSystem = new FakeWorkspaceFileSystem();
        var platform = new FakePlatformServices(fileSystem);

        return new ShellViewModel(
            new OpenDocumentUseCase(loader),
            new SaveDocumentUseCase(new RecordingDocumentSaver()),
            new StubFilePicker(),
            new StubCommandLineActivation(),
            new LocalizationService(AppLanguage.English),
            new InMemorySettingsStore(),
            new RecordingThemeService(),
            new RecordingStartupMetrics(),
            new RenderMarkdownDocumentUseCase(new TestMarkdownRenderer(), new FakeDiagramRenderService()),
            new StubUpdateService(),
            new OpenFolderUseCase(fileSystem),
            new ExpandFolderNodeUseCase(fileSystem),
            new SearchWorkspaceFilesUseCase(fileSystem),
            new WorkspaceFileOperationsUseCase(fileSystem, platform),
            platform,
            static () => new FakeWorkspaceWatcher(),
            new RecordingWindowLauncher());
    }
}
