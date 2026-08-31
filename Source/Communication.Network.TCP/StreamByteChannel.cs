using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Communication.Shared.Channels;

namespace Communication.Network.TCP;

/// <summary>
/// <see cref="TcpClient"/>/<see cref="NetworkStream"/>을 <see cref="IByteChannel"/>로 어댑팅한다.
/// 채널이 <see cref="TcpClient"/> 소유권을 갖고 Dispose에서 닫는다.
/// </summary>
public sealed class StreamByteChannel : IByteChannel
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private volatile bool _disposed;

    public StreamByteChannel(TcpClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _stream = client.GetStream();
    }

    /// <summary>keep-alive 등 소켓 옵션 적용에 쓸 원본 소켓.</summary>
    public Socket Socket => _client.Client;

    public bool IsConnected => !_disposed && _client.Connected;

    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => _stream.ReadAsync(buffer, cancellationToken);

    public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        => _stream.WriteAsync(buffer, cancellationToken);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _client.Dispose();
        }
        catch
        {
            // 닫기 중 예외가 정리를 막으면 안 된다.
        }
    }
}
