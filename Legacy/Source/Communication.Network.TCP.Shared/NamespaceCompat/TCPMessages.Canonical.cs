using Communication.Shared.Messages;
using System.Net.Sockets;

namespace Communication.Network.TCP.Shared.Messages;

/// <summary>Canonical namespace alias for <see cref="Communication.TCP.Shared.Messages.TCPMessageSender"/>.</summary>
public sealed class TCPMessageSender : Communication.TCP.Shared.Messages.TCPMessageSender
{
    public TCPMessageSender(IMessageConverter messageConverter, NetworkStream stream, MessageQueueOptions? options = null)
        : base(messageConverter, stream, options)
    {
    }
}

/// <summary>Canonical namespace alias for <see cref="Communication.TCP.Shared.Messages.TCPMessageReceiver"/>.</summary>
public sealed class TCPMessageReceiver : Communication.TCP.Shared.Messages.TCPMessageReceiver
{
    public TCPMessageReceiver(IMessageConverter messageConverter, NetworkStream stream, IMessageHandler messageHandler)
        : base(messageConverter, stream, messageHandler)
    {
    }
}
