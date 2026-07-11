using Communication.Shared.Messages;

namespace Communication.Shared.Sessions
{
    public interface ISession
    {
        Task SendAsync(object message, object context);
        Task SendAsync(object message);
        Task SendAndFlushAsync(object message, object? context = null, CancellationToken cancellationToken = default);

        void Disconnect();
        bool IsConnected();
    }
}
