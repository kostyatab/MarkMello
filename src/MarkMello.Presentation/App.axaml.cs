using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MarkMello.Application.Abstractions;
using MarkMello.Application.UseCases;
using MarkMello.Domain.Diagnostics;
using MarkMello.Presentation.Localization;
using MarkMello.Presentation.Services;
using MarkMello.Presentation.ViewModels;
using MarkMello.Presentation.Views;
using Microsoft.Extensions.DependencyInjection;

namespace MarkMello.Presentation;

public partial class App : global::Avalonia.Application
{
    /// <summary>
    /// Сервис-провайдер, передаваемый из Program.Main до создания AppBuilder.
    /// Statiс — обусловлено тем, что Avalonia сама создаёт инстанс App.
    /// </summary>
    public static IServiceProvider? Services { get; private set; }

    public static void RegisterServices(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // Имя приложения для macOS: заголовок системного меню и «Hide …». В .app его даёт
        // CFBundleName, но запуск собранного бинарника без бандла остался бы безымянным.
        Name = AppProductInfo.Name;

        var localization = Services?.GetService<ILocalizationService>() ?? new LocalizationService();
        Resources["Localization"] = localization;

        InstallMacOsApplicationMenu(localization);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (Services is null)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Приложение живёт, пока открыто хотя бы одно окно: вторая папка получает своё.
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnLastWindowClose;

            var metrics = Services.GetRequiredService<IStartupMetrics>();
            var window = Services.GetRequiredService<MainWindow>();

            // Stage 2 фиксируем после первого Opened — это момент, когда окно реально показалось пользователю,
            // а не просто инстанцировано.
            window.Opened += (_, _) => metrics.Mark(StartupStage.FirstWindow);

            desktop.MainWindow = window;

            // «Недавние» пишутся с паузой; на выходе запись идёт сразу и выход её дожидается.
            var services = Services;
            desktop.Exit += (_, _) => services.GetService<RecentItemsUseCase>()?.FlushBeforeExit(TimeSpan.FromSeconds(1));

            // Выход молча отменяет проверку и загрузку обновления; недокачанный файл удаляется.
            desktop.Exit += (_, _) => services.GetService<UpdateCoordinator>()?.Dispose();
        }

        WireFileActivationFromAvalonia();

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Системное меню приложения macOS (ADR-0009 Rule 4). Свои пункты — «О MarkMello» и под
    /// ним «Проверить обновления…» (ADR-0004, «Update Model»); без них Avalonia ставит туда
    /// своё «About Avalonia». Services, Hide, Show All и Quit остаются от Avalonia. Два пункта
    /// без сервисов: быстрый путь открытия документа не дорожает, окна About и обновления
    /// создаются только по нажатию.
    ///
    /// Ставится именно в <see cref="Initialize"/>: экспортёр меню Avalonia читает
    /// <c>NativeMenu</c> один раз, в <c>AfterSetup</c> сразу после этого метода, и если
    /// меню нет — сам записывает туда «About Avalonia». Позже свойство менять поздно:
    /// переэкспорта уже не будет.
    /// </summary>
    private void InstallMacOsApplicationMenu(ILocalizationService localization)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var about = new NativeMenuItem(localization["AppMenuAbout"]);
        about.Click += (_, _) => Services?.GetService<IWindowLauncher>()?.ShowAbout();

        var checkForUpdates = new NativeMenuItem(localization["AppMenuCheckForUpdates"]);
        checkForUpdates.Click += (_, _) => Services?.GetService<IWindowLauncher>()?.ShowUpdates(startCheck: true);

        // Подписи идут за языком приложения; системные пункты переводит сама Avalonia.
        localization.PropertyChanged += (_, _) =>
        {
            about.Header = localization["AppMenuAbout"];
            checkForUpdates.Header = localization["AppMenuCheckForUpdates"];
        };

        NativeMenu.SetMenu(this, new NativeMenu { Items = { about, checkForUpdates } });
    }

    /// <summary>
    /// Bridges Avalonia's <c>IActivatableLifetime.Activated</c> into the
    /// app's <see cref="IFileActivationPublisher"/>. On macOS Finder
    /// sends an <c>odoc</c> Apple Event instead of populating <c>argv</c>,
    /// both at cold-start and while the app is already running — both
    /// paths surface here as <c>ActivationKind.File</c>. Windows and
    /// Linux still receive their files through <c>argv</c>, so this
    /// hook is a no-op there.
    /// </summary>
    private void WireFileActivationFromAvalonia()
    {
        if (Services is null)
        {
            return;
        }

        if (TryGetFeature(typeof(IActivatableLifetime)) is not IActivatableLifetime activatable)
        {
            return;
        }

        var publisher = Services.GetRequiredService<IFileActivationPublisher>();
        activatable.Activated += (_, e) =>
        {
            if (e.Kind != ActivationKind.File || e is not FileActivatedEventArgs fileArgs)
            {
                return;
            }

            foreach (var file in fileArgs.Files)
            {
                if (file.Path is { IsFile: true } uri)
                {
                    publisher.NotifyFileActivated(uri.LocalPath);
                }
            }
        };
    }
}
