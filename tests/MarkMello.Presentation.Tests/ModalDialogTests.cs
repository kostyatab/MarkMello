using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MarkMello.Application.Abstractions;
using MarkMello.Application.UseCases;
using MarkMello.Domain;
using MarkMello.Domain.Workspace;
using MarkMello.Presentation.Localization;
using MarkMello.Presentation.ViewModels;
using MarkMello.Presentation.Views;

namespace MarkMello.Presentation.Tests;

/// <summary>
/// Общая рамка модальных диалогов (ADR-0009 Rule 10, A-Dirty, A-Delete): один скрим на всё
/// окно, верхние 44 px тянут окно, клики под скримом не проходят, фокус живёт внутри
/// вопроса и возвращается после ответа.
/// </summary>
[Collection(AvaloniaHeadlessTestGroup.Name)]
public sealed class ModalDialogTests
{
    private static readonly string Root = TestPaths.At("docs");
    private static readonly string Readme = TestPaths.At("docs", "README.md");
    private static readonly string Todo = TestPaths.At("docs", "todo.md");

    private readonly AvaloniaHeadlessFixture _fixture;

    public ModalDialogTests(AvaloniaHeadlessFixture fixture) => _fixture = fixture;

    /// <summary>Быстрый путь: карточки нет в окне, пока ни о чём не спросили.</summary>
    [Fact]
    public Task DialogExistsOnlyWhileItAsks()
    {
        return _fixture.RunAsync(async () =>
        {
            var harness = await CreateHarnessAsync();
            var window = Show(harness.ViewModel);

            Assert.Empty(window.GetVisualDescendants().OfType<ModalDialogFrame>());

            await AskAboutDirtyReadmeAsync(harness, window);
            Assert.Single(window.GetVisualDescendants().OfType<ModalDialogFrame>());

            PressKey(window, Key.Escape, PhysicalKey.Escape);

            Assert.False(harness.ViewModel.IsDirtyPromptOpen);
            Assert.Empty(window.GetVisualDescendants().OfType<ModalDialogFrame>());
            window.Hide();
        });
    }

    /// <summary>
    /// Скрим накрывает всё окно — сайдбар, вкладки и строку, — а не одно тело документа.
    /// Верх скрима, место строки окна, тянет окно; ниже — уже нет.
    /// </summary>
    [Fact]
    public Task ScrimCoversTheWholeWindowAndItsTopDragsTheWindow()
    {
        return _fixture.RunAsync(async () =>
        {
            var harness = await CreateHarnessAsync();
            var window = Show(harness.ViewModel);
            await AskAboutDirtyReadmeAsync(harness, window);

            var frame = Frame(window);
            var layout = window.GetControl<Grid>("SidebarLayout");
            Assert.Equal(layout.Bounds.Size, frame.Bounds.Size);
            Assert.Equal(new Point(0, 0), frame.TranslatePoint(default, layout));
            Assert.True(harness.ViewModel.ShowsSidebar);

            // Куда пришло нажатие, видно по источнику события: попадание считает сама
            // обработка ввода, по отрисованному кадру.
            var sources = new List<Visual>();
            window.AddHandler(
                InputElement.PointerPressedEvent,
                (_, e) => sources.Add(Assert.IsAssignableFrom<Visual>(e.Source)),
                RoutingStrategies.Tunnel,
                handledEventsToo: true);

            var sidebar = new Point(40, 300);
            var tabRow = new Point(window.Bounds.Width / 2, 22);
            var document = new Point(window.Bounds.Width - 40, window.Bounds.Height - 40);
            foreach (var point in new[] { sidebar, tabRow, document })
            {
                Click(window, point);
            }

            Assert.Equal(3, sources.Count);
            Assert.All(sources, source => Assert.True(IsWithin(source, frame), source.GetType().Name));
            Assert.True(harness.ViewModel.IsDirtyPromptOpen);

            // Верх скрима тянет окно, как строка под ним; ниже — уже нет.
            Assert.True(MainWindow.IsWindowDragSource(sources[1]));
            sources.Clear();
            Click(window, new Point(tabRow.X, 60));
            Assert.False(MainWindow.IsWindowDragSource(Assert.Single(sources)));
            window.Hide();
        });
    }

