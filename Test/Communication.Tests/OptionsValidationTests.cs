using Communication.Network.RUDP;
using Communication.Network.TCP;
using Communication.Shared.Framing;
using Communication.Shared.Messages;
using Xunit;

namespace Communication.Tests;

/// <summary>
/// 공개 옵션 표면의 검증 계약 — 잘못된 값은 설정 시점에 거부되어야 한다.
/// (회귀 리팩터링이 검증을 조용히 완화하면 계약이 바뀌므로 핀으로 고정한다.)
/// </summary>
public class OptionsValidationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void TcpTransportOptions_MaxConnections_RejectsNonPositive(int value)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new TcpTransportOptions { MaxConnections = value });

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void TcpTransportOptions_ConnectTimeout_RejectsNonPositive(int value)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new TcpTransportOptions { ConnectTimeout = value });

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RudpTransportOptions_MaxConnections_RejectsNonPositive(int value)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new RudpTransportOptions { MaxConnections = value });

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void RudpTransportOptions_DisconnectTimeout_RejectsNonPositive(int value)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new RudpTransportOptions { DisconnectTimeout = value });

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RudpTransportOptions_ConnectTimeout_RejectsNonPositive(int value)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new RudpTransportOptions { ConnectTimeout = value });

    [Fact]
    public void RudpTransportOptions_ConnectionKey_NullOrEmpty_Rejected()
    {
        Assert.Throws<ArgumentNullException>(() => new RudpTransportOptions { ConnectionKey = null! });
        Assert.Throws<ArgumentException>(() => new RudpTransportOptions { ConnectionKey = "" });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void MessageQueueOptions_MaxPendingMessages_RejectsNonPositive(int value)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new MessageQueueOptions { MaxPendingMessages = value });

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void MessageQueueOptions_CoalesceLimitBytes_RejectsNonPositive(int value)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new MessageQueueOptions { CoalesceLimitBytes = value });

    [Theory]
    [InlineData(-1)]
    public void MessageQueueOptions_FrameTimeout_RejectsNegative(long ticks)
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => new MessageQueueOptions { FrameTimeout = TimeSpan.FromTicks(ticks) });

    [Fact]
    public void MessageQueueOptions_MaxFrameLength_RejectsOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MessageQueueOptions { MaxFrameLength = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new MessageQueueOptions { MaxFrameLength = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new MessageQueueOptions { MaxFrameLength = LengthPrefixFramer.MaxFrameLength + 1 });
    }
}