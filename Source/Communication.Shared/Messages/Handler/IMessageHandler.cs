namespace Communication.Shared.Messages.Handler
{
    public interface IMessageHandler
    {
        void HandleMessage(object message);

        void OnDetectedDisconnection();
    }
}