using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Communication.Shared.Channels;

namespace Communication.Network.TCP;

/// <summary>
/// <see cref="TcpClient"/>/<see cref="NetworkStream"/>을 <see cref="IByteChannel"/>로 어댑팅한다.
/// 채널이 연결 리소스를 소유하며 Dispose에서 닫는다. TLS 경로는 인증이 완료된
/// <see cref="Stream"/>(<c>SslStream</c>)과 원본 소켓을 직접 감싼다.
/// </summary>
public sealed class StreamByteChannel : IByteChannel
{
    private readonly TcpClient? _client; // TcpClient 기반 경로에서만 설정
    private readonly Stream _stream;
    private readonly Socket _socket;
    private readonly Action? _onDispose;
    private volatile bool _disposed;

    /// <summary>TCP 클라이언트를 감싼다. Dispose가 <see cref="TcpClient"/>를 닫는다.</summary>
    public StreamByteChannel(TcpClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _stream = client.GetStream();
        _socket = client.Client;
    }

    /// <summary>Dispose 시 추가 정리 훅(수락 리스너의 연결 수 회수용 — 전송 패키지 내부 전용).</summary>
    internal StreamByteChannel(TcpClient client, Action onDispose)
        : this(client)
    {
        _onDispose = onDispose ?? throw new ArgumentNullException(nameof(onDispose));
    }

    /// <summary>
    /// 이미 확립된 스트림(TLS 인증 완료 <c>SslStream</c> 등)을 감싼다.
    /// Dispose가 스트림을 닫으며 이는 원본 소켓까지 닫는다. <paramref name="socket"/>은
    /// 연결 상태 조회(<see cref="IsConnected"/>)·소켓 옵션 적용용 원본이다.
    /// </summary>
    public StreamByteChannel(Stream stream, Socket socket)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _socket = socket ?? throw new ArgumentNullException(nameof(socket));
    }

    /// <summary>Dispose 시 추가 정리 훅(수락 리스너의 연결 수 회수용 — 전송 패키지 내부 전용).</summary>
    internal StreamByteChannel(Stream stream, Socket socket, Action onDispose)
        : this(stream, socket)
    {
        _onDispose = onDispose ?? throw new ArgumentNullException(nameof(onDispose));
    }

    /// <summary>keep-alive 등 소켓 옵션 적용에 쓸 원본 소켓.</summary>
    public Socket Socket => _socket;

    public bool IsConnected => !_disposed && _socket.Connected;

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
            // TcpClient 경로는 클라이언트(스트림 포함)를, 스트림 경로는 스트림을 닫는다 —
            // SslStream.Dispose는 내부 NetworkStream→소켓까지 닫는다.
            if (_client != null)
            {
                _client.Dispose();
            }
            else
            {
                _stream.Dispose();
            }
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
