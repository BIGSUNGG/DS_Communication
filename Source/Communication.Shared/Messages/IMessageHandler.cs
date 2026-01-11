namespace Communication.Shared.Messages
{
    public interface IMessageHandler
    {
        void HandleMessage(object message);

        void OnDetectedDisconnection();
    }
}