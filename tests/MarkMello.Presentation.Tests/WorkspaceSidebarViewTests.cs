using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
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
/// Проверяет, что разметка сайдбара действительно собирается и биндится:
/// unit-тесты view-model этого не ловят — сломанный XAML падает только в рантайме.
/// </summary>
[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class WorkspaceSidebarViewTests
{
    private readonly AvaloniaHeadlessFixture _fixture;

    public WorkspaceSidebarViewTests(AvaloniaHeadlessFixture fixture) => _fixture = fixture;

    [Fact]
    public Task SidebarRendersRootLevelOfTheTree()
    {
        return _fixture.RunAsync(async () =>
        {
            var viewModel = CreateViewModel();
            await viewModel.OpenFolderPathAsync(TestPaths.At("docs"));

            var window = new Window
            {
                DataContext = viewModel,
                Width = 400,
                Height = 600,
                Content = new WorkspaceSidebarView()
            };

            window.Show();

            var tree = window.GetVisualDescendants().OfType<TreeView>().Single();
            var nodes = Assert.IsAssignableFrom<IEnumerable<FileTreeNodeViewModel>>(tree.ItemsSource);

            Assert.Equal(["adr", "README.md", "notes.md", "pack.bat"], nodes.Select(node => node.Name));

            var rootLabel = window.GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(block => block.Classes.Contains("mm-sidebar-root"));

            Assert.Equal("docs", rootLabel.Text);

            window.Close();
        });
    }

    /// <summary>
    /// Левый клик открывает документ, правый только показывает меню: до фикса открытие
    /// висело на смене выделения, и файл открывался даже правым кликом.
    /// </summary>
    [Theory]
    [InlineData(MouseButton.Left, "notes.md")]
    [InlineData(MouseButton.Right, "README.md")]
    public Task OnlyTheLeftClickOpensTheDocument(MouseButton button, string expectedFileName)
    {
        return _fixture.RunAsync(async () =>
        {
            var viewModel = CreateViewModel();
            await viewModel.OpenFolderPathAsync(TestPaths.At("docs"));

            var sidebar = new WorkspaceSidebarView();
            var window = new Window
            {
                DataContext = viewModel,
                Width = 400,
                Height = 600,
                Content = sidebar
            };

            window.Show();
            window.UpdateLayout();

            // Папка сама открывает README.md, поэтому кликаем по другому документу:
            // при правом клике активным должен остаться README.md.
            var node = viewModel.Workspace!.Roots.Single(row => row.Name == "notes.md");
            sidebar.ActivateFromPointer(button, node);
            await Task.Yield();

            Assert.Equal(TestPaths.At("docs", expectedFileName), viewModel.CurrentDocumentPath);

            window.Close();
        });
    }

    /// <summary>
    /// Раскрытие строки должно доходить до view-model: без этой связки шеврон раскрывает
    /// только контейнер, каталог не читается и под папкой висит пустая строка.
    /// </summary>
    [Fact]
    public Task TreeItemExpansionIsBoundToTheNode()
    {
        return _fixture.RunAsync(() =>
        {
            var binds = new WorkspaceSidebarView().Styles
                .OfType<Style>()
                .SelectMany(style => style.Setters)
                .OfType<Setter>()
                // Значение сеттера — привязка, а не константа: константа означала бы,
                // что связь с моделью снова потеряна.
                .Any(setter => setter.Property == TreeViewItem.IsExpandedProperty
                    && setter.Value is not null and not bool);

            Assert.True(binds);
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Шеврон дерева — Lucide в собственном шаблоне штатного ToggleButton: папка
    /// по-прежнему раскрывается кликом по шеврону и стрелками, а шеврон при
    /// раскрытии поворачивается вниз.
    /// </summary>
    [Fact]
    public Task FolderChevronExpandsTheFolderByMouseAndKeyboardAndTurnsDown()
    {
        return _fixture.RunAsync(async () =>
        {
            var viewModel = CreateViewModel();
            await viewModel.OpenFolderPathAsync(TestPaths.At("docs"));

            var window = ThemedTestWindow.Create(ThemeVariant.Light);
            window.DataContext = viewModel;
            window.Content = new WorkspaceSidebarView();
            window.Show();
            window.UpdateLayout();

            var folder = viewModel.Workspace!.Roots.Single(row => row.Name == "adr");
            var item = window.GetVisualDescendants().OfType<TreeViewItem>().Single(row => row.DataContext == folder);
            var chevron = item.GetVisualDescendants().OfType<ToggleButton>().Single(button => button.Name == "PART_ExpandCollapseChevron");
            var icon = chevron.GetVisualDescendants().OfType<LucideIcon>().Single();

            Assert.True(chevron.IsEffectivelyVisible);
            Assert.NotNull(icon.Data);
            Assert.False(folder.IsExpanded);
            Assert.Equal(Matrix.Identity, icon.RenderTransform?.Value ?? Matrix.Identity);

            var chevronCentre = chevron.TranslatePoint(new Point(chevron.Bounds.Width / 2, chevron.Bounds.Height / 2), window)!.Value;
            window.MouseDown(chevronCentre, MouseButton.Left);
            window.MouseUp(chevronCentre, MouseButton.Left);
            await Task.Yield();

            Assert.True(folder.IsExpanded);
            AssertTurnedDown(icon);

            Assert.True(item.Focus());
            window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
            Assert.False(folder.IsExpanded);
            Assert.Equal(Matrix.Identity, icon.RenderTransform?.Value ?? Matrix.Identity);

            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            Assert.True(folder.IsExpanded);
            AssertTurnedDown(icon);

            window.Close();
        });
    }

    private static void AssertTurnedDown(LucideIcon icon)
    {
        var rotation = Assert.IsAssignableFrom<ITransform>(icon.RenderTransform).Value;
        var quarterTurn = Matrix.CreateRotation(Math.PI / 2);

        Assert.Equal(quarterTurn.M11, rotation.M11, 6);
        Assert.Equal(quarterTurn.M12, rotation.M12, 6);
        Assert.Equal(quarterTurn.M21, rotation.M21, 6);
        Assert.Equal(quarterTurn.M22, rotation.M22, 6);
    }

    private static ShellViewModel CreateViewModel()
    {
        var fileSystem = new FakeWorkspaceFileSystem();
        fileSystem.AddDirectory(
            TestPaths.At("docs"),
            WorkspaceEntry.ForDirectory(TestPaths.At("docs", "adr"), "adr"),
            WorkspaceEntry.ForFile(TestPaths.At("docs", "README.md"), "README.md"),
            WorkspaceEntry.ForFile(TestPaths.At("docs", "notes.md"), "notes.md"),
            WorkspaceEntry.ForFile(TestPaths.At("docs", "pack.bat"), "pack.bat"));
        fileSystem.AddDirectory(
            TestPaths.At("docs", "adr"),
            WorkspaceEntry.ForFile(TestPaths.At("docs", "adr", "0001-record.md"), "0001-record.md"));

        var loader = new StubDocumentLoader();
        loader.Sources[TestPaths.At("docs", "README.md")] =
            new MarkdownSource(TestPaths.At("docs", "README.md"), "README.md", "# readme");
        loader.Sources[TestPaths.At("docs", "notes.md")] =
            new MarkdownSource(TestPaths.At("docs", "notes.md"), "notes.md", "# notes");

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
            new WorkspaceFileOperationsUseCase(fileSystem, new FakePlatformServices()),
            new FakePlatformServices(),
            static () => new FakeWorkspaceWatcher(),
            new RecordingWindowLauncher());
    }
}
