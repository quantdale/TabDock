using System;
using System.Diagnostics;
using System.IO;

namespace TabDock.Infrastructure;

/// <summary>
/// Opt-in WPF data-binding trace capture for local diagnostics. When the
/// <see cref="VariableName"/> environment variable names a writable log path,
/// WPF binding warnings/errors are appended there so silent binding failures in
/// the XAML surfaces can be found without a debugger. When the variable is
/// unset (the normal case) the application behaves exactly as before.
/// </summary>
public static class BindingTraceDiagnostics
{
    public const string VariableName = "TABDOCK_TRACE_BINDINGS";

    public static void EnableIfRequested()
    {
        string? path = Environment.GetEnvironmentVariable(VariableName);
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            PresentationTraceSources.Refresh();
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
            var listener = new TextWriterTraceListener(path)
            {
                TraceOutputOptions = TraceOptions.None,
            };
            PresentationTraceSources.DataBindingSource.Listeners.Add(listener);

            // The listener is process-wide and the process can terminate from
            // any exit path, so flush on ProcessExit as a safety net.
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                try
                {
                    listener.Flush();
                    listener.Close();
                }
                catch
                {
                    // Best-effort flush only.
                }
            };
        }
        catch
        {
            // Diagnostics must never prevent startup.
        }
    }
}
