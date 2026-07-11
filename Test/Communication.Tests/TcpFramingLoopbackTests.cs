using System.Net;
using System.Net.Sockets;
using System.Text;
using Communication.Network.TCP.Client;
using Communication.Network.TCP.Server;
using Communication.Shared.Messages;
using Communication.TCP.Shared.Messages;
using Xunit;

namespace Communication.Tests;

public class TcpFramingLoopbackTests
{
    [Fact]
    public async Task SendAndFlushAsync_RoundtripsUtf8String()
    {
        var converter = new Utf8StringConverter();
        var received = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listenCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        int port = GetFreeTcpPort();
        var listener = new TCPListener(IPAddress.Loopback, port);
        listener.Start();

        var listenTask = listener.ListenAsync(async client =>
        {
            try
            {
                using (client)
                {
                    var stream = client.GetStream();
                    var handler = new CaptureHandler(received);
                    using var receiver = new TCPMessageReceiver(converter, stream, handler);
                    await received.Task.WaitAsync(listenCts.Token);
                }
            }
            catch
            {
            }
        }, listenCts.Token);

        var connector = new TCPConnector("127.0.0.1", port);
        bool connected = await connector.ConnectAsync(async client =>
        {
            using (client)
            {
                var stream = client.GetStream();
                using var sender = new TCPMessageSender(converter, stream);
                await sender.SendAndFlushAsync("ping");
                await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }, listenCts.Token);

        Assert.True(connected);
        Assert.Equal("ping", await received.Task.WaitAsync(TimeSpan.FromSeconds(5)));

        listenCts.Cancel();
        listener.Stop();
        try
        {
            await listenTask.WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (OperationCanceledException)
        {
        }
        catch (TimeoutException)
        {
        }
        catch (SocketException)
        {
        }
        catch (AggregateException ex) when (ex.InnerException is SocketException or ObjectDisposedException or OperationCanceledException)
        {
        }
    }

    private static int GetFreeTcpPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private sealed class Utf8StringConverter : IMessageConverter
    {
        public byte[] Serialize(object message)
        {
            return Encoding.UTF8.GetBytes((string)message);
        }

        public object Deserialize(ReadOnlySpan<byte> message)
        {
            return Encoding.UTF8.GetString(message);
        }
    }

    private sealed class CaptureHandler : IMessageHandler
    {
        private readonly TaskCompletionSource<object> _tcs;

        public CaptureHandler(TaskCompletionSource<object> tcs) => _tcs = tcs;

        public void HandleMessage(object message) => _tcs.TrySetResult(message);

        public void OnDetectedDisconnection()
        {
        }
    }
}
