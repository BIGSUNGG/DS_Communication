namespace Communication.Shared.Messages
{
    public interface IMessageConverter
    {
        byte[] Serialize(object message);
        object Deserialize(ReadOnlySpan<byte> message);
    }
}
