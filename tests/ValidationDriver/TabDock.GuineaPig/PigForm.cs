using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TabDock.GuineaPig;

/// <summary>Native MINMAXINFO lParam layout for WM_GETMINMAXINFO (self-contained — the pig has no reference to TabDock.NativeMethods).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct MinMaxInfo
{
    public PointNative ptReserved;
    public PointNative ptMaxSize;
    public PointNative ptMaxPosition;
    public PointNative ptMinTrackSize;
    public PointNative ptMaxTrackSize;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PointNative
{
    public int x;
    public int y;
}

/// <summary>Deliberate DPI-awareness modes the pig can launch under, so the
/// harness can exercise TabDock against guests in every awareness class
/// (see the DPI-acceptance goal). "Default" keeps the pig's natural
/// (WinForms/no-manifest) awareness — typically DPI_UNAWARE.</summary>
public enum DpiMenuMode
{
    Default,
    Unaware,
    SystemAware,
    PerMonitorAware,
    PerMonitorAwareV2,
}

/// <summary>Self-contained DPI P/Invokes + awareness-context handles. These match
/// the well-known values: unaw/node DPI context = -1, system = -2, per-monitor =
/// -3, per-monitor-v2 = -4. No references to TabDock's own P/Invoke surface.</summary>
internal static class PigDpi
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct ClientRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;

        internal int Width => Right - Left;
        internal int Height => Bottom - Top;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetClientRect(IntPtr hwnd, out ClientRect rect);

    [DllImport("user32.dll")]
    internal static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetWindowDpiAwarenessContext(IntPtr hwnd);

    [DllImport("user32.dll")]
    internal static extern int GetAwarenessFromDpiAwarenessContext(IntPtr value);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(IntPtr hwnd);

    internal static readonly IntPtr ContextUnaware = new IntPtr(-1);
    internal static readonly IntPtr ContextSystemAware = new IntPtr(-2);
    internal static readonly IntPtr ContextPerMonitorAware = new IntPtr(-3);
    internal static readonly IntPtr ContextPerMonitorAwareV2 = new IntPtr(-4);

    /// <summary>
    /// Applies the requested thread DPI awareness context for form creation and
    /// returns the previous context (to restore), or IntPtr.Zero when no change
    /// was requested. The window inherits the thread context that is current
    /// when it is created (mixed-mode DPI scaling), which is exactly how the
    /// harness forces a guest into a given awareness class.
    /// </summary>
    internal static IntPtr ApplyThreadDpi(DpiMenuMode mode)
    {
        IntPtr context = mode switch
        {
            DpiMenuMode.Unaware => ContextUnaware,
            DpiMenuMode.SystemAware => ContextSystemAware,
            DpiMenuMode.PerMonitorAware => ContextPerMonitorAware,
            DpiMenuMode.PerMonitorAwareV2 => ContextPerMonitorAwareV2,
            _ => IntPtr.Zero,
        };
        if (context == IntPtr.Zero)
            return IntPtr.Zero;
        return SetThreadDpiAwarenessContext(context);
    }

    internal static IntPtr RestoreThreadDpi(IntPtr previous)
    {
        if (previous == IntPtr.Zero)
            return previous;
        return SetThreadDpiAwarenessContext(previous);
    }

    internal static string DescribeDpi(IntPtr hwnd)
    {
        uint dpi = GetDpiForWindow(hwnd);
        IntPtr ctx = GetWindowDpiAwarenessContext(hwnd);
        string awareness = ctx == IntPtr.Zero ? "unknown" : GetAwarenessFromDpiAwarenessContext(ctx).ToString();
        return $"dpi={dpi} awareness={awareness}";
    }

    internal static (int Width, int Height) GetNativeClientSize(IntPtr hwnd, Size fallback)
    {
        return GetClientRect(hwnd, out ClientRect rect) && rect.Width >= 0 && rect.Height >= 0
            ? (rect.Width, rect.Height)
            : (fallback.Width, fallback.Height);
    }
}

