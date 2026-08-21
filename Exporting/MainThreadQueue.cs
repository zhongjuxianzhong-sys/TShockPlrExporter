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
    private readonly ConcurrentQueue<Action> pending = new();
    private volatile bool accepting = true;

    /// <summary>排入一个工作项，不等待其完成。</summary>
    public void Enqueue(Action action)
    {
        if (!accepting)
        {
            return;
        }

        pending.Enqueue(action);
    }

    /// <summary>由主线程的 GameUpdate 钩子调用，执行当前排队的所有工作项。</summary>
    public void Drain()
    {
        while (pending.TryDequeue(out Action? action))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                TShock.Log.Error($"[TShockPlrExporter] 主线程工作项执行失败：{ex}");
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

        pending.Enqueue(() =>
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
        });

        try
        {
            if (!completion.Task.Wait(timeout))
            {
                throw new TimeoutException("等待主线程执行超时，服务器可能正在关闭或严重卡顿。");
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
