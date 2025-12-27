using DS.Communication.Shared.Messages;

namespace DS.Communication.Shared.Messages.Sender
{
    public interface IMessageSender
    {
        Task SendAsync(Message message);
    }
}