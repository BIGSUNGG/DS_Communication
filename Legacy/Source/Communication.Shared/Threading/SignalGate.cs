using System.Diagnostics;

namespace Communication.Shared.Threading;

/// <summary>
/// SemaphoreSlim(0,1)에 Interlocked 게이트를 결합해 연속 Release로 인한 SemaphoreFullException을 방지합니다.
/// </summary>
public sealed class SignalGate : IDisposable
{
    private readonly SemaphoreSlim _signal = new(0, 1);
    private int _pending;
    private bool _disposed;

    public void Signal()
    {
        if (_disposed)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _pending, 1, 0) == 0)
        {
            try
            {
                _signal.Release();
            }
            catch (ObjectDisposedException)
            {
            }
            catch (SemaphoreFullException)
            {
            }
        }
    }

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        await _signal.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void ResetPendingAndResignalIf(Func<bool> shouldResignal)
    {
        Interlocked.Exchange(ref _pending, 0);
        if (shouldResignal())
        {
            Signal();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _signal.Release();
        }
        catch
        {
        }

        _signal.Dispose();
    }
}
