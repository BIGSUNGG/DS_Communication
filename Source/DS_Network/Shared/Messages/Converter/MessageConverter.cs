using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DS.Network.Shared.Messages;
using MemoryPack;
using System.IO;

namespace DS.Network.Shared.Messages.Converter
{
    public class MessageConverter
    {
        public static MessageConverter Instance { get; private set; } = new();
        public byte[] Serialize(Message message)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            return MemoryPackSerializer.Serialize(message);
        }

        public Message Deserialize(ReadOnlySpan<byte> message)
        {
            if (message.Length == 0)
                throw new ArgumentException("Message buffer is empty", nameof(message));

            var deserialized = MemoryPackSerializer.Deserialize<Message>(message);
            if (deserialized == null)
                throw new InvalidOperationException("Failed to deserialize message");

            return deserialized;
        }
    }
}