    /// <summary>
    /// Клик по вкладке под скримом до неё не доходит: активная не меняется. Без диалога
    /// тот же клик вкладку переключает — значит, держит его именно скрим.
    /// </summary>
    [Fact]
    public Task ClicksUnderTheScrimDoNotReachTheTabs()
    {
        return _fixture.RunAsync(async () =>
        {
            var harness = await CreateHarnessAsync();
            var window = Show(harness.ViewModel);
            await harness.ViewModel.OpenPathAsync(Todo);
            Render(window);
            var readmeTab = window.GetVisualDescendants()
                .OfType<Border>()
                .Single(border => border.Classes.Contains("mm-tab") && border.DataContext is DocumentTabViewModel { Title: "README.md" });
            var centre = readmeTab.TranslatePoint(new Point(readmeTab.Bounds.Width / 2, readmeTab.Bounds.Height / 2), window)!.Value;

            var workspace = harness.ViewModel.Workspace!;
            await workspace.RequestDeleteCommand.ExecuteAsync(workspace.Roots.Single(row => row.Path == Todo));
            Render(window);

            Click(window, centre);

            Assert.True(harness.ViewModel.IsDeletePromptOpen);
            Assert.Equal(Todo, harness.ViewModel.OpenDocuments.ActiveTab!.Path);

            PressKey(window, Key.Escape, PhysicalKey.Escape);
            Click(window, centre);

            Assert.Equal(Readme, harness.ViewModel.OpenDocuments.ActiveTab!.Path);
            window.Hide();
        });
    }

    /// <summary>
    /// Фокус сразу на «Сохранить»: Enter сохраняет, Esc отменяет, ⌘⌫ и Ctrl+Backspace —
    /// «Не сохранять».
    /// </summary>
    [Theory]
    [InlineData("enter")]
    [InlineData("escape")]
    [InlineData("ctrl-backspace")]
    [InlineData("cmd-backspace")]
    public Task DirtyPromptAnswersToItsKeys(string key)
    {
        return _fixture.RunAsync(async () =>
        {
            var harness = await CreateHarnessAsync();
            var window = Show(harness.ViewModel);
            var readme = harness.ViewModel.OpenDocuments.ActiveTab!;
            await AskAboutDirtyReadmeAsync(harness, window);

            Assert.Same(DialogButton(window, harness.ViewModel.ConfirmDirtySaveCommand), window.FocusManager!.GetFocusedElement());

            switch (key)
            {
                case "enter":
                    PressKey(window, Key.Enter, PhysicalKey.Enter);
                    break;
                case "escape":
                    PressKey(window, Key.Escape, PhysicalKey.Escape);
                    break;
                case "ctrl-backspace":
                    PressKey(window, Key.Back, PhysicalKey.Backspace, RawInputModifiers.Control);
                    break;
                case "cmd-backspace":
                    PressKey(window, Key.Back, PhysicalKey.Backspace, RawInputModifiers.Meta);
                    break;
            }

            Assert.False(harness.ViewModel.IsDirtyPromptOpen);
            if (key == "escape")
            {
                Assert.Same(readme, harness.ViewModel.OpenDocuments.ActiveTab);
                Assert.True(readme.IsDirty);
                Assert.Empty(harness.Saver.Saves);
            }
            else
            {
                string[] expectedSaves = key == "enter" ? ["# readme edited"] : [];
                Assert.DoesNotContain(readme, harness.ViewModel.OpenDocuments.Tabs);
                Assert.Equal(expectedSaves, harness.Saver.Saves.Select(static save => save.Content));
            }

            window.Hide();
        });
    }

