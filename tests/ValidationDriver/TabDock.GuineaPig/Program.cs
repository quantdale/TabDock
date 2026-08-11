using System;
using System.IO;
using System.Windows.Forms;

namespace TabDock.GuineaPig;

/// <summary>
/// Entry point for the TabDock validation guinea-pig window.
/// A tiny WinForms app whose sole purpose is to be captured/released by TabDock
/// under driver control while logging every interesting window message it receives.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        PigOptions opts;
        try
        {
            opts = Parse(args);
        }
        catch (Exception ex)
        {
            WriteErrorLog($"Argument error: {ex.Message} (args: {string.Join(" ", args)})");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(opts.Title))
        {
            WriteErrorLog("--title is required.");
            return 1;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Apply the deliberate DPI-awareness thread context BEFORE the form is
        // created so the resulting window belongs to the requested awareness
        // class (mixed-mode DPI scaling: a top-level window inherits the thread
        // context current at CreateWindow). Restore the previous context in a
        // finally so a pig that requested a mode never leaks that context.
        IntPtr previousDpi = PigDpi.ApplyThreadDpi(opts.DpiMode);
        try
        {
            Application.Run(new PigForm(opts));
        }
        finally
        {
            PigDpi.RestoreThreadDpi(previousDpi);
        }
        return 0;
    }

    private static PigOptions Parse(string[] args)
    {
        var opts = new PigOptions();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--title":
                    opts.Title = Require(args, ref i);
                    break;
                case "--color":
                    opts.Color = Require(args, ref i);
                    break;
                case "--pulse":
                    opts.Pulse = true;
                    break;
                case "--hide-on-close":
                    opts.HideOnClose = true;
                    break;
                case "--minimize-then-hide-on-close":
                    opts.MinimizeThenHideOnClose = true;
                    break;
                case "--self-close-after":
                    opts.SelfCloseAfterSeconds = int.Parse(Require(args, ref i));
                    break;
                case "--self-minimize-after":
                    opts.SelfMinimizeAfterSeconds = int.Parse(Require(args, ref i));
                    break;
                case "--close-button":
                    opts.CloseButton = true;
                    break;
                case "--click-counter-button":
                    opts.ClickCounterButton = true;
                    break;
                case "--text-box":
                    opts.TextBox = true;
                    break;
                case "--min-width":
                    opts.MinWidth = int.Parse(Require(args, ref i));
                    break;
                case "--min-height":
                    opts.MinHeight = int.Parse(Require(args, ref i));
                    break;
                case "--dpi":
                    opts.DpiMode = ParseDpiMode(Require(args, ref i));
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{args[i]}'.");
            }
        }
        return opts;
    }

    private static string Require(string[] args, ref int i)
    {
        if (i + 1 >= args.Length)
            throw new ArgumentException($"'{args[i]}' requires a value.");
        return args[++i];
    }

    private static DpiMenuMode ParseDpiMode(string value)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "unaware": return DpiMenuMode.Unaware;
            case "system": return DpiMenuMode.SystemAware;
            case "per-monitor": return DpiMenuMode.PerMonitorAware;
            case "per-monitor-v2": return DpiMenuMode.PerMonitorAwareV2;
            default: throw new ArgumentException($"Unknown --dpi mode '{value}' (expected unaware|system|per-monitor|per-monitor-v2).");
        }
    }

    /// <summary>Writes startup errors to the shared validation log directory (no console on WinExe).</summary>
    private static void WriteErrorLog(string message)
    {
        try
        {
            string dir = Path.Combine(Path.GetTempPath(), "TabDock-Validation");
            Directory.CreateDirectory(dir);
            File.AppendAllText(
                Path.Combine(dir, $"pig-error-{Environment.ProcessId}.log"),
                $"{DateTime.UtcNow:o} {message}{Environment.NewLine}");
        }
        catch
        {
            // Nothing else we can do from a windowless startup failure.
        }
    }
}
