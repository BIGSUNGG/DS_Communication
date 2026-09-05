using System.Net.Sockets;
using Communication.Network.TCP;
using Xunit;

namespace Communication.Tests;

/// <summary>
/// `KeepAliveApplicator` — 소스에서 유일하게 직접 커버리지가 없던 단위.
/// 플랫폼별 세부 값(IOCTL·raw option)은 검증하지 않고, 계약만 고정한다:
/// 끄기(null·Enabled=false)는 손대지 않고, 켜기는 소켓 keep-alive 플래그를 세우며 예외를 던지지 않는다.
/// </summary>
public class KeepAliveApplicatorTests
{
    [Fact]
    public void NullOrDisabledOptions_LeaveSocketUntouched()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        KeepAliveApplicator.Apply(socket, null);
        Assert.Equal(0, (int)(socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive) ?? 0));

        KeepAliveApplicator.Apply(socket, new SocketKeepAliveOptions
        {
            Enabled = false,
            IdleTime = TimeSpan.FromSeconds(1),
        });
        Assert.Equal(0, (int)(socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive) ?? 0));
    }

    [Fact]
    public void Enabled_WithValues_SetsKeepAlive_WithoutThrowing()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        KeepAliveApplicator.Apply(socket, new SocketKeepAliveOptions
        {
            Enabled = true,
            IdleTime = TimeSpan.FromSeconds(1),
            Interval = TimeSpan.FromSeconds(1),
        });

        Assert.Equal(1, (int)(socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive) ?? 0));
    }

    [Fact]
    public void Enabled_WithZeroValues_JustEnablesKeepAlive()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        // IdleTime·Interval 기본 0 — '켜기만' 요청은 플래그만 세우고 세부 값은 건드리지 않는다.
        KeepAliveApplicator.Apply(socket, new SocketKeepAliveOptions { Enabled = true });

        Assert.Equal(1, (int)(socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive) ?? 0));
    }
}