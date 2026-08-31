using Communication.Shared.Messages;
using Communication.Shared.Sessions;
using Xunit;

namespace Communication.Tests;

public class MessageHandlerTests
{
    [Fact]
    public void HandleMessage_UnregisteredType_SkipsWithoutThrowing()
    {
        using var session = new UnattachedTestSession(new FakeByteChannel());
        var handler = new TestableMessageHandler(session);

        handler.HandleMessage("unregistered"); // 미등록 — Trace 후 skip, 예외 없음

        Assert.Empty(handler.Handled);
    }

    [Fact]
    public void Register_AfterDispatch_TakesEffect()
    {
        using var session = new UnattachedTestSession(new FakeByteChannel());
        var handler = new TestableMessageHandler(session);
        handler.RegisterRecorded<string>();

        handler.HandleMessage("one");
        handler.HandleMessage(42); // int 미등록 상태 — skip
        handler.RegisterRecorded<int>(); // 지연 등록
        handler.HandleMessage(7);

        Assert.Equal(new object[] { "one", 7 }, handler.Handled);
    }

    [Fact]
    public void ConcurrentRegisterAndDispatch_DistinctTypes_NoLossNoThrow()
    {
        using var session = new UnattachedTestSession(new FakeByteChannel());
        var handler = new TestableMessageHandler(session);

        Type[] carriers =
        {
            typeof(bool), typeof(byte), typeof(sbyte), typeof(short), typeof(ushort),
            typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float),
            typeof(double), typeof(decimal), typeof(char), typeof(string), typeof(object), typeof(Guid),
        };

        // 지연 등록과 디스패치가 동시 진행 — 예외 없어야 하고 등록이 유실되면 안 된다.
        Parallel.For(0, carriers.Length, i =>
        {
            Type type = typeof(Envelope<>).MakeGenericType(carriers[i]);
            handler.RegisterRecorded(type);
            handler.HandleMessage(CreateEnvelope(type));
        });

        // 합류 후 전 타입 재디스패치 — 경쟁으로 등록이 사라졌다면 카운트가 부족하다.
        foreach (Type carrier in carriers)
        {
            handler.HandleMessage(CreateEnvelope(typeof(Envelope<>).MakeGenericType(carrier)));
        }

        Assert.Equal(carriers.Length * 2, handler.Handled.Count);
    }

    private static object CreateEnvelope(Type constructedType) => Activator.CreateInstance(constructedType)!;

    /// <summary>타입별 디스패치 검증용 캐리어.</summary>
    private sealed class Envelope<T>
    {
    }

    /// <summary>등록을 공개하는 테스트용 핸들러.</summary>
    private sealed class TestableMessageHandler : MessageHandler
    {
        private readonly object _lock = new();
        private readonly List<object> _handled = new();

        public TestableMessageHandler(ISession session)
            : base(session)
        {
        }

        public IReadOnlyList<object> Handled
        {
            get
            {
                lock (_lock)
                {
                    return _handled.ToList();
                }
            }
        }

        public void RegisterRecorded<T>() => Register<T>(message => Record(message!));

        public void RegisterRecorded(Type messageType) => RegisterMessageType(messageType, Record);

        private void Record(object message)
        {
            lock (_lock)
            {
                _handled.Add(message);
            }
        }
    }
}