/// <summary>Parsed command-line options for the guinea-pig window.</summary>
public sealed class PigOptions
{
    public string Title = string.Empty;
    public string RunId = string.Empty;
    public string? Color;
    public bool Pulse;
    public bool HideOnClose;
    public bool MinimizeThenHideOnClose;
    public int SelfCloseAfterSeconds = -1;
    public int SelfMinimizeAfterSeconds = -1;
    public bool CloseButton;
    public bool ClickCounterButton;
    public bool TextBox;
    // Emit post-WndProc client dimensions for deterministic presentation/render
    // qualification. This is opt-in so ordinary lifecycle scenarios keep their
    // original stable titles and log volume.
    public bool ResizeProbe;
    // Native minimum track size (physical pixels) enforced via WM_GETMINMAXINFO,
    // so the harness can reproduce the browser/explorer "refuses to shrink"
    // containment defect deterministically.
    public int MinWidth;
    public int MinHeight;
    // Deliberately blocks the UI thread after the form is shown so a
    // SendMessageTimeout(WM_GETMINMAXINFO) probe can be qualified against a
    // non-pumping guest without making the process permanently unkillable.
    public int BlockMessagesMilliseconds;
    // Deliberate DPI-awareness mode this pig should launch under (see
    // DpiMenuMode). Default = the pig's natural WinForms/no-manifest awareness.
    public DpiMenuMode DpiMode = DpiMenuMode.Default;
}

/// <summary>
/// A plain solid-color form that logs every WM_CLOSE / WM_DESTROY / WM_SHOWWINDOW /
/// WM_SYSCOMMAND / WM_SIZE / WM_NCCALCSIZE it receives (plus form lifecycle events)
/// to %TEMP%\TabDock-Validation\pig-&lt;pid&gt;.log so the validation driver can assert
/// on exactly what the window experienced while captured inside TabDock.
/// </summary>
public sealed class PigForm : Form
{
    private const int MsgClose = 0x0010;
    private const int MsgDestroy = 0x0002;
    private const int MsgShowWindow = 0x0018;
    private const int MsgSysCommand = 0x0112;
    private const int MsgSize = 0x0005;
    private const int MsgNcCalcSize = 0x0083;
    private const int MsgLButtonDown = 0x0201;
    private const int MsgLButtonUp = 0x0202;
    private const int MsgSetFocus = 0x0007;
    private const int MsgKillFocus = 0x0008;
    private const int MsgMouseActivate = 0x0021;
    private const int MsgGetMinMaxInfo = 0x0024;
    private const int MsgEnterSizeMove = 0x0231;
    private const int MsgExitSizeMove = 0x0232;
    private const int MsgWindowPosChanged = 0x0047;

    private readonly PigOptions _opts;
    private readonly string? _logPath;
    private readonly object _logLock = new object();
    private readonly Color _baseColor;
    private readonly Color _pulseColor;
    private bool _pulseOn;
    private TextBox? _textBox;
    private int _resizeProbeCount;
    private int _nativeMoveCount;

