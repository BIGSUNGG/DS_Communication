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
    private readonly Action? _onDispose;
    private volatile bool _disposed;

    public StreamByteChannel(TcpClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _stream = client.GetStream();
    }

    /// <summary>Dispose 시 추가 정리 훅(수락 리스너의 연결 수 회수용 — 전송 패키지 내부 전용).</summary>
    internal StreamByteChannel(TcpClient client, Action onDispose)
        : this(client)
    {
        _onDispose = onDispose ?? throw new ArgumentNullException(nameof(onDispose));
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
        finally
        {
            // 훅은 닫기 성공 여부와 무관하게 실행 — 상한 슬롯은 반드시 회수된다.
            _onDispose?.Invoke();
        }
    }
}
