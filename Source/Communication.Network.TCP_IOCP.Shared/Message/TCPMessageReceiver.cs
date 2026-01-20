using Communication.Shared.Messages;
using System;
using System.Net.Sockets;

namespace Communication.Network.TCP_IOCP.Shared.Messages;

public sealed class TCPMessageReceiver : MessageReceiver, IDisposable
{
    private readonly Socket _socket;
    private bool _disposed;
    private readonly CancellationTokenSource _cancellationTokenSource;
    
    private readonly SocketAsyncEventArgs _receiveEventArgs;
    private readonly byte[] _receiveBuffer = new byte[64 * 1024]; // 64KB 버퍼
    private int _bufferOffset = 0;
    private int _bytesNeeded = 4; // 처음에는 길이 4바이트 필요
    private bool _readingLength = true;

    public TCPMessageReceiver(IMessageConverter messageConverter, Socket socket, IMessageHandler messageHandler)
        : base(messageConverter, messageHandler)
    {
        _socket = socket ?? throw new ArgumentNullException(nameof(socket));
        _cancellationTokenSource = new CancellationTokenSource();
        
        _receiveEventArgs = new SocketAsyncEventArgs();
        _receiveEventArgs.Completed += OnReceiveCompleted;
        
        StartReceive();
    }

    private void StartReceive()
    {
        if (_disposed || _cancellationTokenSource.IsCancellationRequested)
            return;

        try
        {
            int bytesToReceive = Math.Min(_bytesNeeded, _receiveBuffer.Length - _bufferOffset);
            _receiveEventArgs.SetBuffer(_receiveBuffer, _bufferOffset, bytesToReceive);

            if (!_socket.ReceiveAsync(_receiveEventArgs))
                ProcessReceive(_receiveEventArgs);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error starting receive: {ex.Message}");
            OnDetectedDisconnection();
        }
    }

    private void OnReceiveCompleted(object? sender, SocketAsyncEventArgs e)
    {
        if (e.UserToken is (byte[] buffer, int length))
        {
            ProcessLargeMessageReceive(e, buffer, length);
        }
        else
        {
            ProcessReceive(e);
        }
    }

    private void ProcessReceive(SocketAsyncEventArgs e)
    {
        if (_disposed || _cancellationTokenSource.IsCancellationRequested)
            return;

        try
        {
            if (e.SocketError != SocketError.Success)
            {
                if (e.SocketError == SocketError.OperationAborted)
                {
                    return;
                }
                OnDetectedDisconnection();
                return;
            }

            if (e.BytesTransferred == 0)
            {
                // 연결 종료
                OnDetectedDisconnection();
                return;
            }

            _bufferOffset += e.BytesTransferred;
            _bytesNeeded -= e.BytesTransferred;

            if (_bytesNeeded > 0)
            {
                StartReceive();
                return;
            }

            // 필요한 바이트를 모두 읽음
            if (_readingLength)
            {
                // 길이 읽기 완료
                int messageLength = BitConverter.ToInt32(_receiveBuffer, 0);
                if (messageLength <= 0 || messageLength > 1024 * 1024) // 최대 1MB
                {
                    OnDetectedDisconnection();
                    return;
                }

                // 메시지 본문 읽기 시작
                _bufferOffset = 0;
                _bytesNeeded = messageLength;
                _readingLength = false;

                // 버퍼가 충분한지 확인
                if (messageLength > _receiveBuffer.Length)
                {
                    // 큰 메시지는 별도 버퍼 할당
                    ReadLargeMessage(messageLength);
                    return;
                }
            }
            else
            {
                // 메시지 본문 읽기 완료
                try
                {
                    var message = _messageConverter.Deserialize(new ReadOnlySpan<byte>(_receiveBuffer, 0, _bufferOffset));
                    _messageHandler.HandleMessage(message);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error deserializing message: {ex.Message}");
                }

                // 다음 메시지 읽기 시작
                _bufferOffset = 0;
                _bytesNeeded = 4;
                _readingLength = true;
            }

            // 다음 수신 시작
            StartReceive();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing receive: {ex.Message}");
            OnDetectedDisconnection();
        }
    }

    private void ReadLargeMessage(int messageLength)
    {
        // 큰 메시지는 별도 버퍼에 읽기
        byte[] largeBuffer = new byte[messageLength];
        
        // 나머지 읽기
        _receiveEventArgs.SetBuffer(largeBuffer, 0, messageLength);
        _receiveEventArgs.UserToken = (largeBuffer, messageLength);
        
        if (!_socket.ReceiveAsync(_receiveEventArgs))
        {
            ProcessLargeMessageReceive(_receiveEventArgs, largeBuffer, messageLength);
        }
    }

    private void ProcessLargeMessageReceive(SocketAsyncEventArgs e, byte[] buffer, int messageLength)
    {
        if (_disposed || _cancellationTokenSource.IsCancellationRequested)
            return;

        try
        {
            if (e.SocketError != SocketError.Success)
            {
                if (e.SocketError == SocketError.OperationAborted)
                {
                    return;
                }
                OnDetectedDisconnection();
                return;
            }

            if (e.BytesTransferred == 0)
            {
                OnDetectedDisconnection();
                return;
            }

            int totalReceived = e.Offset + e.BytesTransferred;
            
            if (totalReceived < messageLength)
            {
                // 아직 더 읽어야 함
                e.SetBuffer(buffer, totalReceived, messageLength - totalReceived);
                if (!_socket.ReceiveAsync(e))
                {
                    ProcessLargeMessageReceive(e, buffer, messageLength);
                }
                return;
            }

            // 큰 메시지 읽기 완료
            try
            {
                var message = _messageConverter.Deserialize(new ReadOnlySpan<byte>(buffer, 0, messageLength));
                _messageHandler.HandleMessage(message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deserializing large message: {ex.Message}");
            }

            // 다음 메시지 읽기 시작
            _bufferOffset = 0;
            _bytesNeeded = 4;
            _readingLength = true;
            e.UserToken = null;
            StartReceive();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing large message receive: {ex.Message}");
            OnDetectedDisconnection();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();

        // EventArgs 정리
        _receiveEventArgs.Completed -= OnReceiveCompleted;
        _receiveEventArgs.Dispose();
    }
}
