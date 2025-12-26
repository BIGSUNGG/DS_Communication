using DS.Network.Shared.Messages;

namespace DS.Network.Shared.Messages.Sender
{
    public interface IMessageSender
    {
        Task SendAsync(Message message);
    }
}