    /// <summary>
    /// В удалении фокус на «Отмене»: случайный Enter ничего не удаляет. Esc закрывает
    /// диалог. Удаляемая строка дерева подсвечена, пока диалог открыт.
    /// </summary>
    [Fact]
    public Task DeletePromptFocusesCancelAndHighlightsTheRow()
    {
        return _fixture.RunAsync(async () =>
        {
            var harness = await CreateHarnessAsync();
            var window = Show(harness.ViewModel);
            var workspace = harness.ViewModel.Workspace!;
            var todo = workspace.Roots.Single(row => row.Path == Todo);
            var labelBefore = RowLabelOrigin(window, todo);

            await workspace.RequestDeleteCommand.ExecuteAsync(todo);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.Same(DialogButton(window, harness.ViewModel.CancelDeleteCommand), window.FocusManager!.GetFocusedElement());
            var row = RowLayoutRoot(window, todo);
            Assert.Same(Resource(window, "MmTabHoverBrush"), row.Background);
            Assert.Same(Resource(window, "MmBorderBrush"), row.BorderBrush);

            // Рамка подсветки — только цвет: толщина у всех строк одна, и имя не сдвигается.
            Assert.Equal(RowLayoutRoot(window, workspace.Roots.Single(other => other.Path == Readme)).BorderThickness, row.BorderThickness);
            Assert.Equal(labelBefore, RowLabelOrigin(window, todo));

            PressKey(window, Key.Enter, PhysicalKey.Enter);

            Assert.False(harness.ViewModel.IsDeletePromptOpen);
            Assert.Empty(harness.Platform.TrashedPaths);
            Assert.NotSame(Resource(window, "MmBorderBrush"), RowLayoutRoot(window, todo).BorderBrush);

            await workspace.RequestDeleteCommand.ExecuteAsync(todo);
            Dispatcher.UIThread.RunJobs();
            PressKey(window, Key.Escape, PhysicalKey.Escape);

            Assert.False(harness.ViewModel.IsDeletePromptOpen);
            Assert.Empty(harness.Platform.TrashedPaths);
            window.Hide();
        });
    }

    /// <summary>
    /// Без корзины диалог переспрашивает, и фокус возвращается на «Отмену»: Enter после
    /// клика по «Удалить» не должен удалять безвозвратно.
    /// </summary>
    [Fact]
    public Task PermanentDeleteQuestionReturnsFocusToCancel()
    {
        return _fixture.RunAsync(async () =>
        {
            var harness = await CreateHarnessAsync();
            harness.Platform.TrashResult = TrashResult.Unsupported;
            var window = Show(harness.ViewModel);
            var workspace = harness.ViewModel.Workspace!;

            await workspace.RequestDeleteCommand.ExecuteAsync(workspace.Roots.Single(row => row.Path == Todo));
            Dispatcher.UIThread.RunJobs();
            var confirm = DialogButton(window, harness.ViewModel.ConfirmDeleteCommand);
            Assert.Equal("Delete", confirm.Content);
            confirm.Focus();

            await harness.ViewModel.ConfirmDeleteCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Delete permanently", confirm.Content);
            Assert.Same(DialogButton(window, harness.ViewModel.CancelDeleteCommand), window.FocusManager!.GetFocusedElement());
            window.Hide();
        });
    }

    /// <summary>Ответ на вопрос возвращает фокус туда, где он был, — в редактор.</summary>
    [Fact]
    public Task FocusReturnsToTheEditorAfterTheDialogCloses()
    {
        return _fixture.RunAsync(async () =>
        {
            var harness = await CreateHarnessAsync();
            var window = Show(harness.ViewModel);
            var editor = window.GetVisualDescendants()
                .OfType<TextBox>()
                .Single(box => box.Classes.Contains("mm-editor-textarea"));
            Assert.True(editor.Focus());

            PressKey(window, Key.W, PhysicalKey.W, RawInputModifiers.Control);
            Assert.True(harness.ViewModel.IsDirtyPromptOpen);
            Assert.NotSame(editor, window.FocusManager!.GetFocusedElement());

            PressKey(window, Key.Escape, PhysicalKey.Escape);

            Assert.False(harness.ViewModel.IsDirtyPromptOpen);
            Assert.Same(editor, window.FocusManager!.GetFocusedElement());
            window.Hide();
        });
    }

