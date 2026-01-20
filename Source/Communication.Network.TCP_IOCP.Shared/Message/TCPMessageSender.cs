using Communication.Shared.Messages;
using System;
using System.Collections.Concurrent;
using System.Net.Sockets;

namespace Communication.Network.TCP_IOCP.Shared.Messages;

public sealed class TCPMessageSender : MessageSender, IDisposable
{
    private readonly Socket _socket;
    private bool _disposed;
    private readonly ConcurrentQueue<byte[]> _messageQueue = new();
    private readonly CancellationTokenSource _cancellationTokenSource;
    
    private readonly SocketAsyncEventArgs _sendEventArgs;
    private readonly byte[] _sendBuffer = new byte[64 * 1024]; // 64KB 버퍼
    private int _bufferOffset = 0;
    private int _bytesToSend = 0;
    private bool _isSending = false;

    public TCPMessageSender(IMessageConverter messageConverter, Socket socket)
        : base(messageConverter)
    {
        _socket = socket ?? throw new ArgumentNullException(nameof(socket));
        _cancellationTokenSource = new CancellationTokenSource();
        
        _sendEventArgs = new SocketAsyncEventArgs();
        _sendEventArgs.Completed += OnSendCompleted;
    }

    public override async Task SendAsync(object message, object context)
    {
        await SendAsync(message);
    }

    public override async Task SendAsync(object message)
    {
        byte[] serializedMessage = _messageConverter.Serialize(message);
        _messageQueue.Enqueue(serializedMessage);
        
        // 전송 중이 아니면 시작
        if (!_isSending)
        {
            StartSend();
        }
        
        await Task.CompletedTask;
    }

    private void StartSend()
    {
        if (_disposed || _cancellationTokenSource.IsCancellationRequested)
            return;

        // 큐가 비어있으면 대기
        if (!_messageQueue.TryDequeue(out byte[]? messageBytes))
        {
            _isSending = false;
            return;
        }

        _isSending = true;
        _bufferOffset = 0;

        int totalLength = 4 + messageBytes.Length; // 길이(4) + 본문
        _bytesToSend = totalLength;

        // 길이와 본문을 한번에 준비
        if (totalLength <= _sendBuffer.Length)
        {
            // 작은 메시지: 버퍼에 길이 + 본문 복사
            BitConverter.TryWriteBytes(_sendBuffer, messageBytes.Length);
            Array.Copy(messageBytes, 0, _sendBuffer, 4, messageBytes.Length);
            _sendEventArgs.SetBuffer(_sendBuffer, 0, totalLength);
        }
        else
        {
            // 큰 메시지: 별도 버퍼 할당
            byte[] largeBuffer = new byte[totalLength];
            BitConverter.TryWriteBytes(largeBuffer, messageBytes.Length);
            Array.Copy(messageBytes, 0, largeBuffer, 4, messageBytes.Length);
            _sendEventArgs.SetBuffer(largeBuffer, 0, totalLength);
            _sendEventArgs.UserToken = largeBuffer;
        }

        if (!_socket.SendAsync(_sendEventArgs))
        {
            ProcessSend(_sendEventArgs);
        }
    }

    private void OnSendCompleted(object? sender, SocketAsyncEventArgs e)
    {
        ProcessSend(e);
    }

    private void ProcessSend(SocketAsyncEventArgs e)
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
                Console.WriteLine($"Error sending: {e.SocketError}");
                _isSending = false;
                return;
            }

            _bufferOffset += e.BytesTransferred;
            _bytesToSend -= e.BytesTransferred;

            if (_bytesToSend > 0)
            {
                // 아직 더 보내야 함
                if (e.UserToken is byte[] largeBuffer)
                {
                    // 큰 메시지: 별도 버퍼 사용
                    e.SetBuffer(largeBuffer, _bufferOffset, _bytesToSend);
                }
                else
                {
                    // 작은 메시지: 재사용 버퍼 사용
                    e.SetBuffer(_sendBuffer, _bufferOffset, _bytesToSend);
                }

                if (!_socket.SendAsync(e))
                {
                    ProcessSend(e);
                }
                return;
            }

            // 메시지 전송 완료
            if (e.UserToken is byte[] buffer)
            {
                e.UserToken = null;
            }
            
            // 다음 메시지 전송 시작
            StartSend();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing send: {ex.Message}");
            _isSending = false;
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
        _sendEventArgs.Completed -= OnSendCompleted;
        _sendEventArgs.Dispose();
    }
}
