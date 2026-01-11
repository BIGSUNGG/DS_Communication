using Communication.Shared.Messages;

namespace Communication.Shared.Messages.Sender
{
    public interface IMessageSender
    {
        Task SendAsync(object message);
    }
}