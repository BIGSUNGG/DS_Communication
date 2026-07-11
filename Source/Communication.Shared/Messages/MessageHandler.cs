using Communication.Shared.Sessions;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Communication.Shared.Messages;

public abstract class MessageHandler : IMessageHandler, IDisposable
{
    protected ISession _session;

    bool _disposed = false;
    protected Dictionary<Type, Action<object>> _messageHandleActions = new Dictionary<Type, Action<object>>();
    private readonly SemaphoreSlim _signal = new(0, 1);
    private int _signalPending;
    private Task _processMessageQueueTask;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private ConcurrentQueue<object> _messageQueue = new ConcurrentQueue<object>();

    public MessageHandler(ISession session)
    {
        _session = session;
        _cancellationTokenSource = new();

        RegisterMessageType();

        _processMessageQueueTask = Task.Run(() => ProcessMessageQueueLoopAsync(_cancellationTokenSource.Token));
    }

    protected abstract void RegisterMessageType();

    public void HandleMessage(object message)
    {
        _messageQueue.Enqueue(message);
        Signal();
    }

    private void Signal()
    {
        if (Interlocked.CompareExchange(ref _signalPending, 1, 0) == 0)
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

    private async Task ProcessMessageQueueLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await _signal.WaitAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            try
            {
                while (_messageQueue.TryDequeue(out var message))
                {
                    if (_messageHandleActions.TryGetValue(message.GetType(), out var handler))
                    {
                        handler(message);
                    }
                    else
                    {
                        throw new InvalidOperationException($"No handler registered for message type {message.GetType().Name}");
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error processing message queue: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _signalPending, 0);
                if (!_messageQueue.IsEmpty)
                {
                    Signal();
                }
            }
        }
    }

    public virtual void OnDetectedDisconnection()
    {
        _session.Disconnect();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _cancellationTokenSource.Cancel();
        try
        {
            _signal.Release();
        }
        catch
        {
        }

        try
        {
            _processMessageQueueTask.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
        }

        _signal.Dispose();
        _cancellationTokenSource.Dispose();
    }
}
