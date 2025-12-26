using Common.Messages;

namespace Common.Messages.Sender
{
    public interface IMessageSender
    {
        Task SendAsync(Message message);
    }
}