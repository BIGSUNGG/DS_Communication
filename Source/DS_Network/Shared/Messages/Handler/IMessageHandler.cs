namespace DS.Network.Shared.Messages.Handler
{
    public interface IMessageHandler
    {
        void HandleMessage(Message message);

        void OnDetectedDisconnection();
    }
}