using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using TShockAPI;

namespace TShockPlrExporter.Exporting;

/// <summary>
/// 把工作项排队到 Terraria 主线程执行。
/// 导出流程整体运行在后台线程，但以下三件事必须回到主线程：
/// 发送聊天消息、写入在线玩家的 SSC 数据、以及回退保存路径里对
/// <see cref="Terraria.Main.ServerSideCharacter"/> 的临时改写。
/// </summary>
internal sealed class MainThreadQueue
{
    private sealed record PendingAction(Action Action, CancellationToken CancellationToken);

    private readonly ConcurrentQueue<PendingAction> pending = new();
    private volatile bool accepting = true;

    /// <summary>最后一次 <see cref="Drain"/> 的时刻（UTC ticks），用来判断主线程是否还在抽取队列。</summary>
    private long lastDrainTicks = DateTime.UtcNow.Ticks;

    /// <summary>
    /// 距离上一次主线程抽取队列过了多久。
    /// GameUpdate 钩子每帧都会调用 <see cref="Drain"/>，所以正常情况下这个值只有毫秒级；
    /// 一旦明显偏大，说明钩子没有运行，任何排进来的工作项都不会被执行。
    /// </summary>
    public TimeSpan SinceLastDrain =>
        new(Math.Max(0, DateTime.UtcNow.Ticks - Interlocked.Read(ref lastDrainTicks)));

    /// <summary>排入一个工作项，不等待其完成。</summary>
    public void Enqueue(Action action)
    {
        if (!accepting)
        {
            return;
        }

        pending.Enqueue(new PendingAction(action, CancellationToken.None));
    }

    /// <summary>由主线程的 GameUpdate 钩子调用，执行当前排队的所有工作项。</summary>
    public void Drain()
    {
        // 心跳要无条件更新：队列为空同样说明主线程仍在正常抽取。
        Interlocked.Exchange(ref lastDrainTicks, DateTime.UtcNow.Ticks);

        while (pending.TryDequeue(out PendingAction? pendingAction))
        {
            if (pendingAction.CancellationToken.IsCancellationRequested)
            {
                continue;
            }

            try
            {
                pendingAction.Action();
            }
            catch (Exception ex)
            {
                // 这里只能走日志：工作项本身往往就是「给玩家发消息」，再往回发一次没有意义。
                try
                {
                    TShock.Log.ConsoleError($"[TShockPlrExporter] 主线程工作项执行失败：{ex}");
                }
                catch
                {
                    Console.WriteLine($"[TShockPlrExporter] 主线程工作项执行失败：{ex}");
                }
            }
        }
    }

    /// <summary>
    /// 在主线程执行 <paramref name="action"/> 并阻塞等待其完成，异常会原样抛回调用方。
    /// 只能从后台线程调用；从主线程调用会自死锁。
    /// </summary>
    public void Invoke(Action action, TimeSpan timeout)
    {
        if (!accepting)
        {
            throw new InvalidOperationException("插件正在卸载，主线程队列已关闭。");
        }

        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        using CancellationTokenSource timeoutToken = new();

        pending.Enqueue(new PendingAction(
            () =>
            {
                try
                {
                    action();
                    completion.TrySetResult();
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            },
            timeoutToken.Token));

        try
        {
            if (!completion.Task.Wait(timeout))
            {
                timeoutToken.Cancel();
                throw new TimeoutException(
                    "等待主线程执行超时。主线程队列已有 " +
                    $"{SinceLastDrain.TotalSeconds:F1} 秒没有被抽取，服务器可能正在关闭、严重卡顿，" +
                    "或者 GameUpdate 钩子没有运行。");
            }
        }
        catch (AggregateException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        }
    }

    /// <summary>停止接受新工作项并丢弃尚未执行的项，供插件卸载时调用。</summary>
    public void Shutdown()
    {
        accepting = false;

        while (pending.TryDequeue(out _))
        {
        }
    }
}
