using DS.Communication.Shared.Messages;
using DS.Communication.Shared.Messages.Converter;
using System;
using System.Buffers;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace DS.Communication.Shared.Messages.Sender
{
    public sealed class MessageSender : IMessageSender, IDisposable
    {      
        private readonly NetworkStream _stream;    
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private bool _disposed;
        private readonly ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;

        public MessageSender(NetworkStream stream)
        {
            _stream = stream;
        }
        
        public async Task SendAsync(Message message)
        {
            if (_disposed)
            {
                return;
            }

            byte[]? messageData = null;
            byte[]? fullMessage = null;

            try
            {
                // 메시지를 바이트 배열로 직렬화
                messageData = MessageConverter.Instance.Serialize(message);
                
                // ArrayPool을 사용하여 메모리 할당 최소화
                int totalSize = 4 + messageData.Length;
                fullMessage = _arrayPool.Rent(totalSize);
                
                // Span을 사용하여 효율적으로 데이터 복사
                Span<byte> fullMessageSpan = fullMessage.AsSpan(0, totalSize);
                BitConverter.TryWriteBytes(fullMessageSpan, messageData.Length);
                messageData.AsSpan().CopyTo(fullMessageSpan.Slice(4));
                
                await _sendLock.WaitAsync();
                try
                {
                    // 길이 프리픽스 + 메시지 데이터 전송
                    await _stream.WriteAsync(fullMessage, 0, totalSize);                    
                }
                finally
                {
                    _sendLock.Release();
                }
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
            finally
            {
                // 배열 반환
                if (fullMessage != null)
                {
                    _arrayPool.Return(fullMessage);
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
            _sendLock.Dispose();
        }
    }
}
