using MarkMello.Application.Abstractions;
using MarkMello.Domain.Recent;

namespace MarkMello.Application.UseCases;

/// <summary>
/// «Недавние» стартового экрана (ADR-0009 Rule 8). Один экземпляр на процесс — список
/// общий для всех окон. Список живёт в памяти, запись в <c>settings.json</c> уходит в фон:
/// <see cref="ISettingsStore"/> пишет файл синхронно, а открытие документа ждать её не должно.
/// </summary>
public sealed class RecentItemsUseCase
{
    private readonly ISettingsStore _settings;
    private readonly IPathExistenceProbe _probe;
    private readonly TimeProvider _time;
    private readonly bool _ignoreCase;
    private readonly TimeSpan _writeDelay;
    private readonly bool _isReadOnly;
    private readonly Lock _gate = new();

    /// <summary>Завершается, когда процесс выходит: отложенная запись идёт сразу, без паузы.</summary>
    private readonly TaskCompletionSource _skipWriteDelay = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private IReadOnlyList<RecentEntry>? _entries;
    private Task _lastWrite = Task.CompletedTask;
    private bool _isWriteScheduled;

    /// <summary>
    /// Пауза перед записью. Файл из Finder или командной строки записывается в «Недавние»
    /// на старте, и запись файла настроек не должна делить с первым кадром ни диск, ни
    /// процессор; заодно несколько правок подряд сливаются в одну запись.
    /// </summary>
    public static readonly TimeSpan DefaultWriteDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// В smoke-режиме список только читается: замер открывает файл и папку теми же путями,
    /// что и пользователь, и иначе писал бы их в настоящие «Недавние» разработчика.
    /// </summary>
    public RecentItemsUseCase(
        ISettingsStore settings,
        IPathExistenceProbe probe,
        StartupSmokeTestOptions smokeTestOptions)
        : this(settings, probe, TimeProvider.System, OperatingSystem.IsWindows(), DefaultWriteDelay)
    {
        ArgumentNullException.ThrowIfNull(smokeTestOptions);
        _isReadOnly = smokeTestOptions.IsEnabled;
    }

    public RecentItemsUseCase(
        ISettingsStore settings,
        IPathExistenceProbe probe,
        TimeProvider time,
        bool ignoreCase,
        TimeSpan writeDelay)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(time);

        _settings = settings;
        _probe = probe;
        _time = time;
        _ignoreCase = ignoreCase;
        _writeDelay = writeDelay;
    }

    /// <summary>Текущее местное время — от него считаются подписи «сегодня» и «вчера».</summary>
    public DateTimeOffset LocalNow => _time.GetLocalNow();

    /// <summary>
    /// Список читается при первом обращении, а не на старте: запуск с файлом стартового
    /// экрана не показывает, и «Недавние» ему не нужны.
    /// </summary>
    public async ValueTask<IReadOnlyList<RecentEntry>> GetEntriesAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_entries is { } loaded)
            {
                return loaded;
            }
        }

        var stored = await _settings.LoadRecentAsync(cancellationToken).ConfigureAwait(false);
        var normalized = RecentEntryList.Normalize(stored, _ignoreCase);

        lock (_gate)
        {
            // Пока шло чтение, запись могла уже появиться — её список новее прочитанного.
            return _entries ??= normalized;
        }
    }

    public Task RecordFileAsync(string path) => RecordAsync(path, RecentEntryKind.File);

    public Task RecordFolderAsync(string path) => RecordAsync(path, RecentEntryKind.Folder);

    public async Task RemoveAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (_isReadOnly)
        {
            return;
        }

        await GetEntriesAsync().ConfigureAwait(false);
        Update(entries => RecentEntryList.Remove(entries, path, _ignoreCase));
    }

    public async Task ClearAsync()
    {
        if (_isReadOnly)
        {
            return;
        }

        await GetEntriesAsync().ConfigureAwait(false);
        Update(static _ => []);
    }

    /// <summary>Пути записей, которых больше нет на диске. Проверка идёт вне UI-потока.</summary>
    public Task<IReadOnlyList<string>> FindMissingAsync(
        IReadOnlyList<RecentEntry> entries,
        CancellationToken cancellationToken = default)
        => _probe.FindMissingAsync(entries, cancellationToken);

    /// <summary>Дождаться записи в настройки — для тестов.</summary>
    public Task FlushAsync()
    {
        lock (_gate)
        {
            return _lastWrite;
        }
    }

    /// <summary>
    /// Процесс выходит: запланированная запись идёт без паузы, и выход ждёт её не дольше
    /// <paramref name="timeout"/>. Иначе «Сохранить как…» и сразу ⌘Q теряли бы запись.
    /// </summary>
    public void FlushBeforeExit(TimeSpan timeout)
    {
        _skipWriteDelay.TrySetResult();

        try
        {
            FlushAsync().Wait(timeout);
        }
        catch (AggregateException)
        {
            // Запись best-effort: выход не должен падать из-за «Недавних».
        }
    }

    private async Task RecordAsync(string path, RecentEntryKind kind)
    {
        if (_isReadOnly || string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            return;
        }

        await GetEntriesAsync().ConfigureAwait(false);
        var entry = new RecentEntry(path, kind, _time.GetLocalNow());
        Update(entries => RecentEntryList.Add(entries, entry, _ignoreCase));
    }

    private void Update(Func<IReadOnlyList<RecentEntry>, IReadOnlyList<RecentEntry>> change)
    {
        lock (_gate)
        {
            _entries = change(_entries ?? []);

            // Уже запланированная запись возьмёт и эту правку: снимок берётся перед записью.
            if (!_isWriteScheduled)
            {
                _isWriteScheduled = true;
                _lastWrite = PersistAfterAsync(_lastWrite);
            }
        }
    }

    /// <summary>
    /// Записи идут цепочкой, по одной, и каждая берёт самый свежий список: две быстрые правки
    /// подряд не могут записаться в обратном порядке и вернуть в файл устаревший список.
    /// </summary>
    private async Task PersistAfterAsync(Task previous)
    {
        await previous.ConfigureAwait(false);

        try
        {
            if (_writeDelay > TimeSpan.Zero)
            {
                await Task.WhenAny(Task.Delay(_writeDelay, _time), _skipWriteDelay.Task).ConfigureAwait(false);
            }

            await Task.Run(async () =>
                {
                    IReadOnlyList<RecentEntry> snapshot;
                    lock (_gate)
                    {
                        _isWriteScheduled = false;
                        snapshot = _entries ?? [];
                    }

                    await _settings.SaveRecentAsync(snapshot).ConfigureAwait(false);
                })
                .ConfigureAwait(false);
        }
        catch
        {
            // Запись best-effort, как у параметров чтения: «Недавние» не должны мешать чтению.
        }
    }
}
