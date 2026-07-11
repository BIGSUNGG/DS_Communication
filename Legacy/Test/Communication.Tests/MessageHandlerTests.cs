using Communication.Shared.Messages;
using Communication.Shared.Sessions;
using Xunit;

namespace Communication.Tests;

public class MessageHandlerTests
{
    [Fact]
    public void HandleMessage_UnknownType_DoesNotThrow()
    {
        using var session = new TestSession();
        using var handler = new EmptyMessageHandler(session, new MessageQueueOptions { InlineDispatch = true });

        var ex = Record.Exception(() => handler.HandleMessage(new UnknownPayload("hello")));

        Assert.Null(ex);
    }

    [Fact]
    public void HandleMessage_RegisteredType_InvokesAction()
    {
        using var session = new TestSession();
        using var handler = new RecordingMessageHandler(session, new MessageQueueOptions { InlineDispatch = true });

        handler.HandleMessage(new KnownPayload(42));

        Assert.Equal(42, handler.LastValue);
    }

    private sealed class UnknownPayload
    {
        public UnknownPayload(string text) => Text = text;
        public string Text { get; }
    }

    private sealed class KnownPayload
    {
        public KnownPayload(int value) => Value = value;
        public int Value { get; }
    }

    private sealed class EmptyMessageHandler : MessageHandler
    {
        public EmptyMessageHandler(ISession session, MessageQueueOptions? options = null)
            : base(session, options)
        {
        }

        protected override void RegisterMessageType()
        {
        }
    }

    private sealed class RecordingMessageHandler : MessageHandler
    {
        public int? LastValue { get; private set; }

        public RecordingMessageHandler(ISession session, MessageQueueOptions? options = null)
            : base(session, options)
        {
        }

        protected override void RegisterMessageType()
        {
            _messageHandleActions[typeof(KnownPayload)] = msg =>
            {
                LastValue = ((KnownPayload)msg).Value;
            };
        }
    }

    private sealed class TestSession : Session
    {
        public TestSession()
            : base(_ => new NoOpReceiver(), _ => new CollectingSender(new NoOpConverter()))
        {
        }

        protected override bool IsTransportConnected() => true;

        protected override void OnDisconnected()
        {
        }
    }

    private sealed class NoOpReceiver : IMessageReceiver
    {
    }

    private sealed class CollectingSender : MessageSender
    {
        public List<object> Sent { get; } = new();

        public CollectingSender(IMessageConverter converter)
            : base(converter)
        {
        }

        public override Task SendAsync(object message)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }

        public override Task SendAsync(object message, object context)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }

        public override Task SendAndFlushAsync(object message, object? context = null, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpConverter : IMessageConverter
    {
        public byte[] Serialize(object message) => Array.Empty<byte>();

        public object Deserialize(ReadOnlySpan<byte> message) => new object();
    }
}
