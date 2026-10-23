using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Threading;

namespace TabDock.UnitTests;

/// <summary>
/// No-input STA dispatcher/clock fixture. Allocation helpers are no-inline so
/// a Debug test method cannot keep the created instance alive across a
/// collectability assertion.
/// </summary>
internal static class StaDispatcherFixture
{
    public static void Run(Action<Dispatcher> action, TimeSpan? timeout = null)
    {
        Exception? failure = null;
        using var done = new ManualResetEventSlim(false);
        var thread = new Thread(() =>
        {
            try
            {
                Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
                dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        action(dispatcher);
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                    }
                    finally
                    {
                        dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
                    }
                });
                Dispatcher.Run();
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }
            finally
            {
                done.Set();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Name = "TabDock.StaDispatcherFixture";
        thread.Start();
        if (!done.Wait(timeout ?? TimeSpan.FromSeconds(60)))
            throw new TimeoutException("STA dispatcher fixture timed out.");
        if (failure is not null)
            throw failure;
    }

    public static void Settle(Dispatcher dispatcher)
    {
        var frame = new DispatcherFrame();
        _ = dispatcher.BeginInvoke(DispatcherPriority.SystemIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }

    public static void Pump(Dispatcher dispatcher, TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = duration,
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    public static void CollectSettled(Dispatcher dispatcher)
    {
        for (int pass = 0; pass < 4; pass++)
        {
            Settle(dispatcher);
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }
        Settle(dispatcher);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static WeakReference Allocate(Func<object> factory)
    {
        object instance = factory();
        return new WeakReference(instance);
    }

    public static int Alive(params WeakReference[] references)
    {
        int alive = 0;
        for (int i = 0; i < references.Length; i++)
        {
            if (references[i].IsAlive)
                alive++;
        }
        return alive;
    }
}
