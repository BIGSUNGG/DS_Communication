using Communication.Shared.Messages;

namespace Communication.Shared.Messages
{
    public interface IMessageSender
    {
        Task SendAsync(object message, object context);
        Task SendAsync(object message);
    }
}