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

    // ---- 상속 기반 폴백 검증용 타입 계층 ----

    private abstract class AnimalBase
    {
    }

    private class Mammal : AnimalBase
    {
    }

    private sealed class Cat : Mammal
    {
    }

    /// <summary>등록한 핸들러에 이름표를 달아 어느 핸들러가 돌았는지 관찰한다.</summary>
    private sealed class MarkingHandler : MessageHandler
    {
        private readonly List<string> _marks = new();

        public MarkingHandler(ISession session)
            : base(session)
        {
        }

        public IReadOnlyList<string> Marks
        {
            get
            {
                lock (_marks)
                {
                    return _marks.ToList();
                }
            }
        }

        public void RegisterMarked<T>(string mark) => Register<T>(_ =>
        {
            lock (_marks)
            {
                _marks.Add(mark);
            }
        });
    }

    [Fact]
    public void DerivedMessage_FallsBackToRegisteredBase()
    {
        using var session = new UnattachedTestSession(new FakeByteChannel());
        var handler = new MarkingHandler(session);
        handler.RegisterMarked<AnimalBase>("animal");

        handler.HandleMessage(new Cat()); // 정확 타입 미등록 — 등록된 베이스로 분배.

        Assert.Equal(new[] { "animal" }, handler.Marks);
    }

    [Fact]
    public void MostDerivedRegisteredBase_Wins()
    {
        using var session = new UnattachedTestSession(new FakeByteChannel());
        var handler = new MarkingHandler(session);
        handler.RegisterMarked<AnimalBase>("animal");
        handler.RegisterMarked<Mammal>("mammal");

        handler.HandleMessage(new Cat()); // 둘 다 대입 가능 — 더 구체적인 Mammal이 분배되어야 한다.

        Assert.Equal(new[] { "mammal" }, handler.Marks);
    }

    [Fact]
    public void ExactRegisteredType_BeatsBaseFallback()
    {
        using var session = new UnattachedTestSession(new FakeByteChannel());
        var handler = new MarkingHandler(session);
        handler.RegisterMarked<Cat>("cat");
        handler.RegisterMarked<AnimalBase>("animal");

        handler.HandleMessage(new Cat()); // 정확 타입 등록이 폴백보다 우선.

        Assert.Equal(new[] { "cat" }, handler.Marks);
    }

    [Fact]
    public void UnrelatedType_StillSkips_WhenOnlyBasesRegistered()
    {
        using var session = new UnattachedTestSession(new FakeByteChannel());
        var handler = new MarkingHandler(session);
        handler.RegisterMarked<AnimalBase>("animal");

        handler.HandleMessage(42); // 상속 관계 없음 — 폴백 없이 skip, 예외 없음.

        Assert.Empty(handler.Marks);
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
