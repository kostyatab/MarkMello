using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Windows.Input;
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

    /// <summary>
    /// Шапка сайдбара — продолжение строки окна (A-Chrome): на macOS слева светофор,
    /// кнопка панели справа; на Windows и Linux кнопка слева.
    /// </summary>
    [Theory]
    [InlineData(true, 11, 11, HorizontalAlignment.Right)]
    [InlineData(false, 11, 11, HorizontalAlignment.Left)]
    public void HeaderLeavesRoomForTheTrafficLightsOnMacOS(bool isMacOS, double leading, double trailing, HorizontalAlignment alignment)
    {
        var (padding, toggleAlignment) = WorkspaceSidebarView.CalculateHeaderLayout(isMacOS);

        Assert.Equal(new Thickness(leading, 0, trailing, 0), padding);
        Assert.Equal(alignment, toggleAlignment);
    }

    [Fact]
    public Task HeaderDragsTheWindowAndHidesThePanel()
    {
        return _fixture.RunAsync(async () =>
        {
            var viewModel = CreateViewModel();
            await viewModel.OpenFolderPathAsync(TestPaths.At("docs"));

            var sidebar = new WorkspaceSidebarView { DataContext = viewModel };
            var header = sidebar.GetControl<Border>("SidebarHeader");
            var toggle = sidebar.GetControl<Button>("SidebarToggleButton");

            Assert.Contains(MainWindow.WindowDragClass, header.Classes);
            Assert.Equal(MainWindow.CalculateWindowRowHeight(OperatingSystem.IsMacOS()), header.Height);
            Assert.Equal("Hide file panel (Ctrl+B)", ToolTip.GetTip(toggle));

            toggle.Command!.Execute(null);

            Assert.False(viewModel.ShowsSidebar);
            Assert.NotNull(viewModel.Workspace);
        });
    }

    /// <summary>
    /// «имя папки ▾» — действия над самой папкой, «+» — создание в ней (ADR-0009 Rule 4).
    /// Кнопки меню приложения в шапке больше нет: меню ⋯ живёт в строке окна.
    /// </summary>
    [Fact]
    public Task FolderRowMenusCarryTheFolderActions()
    {
        return _fixture.RunAsync(async () =>
        {
            var platform = new FakePlatformServices();
            var viewModel = CreateViewModel(platform);
            await viewModel.OpenFolderPathAsync(TestPaths.At("docs"));

            var sidebar = new WorkspaceSidebarView();
            var window = ThemedTestWindow.Create(ThemeVariant.Light, sidebar);
            window.DataContext = viewModel;
            window.Show();
            window.UpdateLayout();

            Assert.DoesNotContain(
                sidebar.GetVisualDescendants().OfType<ToggleButton>(),
                button => button.Command == viewModel.ToggleAppMenuCommand);

            var folderMenu = OpenMenu(sidebar.GetControl<Button>("FolderMenuButton"));
            Assert.Equal(
                ["Open Another Folder…", "Show in Explorer", "Close Folder"],
                folderMenu.Select(item => item.Header));

            folderMenu[1].Command!.Execute(null);
            await Task.Yield();
            Assert.Equal([TestPaths.At("docs")], platform.RevealedPaths);

            var createMenu = OpenMenu(sidebar.GetControl<Button>("CreateMenuButton"));
            Assert.Equal(["New File", "New Folder"], createMenu.Select(item => item.Header));

            createMenu[0].Command!.Execute(null);
            Assert.True(viewModel.Workspace!.IsEditingName);

            window.Close();
        });
    }

    /// <summary>
    /// Строки дерева 28 px с иконками; текущий файл — заливка MmTabActiveBrush и иконка
    /// акцентом вместо полоски 2 px, не-документ — приглушённая иконка (ADR-0009 Rule 1).
    /// </summary>
    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public Task CurrentFileRowIsFilledAndRowsCarryIcons(string theme)
    {
        return _fixture.RunAsync(async () =>
        {
            var viewModel = CreateViewModel();
            await viewModel.OpenFolderPathAsync(TestPaths.At("docs"));

            var window = ThemedTestWindow.Create(theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light);
            window.DataContext = viewModel;
            window.Content = new WorkspaceSidebarView();
            window.Show();
            window.UpdateLayout();

            var readme = Row(window, "README.md");
            var notes = Row(window, "notes.md");

            Assert.True(WorkspaceSidebarView.GetIsActiveRow(readme));
            Assert.False(WorkspaceSidebarView.GetIsActiveRow(notes));
            Assert.Equal(28, readme.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "PART_LayoutRoot").Bounds.Height);
            Assert.Same(Resource(window, "MmTabActiveBrush"), LayoutRoot(readme).Background);
            Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(LayoutRoot(notes).Background).Color);

            Assert.Same(Resource(window, "LucideFileTextGeometry"), RowIcon(readme).Data);
            Assert.Same(Resource(window, "MmAccentBrush"), RowIcon(readme).Foreground);
            Assert.Same(Resource(window, "MmTextSoftBrush"), RowIcon(notes).Foreground);
            Assert.Same(Resource(window, "LucideFileGeometry"), RowIcon(Row(window, "pack.bat")).Data);
            Assert.Same(Resource(window, "MmTreeInertTextBrush"), RowIcon(Row(window, "pack.bat")).Foreground);

            var folder = viewModel.Workspace!.Roots.Single(node => node.Name == "adr");
            Assert.Same(Resource(window, "LucideFolderGeometry"), RowIcon(Row(window, "adr")).Data);
            await viewModel.Workspace.ExpandNodeAsync(folder);
            Assert.Same(Resource(window, "LucideFolderOpenGeometry"), RowIcon(Row(window, "adr")).Data);

            // Открыли другой файл — заливка переезжает за ним.
            await viewModel.OpenPathAsync(TestPaths.At("docs", "notes.md"));
            Assert.False(WorkspaceSidebarView.GetIsActiveRow(readme));
            Assert.Same(Resource(window, "MmTabActiveBrush"), LayoutRoot(notes).Background);

            window.Close();
        });
    }

    private static TreeViewItem Row(Window window, string name)
        => window.GetVisualDescendants()
            .OfType<TreeViewItem>()
            .Single(item => item.DataContext is FileTreeNodeViewModel node && node.Name == name);

    private static Border LayoutRoot(TreeViewItem item)
        => item.GetVisualDescendants().OfType<Border>().First(border => border.Name == "PART_LayoutRoot");

    private static LucideIcon RowIcon(TreeViewItem item)
        => item.GetVisualDescendants().OfType<LucideIcon>().Single(icon => icon.Classes.Contains("mm-tree-icon"));

    private static object? Resource(Window window, string key)
    {
        Assert.True(window.TryFindResource(key, window.ActualThemeVariant, out var value));
        return value;
    }

    /// <summary>
    /// Пункты меню привязаны к shell только пока меню открыто: закрытое меню
    /// отцепляется от дерева, и подписи с командами пропадают.
    /// </summary>
    private static List<(string? Header, ICommand? Command)> OpenMenu(Button button)
    {
        var flyout = Assert.IsType<MenuFlyout>(button.Flyout);
        flyout.ShowAt(button);
        Dispatcher.UIThread.RunJobs();
        var items = flyout.Items
            .OfType<MenuItem>()
            .Select(item => (item.Header as string, item.Command))
            .ToList();
        flyout.Hide();
        return items;
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

    private static ShellViewModel CreateViewModel(FakePlatformServices? platform = null)
    {
        platform ??= new FakePlatformServices();

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
            new WorkspaceFileOperationsUseCase(fileSystem, platform),
            platform,
            static () => new FakeWorkspaceWatcher(),
            new RecordingWindowLauncher());
    }
}
