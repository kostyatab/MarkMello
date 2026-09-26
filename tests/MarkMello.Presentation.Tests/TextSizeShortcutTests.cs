using Avalonia.Input;
using Avalonia.Headless;
using MarkMello.Application.UseCases;
using MarkMello.Domain;
using MarkMello.Presentation.Localization;
using MarkMello.Presentation.ViewModels;
using MarkMello.Presentation.Views;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// ⌘+ / ⌘= / ⌘− / ⌘0 и те же сочетания с Ctrl — жесты настоящего <see cref="MainWindow"/>.
/// «+» на основной клавиатуре — это «=» с Shift, а клавиши цифрового блока приходят
/// своими кодами: каждое из этих нажатий должно попасть в команду.
/// </summary>
[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class TextSizeShortcutTests
{
    private const RawInputModifiers Ctrl = RawInputModifiers.Control;
    private const RawInputModifiers Cmd = RawInputModifiers.Meta;
    private const RawInputModifiers Shift = RawInputModifiers.Shift;

    private readonly AvaloniaHeadlessFixture _fixture;

    public TextSizeShortcutTests(AvaloniaHeadlessFixture fixture) => _fixture = fixture;

    [Theory]
    // Больше: «=», «+» (Shift+=) и «+» цифрового блока.
    [InlineData(Key.OemPlus, Ctrl, PhysicalKey.Equal, "=", 15)]
    [InlineData(Key.OemPlus, Ctrl | Shift, PhysicalKey.Equal, "+", 15)]
    [InlineData(Key.Add, Ctrl, PhysicalKey.NumPadAdd, "+", 15)]
    [InlineData(Key.OemPlus, Cmd, PhysicalKey.Equal, "=", 15)]
    [InlineData(Key.OemPlus, Cmd | Shift, PhysicalKey.Equal, "+", 15)]
    // Меньше: «−» и «−» цифрового блока.
    [InlineData(Key.OemMinus, Ctrl, PhysicalKey.Minus, "-", 13)]
    [InlineData(Key.Subtract, Ctrl, PhysicalKey.NumPadSubtract, "-", 13)]
    [InlineData(Key.OemMinus, Cmd, PhysicalKey.Minus, "-", 13)]
    // Без модификатора — обычная клавиша, размер не меняется.
    [InlineData(Key.OemPlus, RawInputModifiers.None, PhysicalKey.Equal, "=", 14)]
    public Task ShortcutStepsTextSizeByOnePixel(Key key, RawInputModifiers modifiers, PhysicalKey physicalKey, string symbol, int expected)
    {
        return _fixture.RunAsync(async () =>
        {
            var viewModel = await CreateViewerAsync();
            var window = Show(viewModel);

            window.KeyPress(key, modifiers, physicalKey, symbol);

            Assert.Equal(expected, viewModel.ReadingPreferences.FontSize);
            window.Hide();
        });
    }

    [Theory]
    [InlineData(Key.D0, Ctrl, PhysicalKey.Digit0)]
    [InlineData(Key.D0, Cmd, PhysicalKey.Digit0)]
    [InlineData(Key.NumPad0, Ctrl, PhysicalKey.NumPad0)]
    [InlineData(Key.NumPad0, Cmd, PhysicalKey.NumPad0)]
    public Task ShortcutResetsTextSizeToTheDefault(Key key, RawInputModifiers modifiers, PhysicalKey physicalKey)
    {
        return _fixture.RunAsync(async () =>
        {
            var viewModel = await CreateViewerAsync();
            viewModel.FontSizeSetting = 22;
            var window = Show(viewModel);

            window.KeyPress(key, modifiers, physicalKey, "0");

            Assert.Equal(ReadingPreferences.Default.FontSize, viewModel.ReadingPreferences.FontSize);
            window.Hide();
        });
    }

    [Fact]
    public Task ShortcutDoesNothingAtTheEdgeOfTheRange()
    {
        return _fixture.RunAsync(async () =>
        {
            var viewModel = await CreateViewerAsync();
            viewModel.FontSizeSetting = ReadingPreferences.MaxFontSize;
            var window = Show(viewModel);

            window.KeyPress(Key.OemPlus, Ctrl, PhysicalKey.Equal, "=");
            Assert.Equal(ReadingPreferences.MaxFontSize, viewModel.ReadingPreferences.FontSize);

            viewModel.FontSizeSetting = ReadingPreferences.MinFontSize;
            window.KeyPress(Key.OemMinus, Ctrl, PhysicalKey.Minus, "-");
            Assert.Equal(ReadingPreferences.MinFontSize, viewModel.ReadingPreferences.FontSize);

            window.Hide();
        });
    }

    /// <summary>
    /// Окно без composition root: нужны только его жесты и привязки к VM. Закрывать
    /// такое окно нельзя — отписка в OnClosed ждёт VM из полного конструктора, — поэтому
    /// тесты его прячут.
    /// </summary>
    private static MainWindow Show(ShellViewModel viewModel)
    {
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        return window;
    }

    private static async Task<ShellViewModel> CreateViewerAsync()
    {
        var path = TestPaths.At("docs", "README.md");
        var loader = new StubDocumentLoader();
        loader.Sources[path] = new MarkdownSource(path, "README.md", "# readme");
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
            TestUpdates.CreateViewModel(),
            new OpenFolderUseCase(fileSystem),
            new ExpandFolderNodeUseCase(fileSystem),
            new SearchWorkspaceFilesUseCase(fileSystem),
            new WorkspaceFileOperationsUseCase(fileSystem, new FakePlatformServices()),
            new FakePlatformServices(),
            static () => new FakeWorkspaceWatcher(),
            new RecordingWindowLauncher());

        await viewModel.OpenPathAsync(path);
        Assert.True(viewModel.IsViewer);
        Assert.Equal(ReadingPreferences.Default.FontSize, viewModel.ReadingPreferences.FontSize);
        return viewModel;
    }
}
