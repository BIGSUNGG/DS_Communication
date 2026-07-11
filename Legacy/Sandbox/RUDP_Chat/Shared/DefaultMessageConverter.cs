using System;
using MessageProtocol.Serialize;
using Communication.Shared.Messages;

namespace RUDP_Chat.Shared.Messages
{
    public class MessageConverter : IMessageConverter
    {
        public byte[] Serialize(object message)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            return MessageSerializer.Serialize(message);
        }

        public object Deserialize(ReadOnlySpan<byte> message)
        {
            if (message.Length == 0)
                throw new ArgumentException("Message buffer is empty", nameof(message));

            var deserialized = MessageSerializer.Deserialize(message.ToArray());
            if (deserialized == null)
                throw new InvalidOperationException("Failed to deserialize message");

            return deserialized;
        }
    }
}
