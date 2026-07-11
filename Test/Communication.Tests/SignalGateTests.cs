using Communication.Shared.Threading;
using Xunit;

namespace Communication.Tests;

public class SignalGateTests
{
    [Fact]
    public void Signal_MultipleTimes_DoesNotThrowSemaphoreFullException()
    {
        using var gate = new SignalGate();

        var ex = Record.Exception(() =>
        {
            for (int i = 0; i < 100; i++)
            {
                gate.Signal();
            }
        });

        Assert.Null(ex);
    }

    [Fact]
    public async Task WaitAsync_Unblocks_AfterSignal()
    {
        using var gate = new SignalGate();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var waitTask = gate.WaitAsync(cts.Token);
        Assert.False(waitTask.IsCompleted);

        gate.Signal();

        await waitTask;
        Assert.True(waitTask.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task WaitAsync_Unblocks_AfterMultipleSignals()
    {
        using var gate = new SignalGate();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        gate.Signal();
        gate.Signal();
        gate.Signal();

        await gate.WaitAsync(cts.Token);
    }
}
