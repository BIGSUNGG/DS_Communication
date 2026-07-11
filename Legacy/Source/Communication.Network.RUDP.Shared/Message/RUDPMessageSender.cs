using Communication.Shared.Messages;
using Communication.Shared.Threading;
using LiteNetLib;
using LiteNetLib.Utils;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Communication.Network.RUDP.Shared.Messages
{
    public sealed class RUDPMessageSender : MessageSender, IDisposable
    {
        private bool _disposed;
        private readonly NetPeer _netPeer;
        private readonly NetDataWriter _dataWriter;
        private readonly SignalGate _signal = new();
        private readonly ConcurrentQueue<(byte[] Data, DeliveryMethod Method, TaskCompletionSource<bool>? FlushTcs)> _messageQueue = new();
        private readonly SemaphoreSlim _backpressure;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly Task _processMessageQueueTask;

        public RUDPMessageSender(IMessageConverter messageConverter, NetPeer netPeer, MessageQueueOptions? options = null)
            : base(messageConverter)
        {
            _netPeer = netPeer;
            _dataWriter = new NetDataWriter();
            options ??= new MessageQueueOptions();
            _backpressure = new SemaphoreSlim(options.MaxPendingMessages, options.MaxPendingMessages);
            _cancellationTokenSource = new CancellationTokenSource();
            _processMessageQueueTask = Task.Run(() => ProcessMessageQueueLoopAsync(_cancellationTokenSource.Token));
        }

        public override Task SendAsync(object message, object context)
        {
            return EnqueueAsync(message, context, flush: false, CancellationToken.None);
        }

        public override Task SendAsync(object message)
        {
            return SendAsync(message, new MessageSendContext(ReliableType.ReliableOrdered));
        }

        public override Task SendAndFlushAsync(object message, object? context = null, CancellationToken cancellationToken = default)
        {
            return EnqueueAsync(message, context ?? new MessageSendContext(ReliableType.ReliableOrdered), flush: true, cancellationToken);
        }

        private async Task EnqueueAsync(object message, object context, bool flush, CancellationToken cancellationToken)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(RUDPMessageSender));
            }

            DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered;
            if (context is MessageSendContext sendContext)
            {
                deliveryMethod = (DeliveryMethod)sendContext.Reliable;
            }

            await _backpressure.WaitAsync(cancellationToken).ConfigureAwait(false);

            byte[] serializedMessage = _messageConverter.Serialize(message);
            TaskCompletionSource<bool>? flushTcs = flush
                ? new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
                : null;

            _messageQueue.Enqueue((serializedMessage, deliveryMethod, flushTcs));
            _signal.Signal();

            if (flushTcs != null)
            {
                using (cancellationToken.Register(static state =>
                {
                    ((TaskCompletionSource<bool>)state!).TrySetCanceled();
                }, flushTcs))
                {
                    await flushTcs.Task.ConfigureAwait(false);
                }
            }
        }

        private async Task ProcessMessageQueueLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await _signal.WaitAsync(cancellationToken).ConfigureAwait(false);
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
                    while (_messageQueue.TryDequeue(out var messageData))
                    {
                        try
                        {
                            _backpressure.Release();
                        }
                        catch (ObjectDisposedException)
                        {
                        }
                        catch (SemaphoreFullException)
                        {
                        }

                        try
                        {
                            if (_netPeer != null && _netPeer.ConnectionState == ConnectionState.Connected)
                            {
                                _dataWriter.Reset();
                                _dataWriter.Put(messageData.Data);
                                _netPeer.Send(_dataWriter, messageData.Method);
                            }

                            messageData.FlushTcs?.TrySetResult(true);
                        }
                        catch (Exception ex)
                        {
                            Trace.WriteLine($"Error sending message: {ex.Message}");
                            messageData.FlushTcs?.TrySetException(ex);
                        }
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

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _cancellationTokenSource.Cancel();
            _signal.Dispose();

            while (_messageQueue.TryDequeue(out var item))
            {
                try
                {
                    _backpressure.Release();
                }
                catch
                {
                }

                item.FlushTcs?.TrySetCanceled();
            }

            try
            {
                _processMessageQueueTask.Wait(TimeSpan.FromSeconds(1));
            }
            catch
            {
            }

            _cancellationTokenSource.Dispose();
            _backpressure.Dispose();
            _dataWriter.Reset();
        }
    }
}
