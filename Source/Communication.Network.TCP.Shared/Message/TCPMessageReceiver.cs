using Communication.Shared.Messages;
using System;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Communication.TCP.Shared.Messages
{
    public sealed class TCPMessageReceiver : MessageReceiver, IDisposable
    {
        private readonly NetworkStream _stream;
        private bool _disposed;
        private Task _receiveTask;
        private readonly CancellationTokenSource _cancellationTokenSource;

        public TCPMessageReceiver(IMessageConverter messageConverter, NetworkStream stream, IMessageHandler messageHandler)
            : base(messageConverter, messageHandler)
        {
            _stream = stream;
            _cancellationTokenSource = new CancellationTokenSource();
            _receiveTask = Task.Run(() => ReceiveLoopAsync(_cancellationTokenSource));
        }

        private async Task ReceiveLoopAsync(CancellationTokenSource cancellationTokenSource)
        {
            byte[] lengthBuffer = new byte[4];
            
            while (!cancellationTokenSource.IsCancellationRequested && !_disposed)
            {
                try
                {
                    // 메시지 길이 읽기 (4바이트)
                    int bytesRead = await _stream.ReadAsync(lengthBuffer, 0, 4, cancellationTokenSource.Token);
                    if (bytesRead == 0)
                    {
                        OnDetectedDisconnection();
                        break;
                    }

                    if (bytesRead < 4)
                    {
                        // 길이 프리픽스를 완전히 읽을 때까지 계속 읽기
                        int remaining = 4 - bytesRead;
                        while (remaining > 0)
                        {
                            int read = await _stream.ReadAsync(lengthBuffer, bytesRead, remaining, cancellationTokenSource.Token);
                            if (read == 0)
                            {
                                OnDetectedDisconnection();
                                return;
                            }
                            bytesRead += read;
                            remaining -= read;
                        }
                    }

                    int messageLength = BitConverter.ToInt32(lengthBuffer, 0);
                    if (messageLength <= 0 || messageLength > 1024 * 1024) // 최대 1MB
                    {
                        OnDetectedDisconnection();
                        break;
                    }

                    // 메시지 본문 읽기
                    byte[] messageBytes = new byte[messageLength];
                    bytesRead = 0;
                    while (bytesRead < messageLength)
                    {
                        int read = await _stream.ReadAsync(messageBytes, bytesRead, messageLength - bytesRead, cancellationTokenSource.Token);
                        if (read == 0)
                        {
                            OnDetectedDisconnection();
                            return;
                        }
                        bytesRead += read;
                    }

                    // 역직렬화 및 핸들러에 전달
                    var message = _messageConverter.Deserialize(messageBytes);
                    _messageHandler.HandleMessage(message);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error receiving message: {ex.Message}");
                    OnDetectedDisconnection();
                    break;
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
            _receiveTask.Wait(TimeSpan.FromSeconds(1));
            _cancellationTokenSource.Dispose();
        }
    }
}