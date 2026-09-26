using MarkMello.Application.Abstractions;
using MarkMello.Application.Updates;

namespace MarkMello.Presentation.Services;

/// <summary>
/// Одна отложенная фоновая проверка на весь запуск, общая для всех окон (ADR-0004,
/// «Update Model»). Пауза начинается с первым обращением — из <c>Opened</c> первого
/// окна, — так что до показа окна в сеть не ходит ничего. Выход отменяет проверку
/// ещё до сетевого запроса.
/// </summary>
public sealed class DeferredUpdateCheck : IDisposable
{
    private readonly IUpdateService _updateService;
    private readonly TimeSpan _delay;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _gate = new();
    private Task<UpdateCheckResult>? _check;
    private bool _isDisposed;

    public DeferredUpdateCheck(IUpdateService updateService)
        : this(updateService, TimeSpan.FromSeconds(30))
    {
    }

    internal DeferredUpdateCheck(IUpdateService updateService, TimeSpan delay)
    {
        ArgumentNullException.ThrowIfNull(updateService);
        _updateService = updateService;
        _delay = delay;
    }

    /// <summary>
    /// Результат общей проверки. Первый вызов её запускает, остальные ждут того же
    /// результата. Отмена <paramref name="cancellationToken"/> снимает только этого
    /// ждущего, а не саму проверку.
    /// </summary>
    public Task<UpdateCheckResult> WaitAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            _check ??= CheckAsync(_shutdown.Token);
            return _check.WaitAsync(cancellationToken);
        }
    }

    private async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
        try
        {
            return await _updateService.CheckForUpdatesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new UpdateCheckResult.Failed(exception.Message);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
        }

        _shutdown.Cancel();
        _shutdown.Dispose();
    }
}
