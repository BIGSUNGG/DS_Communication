using System;
using System.Threading;
using System.Threading.Tasks;

namespace Communication.Shared.Threading;

/// <summary>
/// SemaphoreSlim(0,1)에 Interlocked 게이트를 결합해 연속 <see cref="Signal"/>로도
/// <see cref="SemaphoreFullException"/>이 나지 않는 단일 시그널 게이트. 송신·디스패치 웨이크업에 사용한다.
/// </summary>
public sealed class SignalGate : IDisposable
{
    private readonly SemaphoreSlim _signal = new(0, 1);
    private int _pending;
    private bool _disposed;

    /// <summary>대기자를 깨운다. 이미 깨울 신호가 걸려 있으면 아무 일도 하지 않는다.</summary>
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

    /// <summary>신호를 기다린다. <see cref="Dispose"/>로 풀리면 이후 대기는 즉시 취소로 끝난다.</summary>
    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        await _signal.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>pending을 지우고, 조건이 참이면 다시 신호를 건다(드레인 후 재점검용).</summary>
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
            // dispose 해제 — 대기자가 깨어나 취소 경로로 빠진다.
        }

        _signal.Dispose();
    }
}
