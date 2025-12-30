namespace DS.Communication.Shared.Messages.Handler
{
    public interface IMessageHandler
    {
        void HandleMessage(Message message);

        void OnDetectedDisconnection();
    }
}