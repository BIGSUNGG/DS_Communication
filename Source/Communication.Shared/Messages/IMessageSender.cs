using Communication.Shared.Messages;

namespace Communication.Shared.Messages
{
    public interface IMessageSender
    {
        Task SendAsync(object message);
    }
}