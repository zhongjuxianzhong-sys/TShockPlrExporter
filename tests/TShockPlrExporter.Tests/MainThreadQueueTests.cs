using TShockPlrExporter.Exporting;
using Xunit;

namespace TShockPlrExporter.Tests;

public class MainThreadQueueTests
{
    [Fact]
    public async Task Invoke_RunsActionWhenDrained()
    {
        MainThreadQueue queue = new();
        TaskCompletionSource actionRan = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Task drainer = Task.Run(
            () =>
            {
                while (!actionRan.Task.IsCompleted)
                {
                    queue.Drain();
                    Thread.Yield();
                }
            });

        await Task.Run(
            () => queue.Invoke(
                () => actionRan.TrySetResult(),
                TimeSpan.FromSeconds(5)));

        await drainer;
        Assert.True(actionRan.Task.IsCompleted);
    }

    [Fact]
    public void InvokeTimeout_CancelsPendingAction()
    {
        MainThreadQueue queue = new();
        int executed = 0;

        Assert.Throws<TimeoutException>(() => queue.Invoke(() => executed++, TimeSpan.FromMilliseconds(50)));

        queue.Drain();
        Assert.Equal(0, executed);
    }
}
