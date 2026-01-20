using Communication.Shared.Sessions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Communication.Shared.Messages;

public abstract class MessageHandler : IMessageHandler, IDisposable
{
    protected ISession _session;

    bool _disposed = false;
    protected Dictionary<Type, Action<object>> _messageHandleActions = new Dictionary<Type, Action<object>>();
    private SemaphoreSlim _lock = new (0,1);
    private Task _processMessageQueueTask;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private ConcurrentQueue<object> _messageQueue = new ConcurrentQueue<object>();

    public MessageHandler(ISession session)
    {
        _session = session;
        _cancellationTokenSource = new();

        RegisterMessageType();

        _processMessageQueueTask= Task.Run(() => ProcessMessageQueueLoopAsync(_cancellationTokenSource));
    }

    protected abstract void RegisterMessageType();

    public void HandleMessage(object message)
    {
        _messageQueue.Enqueue(message);
        _lock.Release();
    }

    async void ProcessMessageQueueLoopAsync(CancellationTokenSource token)
    {
        while (!token.IsCancellationRequested)
        {
            await _lock.WaitAsync(token.Token);
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
        }
    }

    public virtual void OnDetectedDisconnection()
    {
        _session.Disconnect();
    }

    public void Dispose()
    {
        if(_disposed)
        {
            return;
        }

        _disposed = true;

        _lock.Dispose();

        try
        {
            _processMessageQueueTask.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
        }

        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
    }
}
