using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Communication.Shared.Messages;
using System.IO;
using MessageProtocol.Serialize;

namespace Communication.Shared.Messages.Converter
{
    public class MessageConverter
    {
        public static MessageConverter Instance { get; private set; } = new();
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