    /// <summary>
    /// Карточка 440, радиус 12, фон карточек; кнопки 32 px, радиус 8. Обычная — заливка
    /// вкладки, основная — цветом текста по фону, опасная — акцентом. В обеих темах.
    /// </summary>
    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public Task CardAndButtonsUseThePaletteOfTheTheme(string theme)
    {
        return _fixture.Session.Dispatch(() =>
        {
            var secondary = new Button { Classes = { "mm-dialog-secondary" }, Content = "Cancel" };
            var primary = new Button { Classes = { "mm-dialog-primary" }, Content = "Save" };
            var destructive = new Button { Classes = { "mm-dialog-destructive" }, Content = "Delete" };
            var frame = new ModalDialogFrame
            {
                Content = new StackPanel { Children = { secondary, primary, destructive } }
            };
            var window = ThemedTestWindow.Create(theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light, frame);
            window.Show();
            window.UpdateLayout();

            Assert.Same(Resource(window, "MmScrimBrush"), frame.Background);
            var card = frame.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("mm-dialog-card"));
            Assert.Equal(440, card.Bounds.Width);
            Assert.Equal(new CornerRadius(12), card.CornerRadius);
            Assert.Same(Resource(window, "MmElevatedBackgroundBrush"), card.Background);
            Assert.Equal(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetTabNavigation(card));

            var dragArea = frame.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains(MainWindow.WindowDragClass));
            Assert.Equal(44, dragArea.Bounds.Height);
            Assert.Equal(frame.Bounds.Width, dragArea.Bounds.Width);

            Assert.All(new[] { secondary, primary, destructive }, button =>
            {
                Assert.Equal(32, button.Bounds.Height);
                Assert.Equal(new CornerRadius(8), button.CornerRadius);
            });

            AssertColours(window, secondary, "MmTabBrush", "MmTextBrush");
            AssertColours(window, primary, "MmTextBrush", "MmBackgroundBrush");
            AssertColours(window, destructive, "MmAccentBrush", "MmElevatedBackgroundBrush");

            ((IPseudoClasses)secondary.Classes).Add(":pointerover");
            Assert.Same(Resource(window, "MmTabHoverBrush"), Presenter(secondary).Background);

            // Кольцо фокуса — снаружи кнопки, через 2 px от неё. Слой adorner'ов по умолчанию
            // режет его по границам кнопки, и от кольца оставались точки в углах.
            var ring = Assert.IsType<Border>(primary.FocusAdorner!.Build());
            Assert.Equal(new Thickness(-4), ring.Margin);
            Assert.Equal(new Thickness(2), ring.BorderThickness);
            Assert.False(AdornerLayer.GetIsClipEnabled(ring));

            window.Close();
        }, CancellationToken.None);
    }

    /// <summary>
    /// Рамка отдаёт фокус помеченной кнопке при появлении и снова — когда диалог задаёт
    /// новый вопрос.
    /// </summary>
    [Fact]
    public Task FrameFocusesTheInitialButtonOnShowAndOnANewQuestion()
    {
        return _fixture.Session.Dispatch(() =>
        {
            var safe = new Button { Content = "Cancel" };
            ModalDialogFrame.SetIsInitialFocus(safe, true);
            var risky = new Button { Content = "Delete" };
            var frame = new ModalDialogFrame
            {
                FocusResetKey = "first question",
                Content = new StackPanel { Children = { risky, safe } }
            };
            var window = ThemedTestWindow.Create(ThemeVariant.Light, frame);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Same(safe, window.FocusManager!.GetFocusedElement());

            Assert.True(risky.Focus());
            frame.FocusResetKey = "second question";

            Assert.Same(safe, window.FocusManager!.GetFocusedElement());
            window.Close();
        }, CancellationToken.None);
    }

    private static async Task AskAboutDirtyReadmeAsync(ModalHarness harness, Window window)
    {
        await harness.ViewModel.CloseActiveTabCommand.ExecuteAsync(null);
        Assert.True(harness.ViewModel.IsDirtyPromptOpen);
        Render(window);
    }

    private static void PressKey(Window window, Key key, PhysicalKey physicalKey, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, physicalKey, null);
        Render(window);
    }

    private static void Click(Window window, Point point)
    {
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Render(window);
    }

    /// <summary>
    /// Попадание мыши считается по отрисованной сцене: без кадра новый скрим для мыши
    /// ещё не существует, и клик «проходит» сквозь него или не попадает никуда.
    /// </summary>
    private static void Render(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static ModalDialogFrame Frame(Window window)
        => window.GetVisualDescendants().OfType<ModalDialogFrame>().Single();

    private static Button DialogButton(Window window, System.Windows.Input.ICommand command)
        => Frame(window).GetVisualDescendants().OfType<Button>().Single(button => ReferenceEquals(button.Command, command));

    private static Border RowLayoutRoot(Window window, FileTreeNodeViewModel node)
        => window.GetVisualDescendants()
            .OfType<TreeViewItem>()
            .Single(item => ReferenceEquals(item.DataContext, node))
            .GetVisualDescendants()
            .OfType<Border>()
            .First(border => border.Name == "PART_LayoutRoot");

    private static bool IsWithin(Visual visual, Visual ancestor)
        => ReferenceEquals(visual, ancestor) || visual.GetVisualAncestors().Contains(ancestor);

    private static Point RowLabelOrigin(Window window, FileTreeNodeViewModel node)
    {
        var row = RowLayoutRoot(window, node);
        var label = row.GetVisualDescendants().OfType<TextBlock>().First(block => block.Text == node.Name);
        return label.TranslatePoint(default, window)!.Value;
    }

    private static ContentPresenter Presenter(Button button)
        => button.GetVisualDescendants().OfType<ContentPresenter>().First();

    private static void AssertColours(Window window, Button button, string background, string foreground)
    {
        Assert.Same(Resource(window, background), button.Background);
        Assert.Same(Resource(window, foreground), button.Foreground);
    }

    private static object? Resource(Window window, string key)
    {
        Assert.True(window.TryFindResource(key, window.ActualThemeVariant, out var value), key);
        return value;
    }

    /// <summary>
    /// Окно без composition root, как в <see cref="WindowRowTests"/>: закрыть его нельзя —
    /// отписка в OnClosed ждёт VM из полного конструктора, — поэтому тесты его прячут.
    /// Тестовая сессия идёт без темы, а карточке нужны шаблоны Fluent и стили диалогов:
    /// тема подключается до того, как окно получит view model, — сайдбар и диалоги
    /// создаются уже после неё.
    /// </summary>
    private static MainWindow Show(ShellViewModel viewModel)
    {
        var window = new MainWindow { RequestedThemeVariant = ThemeVariant.Light };
        window.Styles.Add(new FluentTheme());
        window.Styles.Add(Assert.IsAssignableFrom<IStyle>(LoadTheme("Controls.axaml")));
        window.Resources.MergedDictionaries.Add(Assert.IsAssignableFrom<IResourceProvider>(LoadTheme("Colors.axaml")));
        window.Resources.MergedDictionaries.Add(Assert.IsAssignableFrom<IResourceProvider>(LoadTheme("Icons.axaml")));
        window.DataContext = viewModel;
        window.Show();
        Render(window);
        return window;
    }

    private static object LoadTheme(string themeFile)
        => AvaloniaXamlLoader.Load(new Uri("avares://MarkMello.Presentation/Themes/" + themeFile));

    /// <summary>Папка docs: README.md открыт сам (ADR-0007 Rule 2) и правится.</summary>
    private static async Task<ModalHarness> CreateHarnessAsync()
    {
        var loader = new StubDocumentLoader();
        loader.Sources[Readme] = new MarkdownSource(Readme, "README.md", "# readme");
        loader.Sources[Todo] = new MarkdownSource(Todo, "todo.md", "# todo");

        var fileSystem = new FakeWorkspaceFileSystem();
        fileSystem.AddDirectory(
            Root,
            WorkspaceEntry.ForFile(Readme, "README.md"),
            WorkspaceEntry.ForFile(Todo, "todo.md"));
        var platform = new FakePlatformServices(fileSystem);
        var saver = new RecordingDocumentSaver();

        var viewModel = new ShellViewModel(
            new OpenDocumentUseCase(loader),
            new SaveDocumentUseCase(saver),
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

        await viewModel.OpenFolderPathAsync(Root);
        Assert.Equal(Readme, viewModel.CurrentDocumentPath);
        await viewModel.ToggleEditModeCommand.ExecuteAsync(null);
        viewModel.EditorSession!.SourceText = "# readme edited";

        return new ModalHarness(viewModel, saver, platform);
    }

    private sealed record ModalHarness(ShellViewModel ViewModel, RecordingDocumentSaver Saver, FakePlatformServices Platform);
}
