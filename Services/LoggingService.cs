using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;

namespace TabDock.Services;

/// <summary>
/// Small rotating file logger. Writes to %APPDATA%\TabDock\logs\TabDock.log.
/// Log() only enqueues; a dedicated background thread does the actual disk I/O,
/// so callers on hot paths (focus handling, layout, WinEvent dispatch) never
/// block on file writes. The queue is bounded and non-blocking: if the writer
/// falls behind, new lines are dropped rather than stalling the caller.
/// </summary>
public sealed class LoggingService : IDisposable
{
    private readonly string _logDirectory;
    private readonly string _logFile;
    private const long MaxSize = 1 * 1024 * 1024; // 1 MB
    private const int QueueCapacity = 4096;

    private readonly BlockingCollection<string> _queue = new(QueueCapacity);
    private readonly Thread _writerThread;
    private long _droppedLines;
    private bool _disposed;

    public LoggingService()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _logDirectory = Path.Combine(appData, "TabDock", "logs");
        _logFile = Path.Combine(_logDirectory, "TabDock.log");
        Directory.CreateDirectory(_logDirectory);

        _writerThread = new Thread(WriterLoop)
        {
            IsBackground = true,
            Name = "TabDockLogWriter",
        };
        _writerThread.Start();
    }

    public void Log(string message)
    {
        string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";

        // TryAdd(0) never blocks: a full queue means the writer thread is stalled
        // (e.g. a slow disk), and a hot caller must not stall with it.
        if (!_queue.TryAdd(line, 0))
        {
            Interlocked.Increment(ref _droppedLines);
        }
    }

    public void LogException(string context, Exception ex)
    {
        Log($"EXCEPTION in {context}: {ex}");
    }

    private void WriterLoop()
    {
        foreach (string line in _queue.GetConsumingEnumerable())
        {
            try
            {
                RotateIfNeeded();
                long dropped = Interlocked.Exchange(ref _droppedLines, 0);
                string toWrite = dropped > 0
                    ? line + Environment.NewLine + $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] ({dropped} log line(s) dropped: writer fell behind)" + Environment.NewLine
                    : line + Environment.NewLine;
                File.AppendAllText(_logFile, toWrite);
            }
            catch (Exception ex)
            {
                // Logger must not throw. Best effort only.
                try { File.AppendAllText(Path.Combine(_logDirectory, "TabDock.log.err"), $"Failed to log: {ex}{Environment.NewLine}"); }
                catch { }
            }
        }
    }

    private void RotateIfNeeded()
    {
        try
        {
            if (File.Exists(_logFile))
            {
                var info = new FileInfo(_logFile);
                if (info.Length > MaxSize)
                {
                    string backup = _logFile + ".old";
                    if (File.Exists(backup))
                        File.Delete(backup);
                    File.Move(_logFile, backup);
                }
            }
        }
        catch
        {
            // Rotation is best effort.
        }
    }

    /// <summary>
    /// Stops accepting new lines and waits briefly for the writer thread to
    /// flush the queue. Safe to call multiple times.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _queue.CompleteAdding();
        _writerThread.Join(TimeSpan.FromSeconds(2));
        _queue.Dispose();
    }
}
