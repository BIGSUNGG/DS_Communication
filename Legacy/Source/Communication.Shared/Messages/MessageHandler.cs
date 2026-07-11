using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using Communication.Shared.Sessions;
using Communication.Shared.Threading;

namespace Communication.Shared.Messages;

public abstract class MessageHandler : IMessageHandler, IDisposable
{
    protected ISession _session;

    bool _disposed = false;
    protected Dictionary<Type, Action<object>> _messageHandleActions = new Dictionary<Type, Action<object>>();
    private readonly SignalGate _signal = new();
    private Task? _processMessageQueueTask;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly ConcurrentQueue<object> _messageQueue = new();
    private int _pendingCount;
    private readonly SemaphoreSlim _capacity;
    private readonly MessageQueueOptions _options;

    public MessageHandler(ISession session, MessageQueueOptions? options = null)
    {
        _session = session;
        _options = options ?? new MessageQueueOptions();
        _capacity = new SemaphoreSlim(_options.MaxPendingMessages, _options.MaxPendingMessages);
        _cancellationTokenSource = new();

        RegisterMessageType();

        if (!_options.InlineDispatch)
        {
            _processMessageQueueTask = Task.Run(() => ProcessMessageQueueLoopAsync(_cancellationTokenSource.Token));
        }
    }

    public bool InlineDispatch => _options.InlineDispatch;

    protected abstract void RegisterMessageType();

    public void HandleMessage(object message)
    {
        if (_options.InlineDispatch)
        {
            DispatchOne(message);
            return;
        }

        if (!_capacity.Wait(0))
        {
            _capacity.Wait();
        }

        _messageQueue.Enqueue(message);
        Interlocked.Increment(ref _pendingCount);
        _signal.Signal();
    }

    private void DispatchOne(object message)
    {
        if (_messageHandleActions.TryGetValue(message.GetType(), out var handler))
        {
            handler(message);
        }
        else
        {
            Trace.WriteLine($"No handler registered for message type {message.GetType().Name}; skipping.");
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
                    Interlocked.Decrement(ref _pendingCount);
                    _capacity.Release();
                    DispatchOne(message);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error processing message queue: {ex.Message}");
            }
            finally
            {
                _signal.ResetPendingAndResignalIf(() => !_messageQueue.IsEmpty);
            }
        }
    }

    public virtual void OnDetectedDisconnection()
    {
        if (_session is Session session)
        {
            session.MarkDisconnected();
        }

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
        _signal.Dispose();

        if (_processMessageQueueTask != null)
        {
            try
            {
                _processMessageQueueTask.Wait(TimeSpan.FromSeconds(1));
            }
            catch
            {
            }
        }

        _capacity.Dispose();
        _cancellationTokenSource.Dispose();
    }
}
