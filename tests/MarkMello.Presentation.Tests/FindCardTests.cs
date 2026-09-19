using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MarkMello.Application.UseCases;
using MarkMello.Domain;
using MarkMello.Presentation.Localization;
using MarkMello.Presentation.ViewModels;
using MarkMello.Presentation.Views;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Карточка поиска по A-CollapsedFind: раскрывается под кнопкой поиска у правого края,
/// создаётся только по ⌘F и при каждом открытии отдаёт фокус полю.
/// </summary>
[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class FindCardTests
{
    private static readonly string DocumentPath = TestPaths.At("docs", "README.md");

    private readonly AvaloniaHeadlessFixture _fixture;

    public FindCardTests(AvaloniaHeadlessFixture fixture) => _fixture = fixture;

    /// <summary>Быстрый путь: до первого ⌘F карточки нет в дереве окна.</summary>
    [Fact]
    public Task CardIsNotCreatedBeforeTheFirstFind()
    {
        return _fixture.RunAsync(async () =>
        {
            var window = Show(await CreateViewerAsync());

            Assert.Empty(window.GetVisualDescendants().OfType<FindBarView>());

            PressFind(window);

            Assert.Single(window.GetVisualDescendants().OfType<FindBarView>());
            window.Hide();
        });
    }

    [Fact]
    public Task CardOpensUnderThePressedFindButtonAtTheRightEdge()
    {
        return _fixture.RunAsync(async () =>
        {
            var window = Show(await CreateViewerAsync());
            var findButton = window.GetControl<ToggleButton>("FindTriggerButton");
            Assert.False(findButton.IsChecked);

            PressFind(window);

            var card = Card(window);
            var cardOrigin = card.TranslatePoint(default, window)!.Value;
            var buttonOrigin = findButton.TranslatePoint(default, window)!.Value;

            Assert.True(findButton.IsChecked);
            Assert.Equal(new Size(392, 42), card.Bounds.Size);
            Assert.Equal(MainWindow.CalculateWindowRowHeight(OperatingSystem.IsMacOS()) + 6, cardOrigin.Y);
            Assert.InRange(buttonOrigin.X, cardOrigin.X, cardOrigin.X + card.Bounds.Width - findButton.Bounds.Width);

            // Правый отступ, как у карточек Aa и меню ⋯, держит общий хост.
            Assert.Same(window.GetControl<Panel>("OverlayCardHost"), window.GetControl<ContentControl>("FindBarHost").Parent);
            window.Hide();
        });
    }

    /// <summary>⌘F → Esc → ⌘F: поле снова в фокусе, а не только при первом открытии.</summary>
    [Fact]
    public Task FieldIsFocusedOnEveryOpen()
    {
        return _fixture.RunAsync(async () =>
        {
            var viewModel = await CreateViewerAsync();
            var window = Show(viewModel);

            PressFind(window);
            Assert.Same(Input(window), window.FocusManager!.GetFocusedElement());

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Dispatcher.UIThread.RunJobs();

            Assert.False(viewModel.IsFindBarOpen);
            Assert.False(window.GetControl<ToggleButton>("FindTriggerButton").IsChecked);
            Assert.Empty(window.GetVisualDescendants().OfType<FindBarView>());

            PressFind(window);
            Assert.Same(Input(window), window.FocusManager!.GetFocusedElement());
            window.Hide();
        });
    }

    [Fact]
    public Task CloseButtonClosesTheCard()
    {
        return _fixture.RunAsync(async () =>
        {
            var viewModel = await CreateViewerAsync();
            var window = Show(viewModel);
            PressFind(window);

            var close = window.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => ReferenceEquals(button.Command, viewModel.CloseFindBarCommand));
            ((IInvokeProvider)new ButtonAutomationPeer(close)).Invoke();

            Assert.False(viewModel.IsFindBarOpen);
            Assert.Empty(window.GetVisualDescendants().OfType<FindBarView>());
            window.Hide();
        });
    }

    /// <summary>
    /// ↵ и ⇧↵ в поле уходят окну всплывающими событиями: карточка создаётся заново при
    /// каждом открытии, и окно ловит переходы у себя, не держа ссылку на экземпляр.
    /// </summary>
    [Fact]
    public Task EnterAndShiftEnterReachTheWindow()
    {
        return _fixture.RunAsync(async () =>
        {
            var viewModel = await CreateViewerAsync();
            var window = Show(viewModel);
            var next = 0;
            var previous = 0;
            window.AddHandler(FindBarView.FindNextRequestedEvent, (_, _) => next++);
            window.AddHandler(FindBarView.FindPreviousRequestedEvent, (_, _) => previous++);

            PressFind(window);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            window.KeyPress(Key.Enter, RawInputModifiers.Shift, PhysicalKey.Enter, null);

            Assert.Equal(1, next);
            Assert.Equal(1, previous);
            Assert.True(viewModel.IsFindBarOpen);
            window.Hide();
        });
    }

    [Fact]
    public Task CardOpensInEditModeToo()
    {
        return _fixture.RunAsync(async () =>
        {
            var viewModel = await CreateViewerAsync();
            await viewModel.ToggleEditModeCommand.ExecuteAsync(null);
            var window = Show(viewModel);

            PressFind(window);

            Assert.True(viewModel.IsFindBarOpen);
            Assert.True(window.GetControl<ToggleButton>("FindTriggerButton").IsChecked);
            Assert.Same(Input(window), window.FocusManager!.GetFocusedElement());
            window.Hide();
        });
    }

    /// <summary>
    /// Карточка на фоне карточек (MmElevatedBackgroundBrush) с рамкой MmBorderBrush;
    /// кнопки ↑ ↓ ✕ 28×28 без рамки, под курсором — заливка кнопок строки. В обеих темах.
    /// </summary>
    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public Task CardUsesThePaletteOfTheTheme(string theme)
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = new FindBarView();
            var window = ThemedTestWindow.Create(theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light, view);
            window.Show();
            window.UpdateLayout();

            var card = view.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("mm-find-card"));
            Assert.Same(Resource(window, "MmElevatedBackgroundBrush"), card.Background);
            Assert.Same(Resource(window, "MmBorderBrush"), card.BorderBrush);
            Assert.Equal(new CornerRadius(10), card.CornerRadius);

            var buttons = view.GetVisualDescendants().OfType<Button>().ToArray();
            Assert.Equal(3, buttons.Length);
            Assert.All(buttons, button => Assert.Equal(new Size(28, 28), button.Bounds.Size));

            ((IPseudoClasses)buttons[0].Classes).Add(":pointerover");
            var presenter = buttons[0].GetVisualDescendants().OfType<ContentPresenter>().First();
            Assert.Same(Resource(window, "MmTabHoverBrush"), presenter.Background);
            Assert.Same(Resource(window, "MmTextBrush"), presenter.Foreground);

            window.Close();
        }, CancellationToken.None);
    }

    /// <summary>
    /// Поле занимает всю высоту карточки: клик у её верхнего края, выше строки текста,
    /// тоже ставит курсор в поле, а не проваливается в пустоту.
    /// </summary>
    [Fact]
    public Task ClickAboveTheTextFocusesTheField()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var view = new FindBarView();
            var window = ThemedTestWindow.Create(ThemeVariant.Light, view);
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var card = view.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("mm-find-card"));
            var input = view.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "FindInput");
            // Фокус уводим на ↑: он есть и без VM, в отличие от ✕ с командой из привязки.
            var previousButton = view.GetVisualDescendants().OfType<Button>().First(button => button.Classes.Contains("mm-find-button"));
            Assert.True(previousButton.Focus());

            var inputCentre = input.TranslatePoint(new Point(input.Bounds.Width / 2, 0), card)!.Value;
            var nearTopEdge = card.TranslatePoint(new Point(inputCentre.X, 3), window)!.Value;
            window.MouseDown(nearTopEdge, MouseButton.Left);
            window.MouseUp(nearTopEdge, MouseButton.Left);

            Assert.Same(input, window.FocusManager!.GetFocusedElement());

            window.Close();
        }, CancellationToken.None);
    }

    private static void PressFind(Window window)
    {
        window.KeyPress(Key.F, RawInputModifiers.Control, PhysicalKey.F, "f");
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static Border Card(Window window)
        => window.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("mm-find-card"));

    private static TextBox Input(Window window)
        => window.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "FindInput");

    private static object? Resource(Window window, string key)
    {
        Assert.True(window.TryFindResource(key, window.ActualThemeVariant, out var value));
        return value;
    }

    /// <summary>
    /// Окно без composition root, как в <see cref="WindowRowTests"/>: закрыть его нельзя —
    /// отписка в OnClosed ждёт VM из полного конструктора, — поэтому тесты его прячут.
    /// </summary>
    private static MainWindow Show(ShellViewModel viewModel)
    {
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static async Task<ShellViewModel> CreateViewerAsync()
    {
        var loader = new StubDocumentLoader();
        loader.Sources[DocumentPath] = new MarkdownSource(DocumentPath, "README.md", "# readme\n\nalpha beta alpha");
        var fileSystem = new FakeWorkspaceFileSystem();

        var viewModel = new ShellViewModel(
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
            new WorkspaceFileOperationsUseCase(fileSystem, new FakePlatformServices()),
            new FakePlatformServices(),
            static () => new FakeWorkspaceWatcher(),
            new RecordingWindowLauncher());

        await viewModel.OpenPathAsync(DocumentPath);
        Assert.True(viewModel.IsViewer);
        return viewModel;
    }
}
