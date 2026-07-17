using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace TabDock.GuineaPig;

/// <summary>Parsed command-line options for the guinea-pig window.</summary>
public sealed class PigOptions
{
    public string Title = string.Empty;
    public string? Color;
    public bool Pulse;
    public bool HideOnClose;
    public bool MinimizeThenHideOnClose;
    public int SelfCloseAfterSeconds = -1;
    public int SelfMinimizeAfterSeconds = -1;
    public bool CloseButton;
    public bool ClickCounterButton;
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

    private readonly PigOptions _opts;
    private readonly string? _logPath;
    private readonly object _logLock = new object();
    private readonly Color _baseColor;
    private readonly Color _pulseColor;
    private bool _pulseOn;

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

        Shown += (s, e) => Log("LIFECYCLE Shown");
        FormClosing += OnPigFormClosing;
        FormClosed += (s, e) => Log("LIFECYCLE FormClosed");

        Log($"LIFECYCLE Created title='{opts.Title}' pid={Environment.ProcessId} color={_baseColor} " +
            $"pulse={opts.Pulse} hideOnClose={opts.HideOnClose} minThenHide={opts.MinimizeThenHideOnClose} " +
            $"selfClose={opts.SelfCloseAfterSeconds} selfMin={opts.SelfMinimizeAfterSeconds} closeButton={opts.CloseButton}");
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
            string? name = m.Msg switch
            {
                MsgClose => "WM_CLOSE",
                MsgDestroy => "WM_DESTROY",
                MsgShowWindow => "WM_SHOWWINDOW",
                MsgSysCommand => "WM_SYSCOMMAND",
                MsgSize => "WM_SIZE",
                MsgNcCalcSize => "WM_NCCALCSIZE",
                _ => null,
            };
            if (name != null)
                Log($"{name} wParam=0x{(long)m.WParam:X} lParam=0x{(long)m.LParam:X}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine("PigForm WndProc logging failed: " + ex.Message);
        }

        base.WndProc(ref m);
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