    public PigForm(PigOptions opts)
    {
        _opts = opts;

        string dir = Path.Combine(Path.GetTempPath(), "TabDock-Validation");
        try
        {
            Directory.CreateDirectory(dir);
            _logPath = Path.Combine(dir, $"pig-{Environment.ProcessId}.log");
        }
        catch (Exception ex)
        {
            Debug.WriteLine("PigForm: could not create log directory: " + ex.Message);
            _logPath = null;
        }

        Text = opts.Title;
        ClientSize = new Size(500, 400);
        _baseColor = ParseColor(opts.Color);
        _pulseColor = ShiftBrightness(_baseColor, 25);
        BackColor = _baseColor;

        if (opts.Pulse)
        {
            var pulseTimer = new Timer { Interval = 500 };
            pulseTimer.Tick += (s, e) =>
            {
                _pulseOn = !_pulseOn;
                BackColor = _pulseOn ? _pulseColor : _baseColor;
            };
            pulseTimer.Start();
        }

        if (opts.BlockMessagesMilliseconds > 0)
        {
            Shown += (_, _) =>
            {
                Log($"BLOCK_MESSAGES start={opts.BlockMessagesMilliseconds}ms");
                System.Threading.Thread.Sleep(opts.BlockMessagesMilliseconds);
                Log("BLOCK_MESSAGES end");
            };
        }

        if (opts.SelfCloseAfterSeconds > 0)
        {
            var t = new Timer { Interval = opts.SelfCloseAfterSeconds * 1000 };
            t.Tick += (s, e) =>
            {
                t.Stop();
                Log("LIFECYCLE SelfCloseTimer -> Close()");
                Close();
            };
            t.Start();
        }

        if (opts.SelfMinimizeAfterSeconds > 0)
        {
            var t = new Timer { Interval = opts.SelfMinimizeAfterSeconds * 1000 };
            t.Tick += (s, e) =>
            {
                t.Stop();
                Log("LIFECYCLE SelfMinimizeTimer -> WindowState=Minimized");
                WindowState = FormWindowState.Minimized;
            };
            t.Start();
        }

        if (opts.CloseButton)
        {
            var btn = new Button
            {
                Text = "X-CLOSE",
                Size = new Size(90, 30),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
            };
            btn.Location = new Point(ClientSize.Width - btn.Width - 8, 8);
            btn.Click += (s, e) =>
            {
                Log("LIFECYCLE X-CLOSE button clicked -> Close()");
                Close();
            };
            Controls.Add(btn);
        }

        if (opts.ClickCounterButton)
        {
            var btn = new Button
            {
                Name = "ClickCounterButton",
                Text = "Click me: 0",
                Size = new Size(140, 40),
                Anchor = AnchorStyles.None,
            };
            btn.Location = new Point(
                (ClientSize.Width - btn.Width) / 2,
                (ClientSize.Height - btn.Height) / 2);
            int clickCount = 0;
            btn.Click += (s, e) =>
            {
                clickCount++;
                btn.Text = $"Click me: {clickCount}";
                Log($"BUTTON_CLICK count={clickCount}");
            };
            Controls.Add(btn);
        }

        if (opts.TextBox)
        {
            _textBox = new TextBox
            {
                Name = "TypedTextBox",
                Text = string.Empty,
                Dock = DockStyle.Fill,
            };
            _textBox.TextChanged += (s, e) =>
            {
                Log($"TEXTBOX text='{_textBox.Text.Replace("'", "''")}'");
            };
            Controls.Add(_textBox);
            ActiveControl = _textBox;
        }

        Shown += (s, e) => Log($"LIFECYCLE Shown {PigDpi.DescribeDpi(Handle)}");
        FormClosing += OnPigFormClosing;
        FormClosed += (s, e) => Log("LIFECYCLE FormClosed");

        Log($"LIFECYCLE Created title='{opts.Title}' pid={Environment.ProcessId} runId={opts.RunId} color={_baseColor} " +
            $"pulse={opts.Pulse} hideOnClose={opts.HideOnClose} minThenHide={opts.MinimizeThenHideOnClose} " +
            $"selfClose={opts.SelfCloseAfterSeconds} selfMin={opts.SelfMinimizeAfterSeconds} closeButton={opts.CloseButton} textBox={opts.TextBox} " +
            $"resizeProbe={opts.ResizeProbe} " +
            $"dpiMode={opts.DpiMode} minTrack={_opts.MinWidth}x{_opts.MinHeight}");
    }

    private void OnPigFormClosing(object? sender, FormClosingEventArgs e)
    {
        bool canceled = false;
        if (_opts.MinimizeThenHideOnClose)
        {
            // Simulates the PredatorSense pattern: cancel the close, minimize, then hide.
            e.Cancel = true;
            canceled = true;
            WindowState = FormWindowState.Minimized;
            Hide();
        }
        else if (_opts.HideOnClose)
        {
            // Simulates tray apps: cancel the close and just hide.
            e.Cancel = true;
            canceled = true;
            Hide();
        }
        Log($"LIFECYCLE FormClosing reason={e.CloseReason} canceled={canceled}");
    }

    protected override void WndProc(ref Message m)
    {
        // Never throw from WndProc; logging failures are swallowed to Debug output.
        try
        {
            if (m.Msg == MsgGetMinMaxInfo && _opts.BlockMessagesMilliseconds > 0)
            {
                Log($"BLOCK_MESSAGES WM_GETMINMAXINFO start={_opts.BlockMessagesMilliseconds}ms");
                System.Threading.Thread.Sleep(_opts.BlockMessagesMilliseconds);
                Log("BLOCK_MESSAGES WM_GETMINMAXINFO end");
            }

            string? name = m.Msg switch
            {
                MsgClose => "WM_CLOSE",
                MsgDestroy => "WM_DESTROY",
                MsgShowWindow => "WM_SHOWWINDOW",
                MsgSysCommand => "WM_SYSCOMMAND",
                MsgSize => "WM_SIZE",
                MsgNcCalcSize => "WM_NCCALCSIZE",
                MsgLButtonDown => "WM_LBUTTONDOWN",
                MsgLButtonUp => "WM_LBUTTONUP",
                MsgSetFocus => "WM_SETFOCUS",
                MsgKillFocus => "WM_KILLFOCUS",
                MsgMouseActivate => "WM_MOUSEACTIVATE",
                MsgEnterSizeMove => "WM_ENTERSIZEMOVE",
                MsgExitSizeMove => "WM_EXITSIZEMOVE",
                _ => null,
            };
            if (name != null)
                Log($"{name} wParam=0x{(long)m.WParam:X} lParam=0x{(long)m.LParam:X}");

            // Enforce a native minimum track size, reproducing the browser/Explorer
            // "refuses to shrink below its minimum" behavior that the containment
            // fix targets. WM_GETMINMAXINFO's lParam points to a MINMAXINFO whose
            // ptMinTrackSize we raise to the configured minimum.
            if (m.Msg == MsgGetMinMaxInfo && (_opts.MinWidth > 0 || _opts.MinHeight > 0))
            {
                var mmi = (MinMaxInfo)System.Runtime.InteropServices.Marshal.PtrToStructure(m.LParam, typeof(MinMaxInfo))!;
                if (_opts.MinWidth > mmi.ptMinTrackSize.x) mmi.ptMinTrackSize.x = _opts.MinWidth;
                if (_opts.MinHeight > mmi.ptMinTrackSize.y) mmi.ptMinTrackSize.y = _opts.MinHeight;
                System.Runtime.InteropServices.Marshal.StructureToPtr(mmi, m.LParam, true);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine("PigForm WndProc logging failed: " + ex.Message);
        }

        base.WndProc(ref m);

        if (_opts.ResizeProbe && m.Msg == MsgExitSizeMove)
        {
            Log($"NATIVE_MOVE_END sequence={_nativeMoveCount}");
        }
        else if (_opts.ResizeProbe && m.Msg == MsgEnterSizeMove)
        {
            _nativeMoveCount++;
            Log($"NATIVE_MOVE_START sequence={_nativeMoveCount}");
        }
        else if (_opts.ResizeProbe && m.Msg == MsgWindowPosChanged)
        {
            Log($"NATIVE_MOVE_POSITION sequence={_nativeMoveCount}");
        }

        if (_opts.ResizeProbe && (m.Msg == MsgSize || m.Msg == MsgShowWindow))
        {
            if (m.Msg == MsgSize)
                _resizeProbeCount++;
            // WinForms can still expose the previous managed ClientSize while
            // the WndProc is unwinding WM_SIZE. The native client rectangle is
            // the authoritative post-message rendering evidence and avoids
            // recording a stale width that disagrees with the HWND itself.
            (int clientWidth, int clientHeight) = PigDpi.GetNativeClientSize(Handle, ClientSize);
            Log($"CLIENT_PRESENT msg={(m.Msg == MsgSize ? "WM_SIZE" : "WM_SHOWWINDOW")} visible={Visible} client={clientWidth}x{clientHeight} formsClient={ClientSize.Width}x{ClientSize.Height} resizeCount={_resizeProbeCount}");
        }

        // When reparented as a WS_CHILD, WinForms does not always forward focus to
        // ActiveControl on show/tab-switch. Force the editable control focused so
        // keyboard input reaches it in the rapid-switch test.
        if (m.Msg == MsgSetFocus ||
            (m.Msg == MsgShowWindow && m.WParam != IntPtr.Zero) ||
            m.Msg == MsgMouseActivate)
        {
            bool focused = _textBox?.Focus() == true;
            Log($"FOCUS_FORWARD result={focused} msg={m.Msg:X} wParam=0x{(long)m.WParam:X}");
        }
    }

    /// <summary>Open/append/close on every write so lines are flushed even if the process is killed.</summary>
    private void Log(string message)
    {
        string? path = _logPath;
        if (path == null)
            return;
        try
        {
            lock (_logLock)
            {
                File.AppendAllText(path, $"{DateTime.UtcNow:o} {message}{Environment.NewLine}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine("PigForm log write failed: " + ex.Message);
        }
    }

    private static Color ParseColor(string? name)
    {
        switch (name?.ToLowerInvariant())
        {
            case "red": return Color.FromArgb(255, 0, 0);
            case "black": return Color.FromArgb(0, 0, 0);
            case "blue": return Color.FromArgb(0, 0, 255);
            case "green": return Color.FromArgb(0, 200, 0);
            case "white": return Color.White;
            default: return SystemColors.Control;
        }
    }

    private static Color ShiftBrightness(Color c, int delta)
    {
        int Shift(int v) => v > 200 ? Math.Max(0, v - delta) : Math.Min(255, v + delta);
        return Color.FromArgb(Shift(c.R), Shift(c.G), Shift(c.B));
    }
}
