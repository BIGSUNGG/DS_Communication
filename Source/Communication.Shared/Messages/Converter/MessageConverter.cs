using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DS.Communication.Shared.Messages;
using System.IO;
using DS.MessageProtocol.Serialize;

namespace DS.Communication.Shared.Messages.Converter
{
    public class MessageConverter
    {
        public static MessageConverter Instance { get; private set; } = new();
        public byte[] Serialize(Message message)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            return MessageSerializer.Instance.Serialize(message);
        }

        public Message Deserialize(ReadOnlySpan<byte> message)
        {
            if (message.Length == 0)
                throw new ArgumentException("Message buffer is empty", nameof(message));

            var deserialized = MessageSerializer.Instance.Deserialize<Message>(message.ToArray());
            if (deserialized == null)
                throw new InvalidOperationException("Failed to deserialize message");

            return deserialized;
        }
    }
}