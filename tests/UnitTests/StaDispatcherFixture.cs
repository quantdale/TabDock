using System;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using System.Reflection;
using System.Windows;
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
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher? activeDispatcher = null;
        var thread = new Thread(() =>
        {
            try
            {
                Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
                activeDispatcher = dispatcher;
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
                done.TrySetResult(true);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Name = "TabDock.StaDispatcherFixture";
        thread.Start();
        if (!done.Task.Wait(timeout ?? TimeSpan.FromSeconds(60)))
        {
            activeDispatcher?.BeginInvokeShutdown(DispatcherPriority.Send);
            // Test actions must be bounded. Give an in-flight managed turn a
            // chance to release its resources; never dispose a signal it uses.
            bool joined = thread.Join(TimeSpan.FromSeconds(5));
            throw new TimeoutException(joined ? "STA dispatcher fixture timed out and stopped."
                : "STA fixture action did not stop within its shutdown budget.");
        }
        thread.Join();
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [ThreadStatic]
    private static WeakReference<UIElement>? _managedFocus;

    /// <summary>Inject managed focus properties, never native keyboard focus.</summary>
    public static bool Focus(UIElement element)
    {
        if (_managedFocus != null && _managedFocus.TryGetTarget(out UIElement? previous))
        {
            SetReadOnly(previous, "IsKeyboardFocusWithinPropertyKey", false);
            SetReadOnly(previous, "IsKeyboardFocusedPropertyKey", false);
        }
        SetReadOnly(element, "IsKeyboardFocusWithinPropertyKey", true);
        SetReadOnly(element, "IsKeyboardFocusedPropertyKey", true);
        _managedFocus = new WeakReference<UIElement>(element);
        return element.IsKeyboardFocusWithin;
    }

    private static void SetReadOnly(UIElement element, string name, bool value)
    {
        FieldInfo field = typeof(UIElement).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("WPF focus test key missing: " + name);
        if (name == "IsKeyboardFocusWithinPropertyKey")
        {
            MethodInfo writeFlag = typeof(UIElement).GetMethod("WriteFlag", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("WPF focus cache test seam missing");
            Type flags = writeFlag.GetParameters()[0].ParameterType;
            writeFlag.Invoke(element, new[] { Enum.Parse(flags, "IsKeyboardFocusWithinCache"), (object)value });
        }
        element.SetValue((DependencyPropertyKey)field.GetValue(null)!, value);
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
