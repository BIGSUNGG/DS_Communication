using Common.Messages;
using Common.Messages.Converter;
using Common.Messages.Handler;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Common.Messages.Receiver;

public sealed class MessageReceiver : IMessageReceiver, IDisposable
{
    private readonly IMessageHandler _messageHandler;
    private readonly NetworkStream _stream;
    private readonly ConcurrentQueue<Message> _messageQueue;
    private readonly SemaphoreSlim _queueSemaphore;
    private readonly Task _processingTask;
    private readonly Task _receiveTask;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private bool _disposed;

    public MessageReceiver(NetworkStream stream, IMessageHandler messageHandler)
    {
        _stream = stream;
        _messageHandler = messageHandler;
        _messageQueue = new ConcurrentQueue<Message>();
        _queueSemaphore = new SemaphoreSlim(0);
        _cancellationTokenSource = new CancellationTokenSource();
        
        // 하나의 백그라운드 스레드에서 메시지 처리
        _processingTask = Task.Run(ProcessMessagesAsync);
        
        // 네트워크 수신 루프 시작
        _receiveTask = Task.Run(() => ReceiveLoopAsync(_cancellationTokenSource.Token));
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        const int receiveBufferSize = 8192; // 8KB 수신 버퍼
        const int messageBufferSize = 65536; // 64KB 메시지 버퍼
        
        // ArrayPool을 사용하여 메모리 할당 최소화
        var arrayPool = ArrayPool<byte>.Shared;
        var receiveBuffer = arrayPool.Rent(receiveBufferSize);
        var messageBuffer = arrayPool.Rent(messageBufferSize);
        
        int messageBufferOffset = 0; // 버퍼 내 데이터 시작 위치
        int messageBufferLength = 0; // 버퍼 내 데이터 길이

        try
        {
            while (!cancellationToken.IsCancellationRequested && !_disposed)
            {
                // 버퍼 공간이 부족하면 앞부분을 압축
                if (messageBufferOffset > 0 && messageBufferLength > 0)
                {
                    if (messageBufferOffset + messageBufferLength > messageBufferSize - receiveBufferSize)
                    {
                        // 남은 데이터를 앞으로 이동
                        Buffer.BlockCopy(messageBuffer, messageBufferOffset, messageBuffer, 0, messageBufferLength);
                        messageBufferOffset = 0;
                    }
                }

                // 스트림에서 데이터 읽기
                int availableSpace = messageBufferSize - (messageBufferOffset + messageBufferLength);
                int readSize = Math.Min(receiveBufferSize, availableSpace);
                int bytesRead = await _stream.ReadAsync(receiveBuffer, 0, readSize, cancellationToken);
                if (bytesRead == 0)
                {
                    // 연결 종료 감지
                    OnDetectedDisconnection();
                    break;
                }

                // 읽은 데이터를 메시지 버퍼에 복사
                int writeOffset = messageBufferOffset + messageBufferLength;
                Buffer.BlockCopy(receiveBuffer, 0, messageBuffer, writeOffset, bytesRead);
                messageBufferLength += bytesRead;

                // 버퍼에서 완전한 메시지들을 처리
                while (messageBufferLength >= 4)
                {
                    // Span을 사용하여 길이 프리픽스 확인 (배열 복사 없음)
                    var bufferSpan = new ReadOnlySpan<byte>(messageBuffer, messageBufferOffset, messageBufferLength);
                    int messageLength = BitConverter.ToInt32(bufferSpan);
                    
                    if (messageLength <= 0 || messageLength > 1024 * 1024) // 최대 1MB
                    {
                        // 잘못된 길이 - 연결 종료
                        OnDetectedDisconnection();
                        return;
                    }

                    int totalMessageSize = 4 + messageLength;
                    
                    // 완전한 메시지가 버퍼에 있는지 확인
                    if (messageBufferLength < totalMessageSize)
                    {
                        // 아직 완전한 메시지가 없음 - 더 읽어야 함
                        break;
                    }

                    // ArrayPool을 사용하여 메시지 배열 할당 (GC 압박 감소)
                    byte[] fullMessage = arrayPool.Rent(totalMessageSize);
                    
                    // 메시지 복사
                    Buffer.BlockCopy(messageBuffer, messageBufferOffset, fullMessage, 0, totalMessageSize);

                    // 처리한 메시지만큼 버퍼 오프셋 이동
                    messageBufferOffset += totalMessageSize;
                    messageBufferLength -= totalMessageSize;

                    // 메시지 역직렬화 및 큐에 추가 (fire-and-forget)
                    _ = Task.Run(() => ProcessReceivedMessage(fullMessage, arrayPool));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 정상적인 취소
        }
        catch (IOException)
        {
            OnDetectedDisconnection();
        }
        catch (SocketException)
        {
            OnDetectedDisconnection();
        }
        catch (ObjectDisposedException)
        {
            OnDetectedDisconnection();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"메시지 수신 오류: {ex.Message}");
            // 연결 종료 감지
            OnDetectedDisconnection();
        }
        finally
        {
            // 버퍼 반환
            arrayPool.Return(receiveBuffer);
            arrayPool.Return(messageBuffer);
        }
    }

    private void ProcessReceivedMessage(byte[] fullMessage, ArrayPool<byte> arrayPool)
    {
        try
        {
            // Span을 사용하여 길이 프리픽스(처음 4바이트)를 제거하고 메시지 데이터만 추출
            ReadOnlySpan<byte> messageSpan = fullMessage;
            ReadOnlySpan<byte> messageData = messageSpan.Slice(4);

            // 역직렬화
            var deserializedMessage = MessageConverter.Instance.Deserialize(messageData);

            // 큐에 메시지 추가
            _messageQueue.Enqueue(deserializedMessage);
            _queueSemaphore.Release(); // 큐에 메시지가 추가되었음을 알림
        }
        catch (Exception ex)
        {
            Console.WriteLine($"메시지 역직렬화 오류: {ex.Message}");
        }
        finally
        {
            // 메시지 처리 후 배열 반환
            arrayPool.Return(fullMessage);
        }
    }
    private async Task ProcessMessagesAsync()
    {
        while (!_cancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                // 큐에 메시지가 있을 때까지 대기
                await _queueSemaphore.WaitAsync(_cancellationTokenSource.Token);

                // 큐에서 메시지 처리
                while (_messageQueue.TryDequeue(out var message))
                {
                    try
                    {
                        _messageHandler.HandleMessage(message);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"메시지 처리 오류: {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 정상적인 취소
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"메시지 처리 루프 오류: {ex.Message}");
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
        
        try
        {
            _processingTask.Wait(TimeSpan.FromSeconds(1));
            _receiveTask.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
            // 무시
        }

        _cancellationTokenSource.Dispose();
        _queueSemaphore.Dispose();
    }

    private void OnDetectedDisconnection()
    {
        // IMessageHandler를 통해 Session에 연결 종료 알림
        _messageHandler.OnDetectedDisconnection();
    }
}