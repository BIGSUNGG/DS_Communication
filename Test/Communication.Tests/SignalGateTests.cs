using System.Threading;
using System.Threading.Tasks;
using Communication.Shared.Threading;
using Xunit;

namespace Communication.Tests;

/// <summary>
/// <see cref="SignalGate"/> 계약 핀 — 연속 Signal 단일 퍼밋 붕괴, 리셋·재신호 조건, Dispose 해제.
/// 송신·디스패치 루프의 웨이크업 기반이라 간접 커버만 있던 공개 타입의 직접 계약을 고정한다.
/// </summary>
public class SignalGateTests
{
    [Fact]
    public async Task Signal_WakesWaiter()
    {
        using SignalGate gate = new();
        Task wait = gate.WaitAsync(CancellationToken.None);
        Assert.False(wait.IsCompleted); // 대기 중

        gate.Signal();
        await wait.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Signal_BeforeWait_PermitLatches()
    {
        using SignalGate gate = new();
        gate.Signal(); // 대기자 없이 신호 선행

        // 세마포어(0,1) 퍼밋은 래치된다 — 이후 대기는 즉시 통과.
        await gate.WaitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RepeatedSignal_CollapsesToSinglePermit()
    {
        using SignalGate gate = new();

        Task first = gate.WaitAsync(CancellationToken.None);
        gate.Signal();
        gate.Signal();
        gate.Signal(); // 연속 신호 — 퍼밋은 1개로 붕괴

        await first.WaitAsync(TimeSpan.FromSeconds(5));

        Task second = gate.WaitAsync(CancellationToken.None);
        Assert.NotEqual(
            second,
            await Task.WhenAny(second, Task.Delay(200))); // 두 번째 대기자는 풀리지 않는다(무손실 증폭 없음)
    }

    [Fact]
    public async Task ResetAndResignalIf_TrueCondition_SignalsAgain()
    {
        using SignalGate gate = new();

        Task first = gate.WaitAsync(CancellationToken.None);
        gate.Signal();
        await first.WaitAsync(TimeSpan.FromSeconds(5));

        // 조건 참 — pending을 지우고 다시 신호: 다음 대기자가 깬다.
        gate.ResetPendingAndResignalIf(() => true);
        await gate.WaitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ResetAndResignalIf_FalseCondition_DoesNotSignal()
    {
        using SignalGate gate = new();

        Task first = gate.WaitAsync(CancellationToken.None);
        gate.Signal();
        await first.WaitAsync(TimeSpan.FromSeconds(5));

        gate.ResetPendingAndResignalIf(() => false);
        Task next = gate.WaitAsync(CancellationToken.None);
        Assert.NotEqual(
            next,
            await Task.WhenAny(next, Task.Delay(200))); // 허위 웨이크업 없음
    }

    [Fact]
    public async Task Dispose_CompletesPendingWaiter_ThenSubsequentWaitsThrow()
    {
        SignalGate gate = new();
        Task pending = gate.WaitAsync(CancellationToken.None);

        gate.Dispose();

        // 대기 중이던 호출자는 해제(루프는 탈출), 이후 대기는 즉시 예외 — 루프의 OCE/ODE 캐치 경로 전제.
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => gate.WaitAsync(CancellationToken.None));
    }
}
