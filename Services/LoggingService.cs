using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace TabDock.Services;

/// <summary>
/// Small rotating file logger. Writes to %APPDATA%\TabDock\logs\TabDock.log.
/// Log() only enqueues; a dedicated background thread does the actual disk I/O,
/// so callers on hot paths (focus handling, layout, WinEvent dispatch) never
/// block on file writes. The queue is bounded and non-blocking: if the writer
/// falls behind, new lines are dropped rather than stalling the caller.
///
/// The writer keeps ONE append-mode handle open for the process lifetime and
/// drains the queue in batches (PERF25-01). The previous implementation issued
/// File.Exists + new FileInfo(...).Length + File.AppendAllText — an open, seek,
/// write and close, plus two directory-metadata round trips — for every single
/// line, which is the wrong shape for a logger that a container drag or a
/// WinEvent storm can drive hundreds of lines per second. Size is now tracked
/// from the stream position (free, already maintained by FileStream) instead of
/// being re-stat'ed per line, and one Flush covers a whole batch rather than
/// one per line. The file is opened with FileShare.ReadWrite so tailing it in
/// another tool still works while TabDock holds it open.
/// </summary>
public sealed class LoggingService : IDisposable
{
    private readonly string _logDirectory;
    private readonly string _logFile;
    private readonly bool _fileBacked;
    private readonly string? _storageFailureReason;
    private const long MaxSize = 1 * 1024 * 1024; // 1 MB
    private const int QueueCapacity = 4096;
    // Upper bound on lines coalesced into a single write+flush. Large enough
    // that a burst costs one round trip, small enough that a stalled disk can
    // never make a single write unboundedly large.
    private const int MaxBatchLines = 256;

    private readonly BlockingCollection<string> _queue = new(QueueCapacity);
    private readonly Thread _writerThread;
    private readonly ConcurrentQueue<string> _memoryLines = new();

    // Writer-thread-only state. Nothing else may touch these: the writer thread
    // owns the handle for its whole lifetime and closes it as it unwinds.
    private readonly StringBuilder _batch = new(8 * 1024);
    private FileStream? _stream;
    private StreamWriter? _writer;

    private long _droppedLines;
    // Volatile: exit/crash paths log from the UI thread, the WinEvent dispatch
    // thread, and the AppDomain unhandled-exception thread, any of which can race
    // Dispose().
    private volatile bool _disposed;
    // Re-entrancy gate for Dispose: Interlocked.Exchange makes exactly one
    // concurrent caller perform the teardown; the rest return immediately.
    private int _disposeStarted;

    // ---- Writer-thread-only failure-path state ----
    // Cap on the .err fallback file: a persistent logging failure must not fill
    // the disk. When the file exceeds this, it is truncated before appending.
    private const long MaxErrFileSize = 64 * 1024;
    // Consecutive identical error lines are suppressed so a tight failure loop
    // does not spam the fallback file either.
    private string? _lastErrLine;
    // After a failed rotation, retry at most once per this many batches instead
    // of churning a close/delete/move/open cycle on every batch for the rest of
    // the session.
    private const int RotationRetryEveryBatches = 20;
    private int _batchesUntilRotationRetry;

    public LoggingService(string? logDirectory = null)
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _logDirectory = string.IsNullOrWhiteSpace(logDirectory)
            ? Path.Combine(appData, "TabDock", "logs")
            : Path.GetFullPath(logDirectory);
        _logFile = Path.Combine(_logDirectory, "TabDock.log");
        try
        {
            Directory.CreateDirectory(_logDirectory);
            _fileBacked = true;
            _storageFailureReason = null;
        }
        catch (Exception ex)
        {
            // Logging is diagnostic, not a reason to prevent the app from
            // launching. Keep a bounded in-memory tail; safety-critical guest
            // capture is gated separately by the recovery journal.
            _fileBacked = false;
            _storageFailureReason = ex.GetType().Name + ": " + ex.Message;
        }

        _writerThread = new Thread(WriterLoop)
        {
            IsBackground = true,
            Name = "TabDockLogWriter",
        };
        _writerThread.Start();
    }

    public bool IsFileBacked => _fileBacked;

    public string? StorageFailureReason => _storageFailureReason;

    internal IReadOnlyList<string> MemoryLines => _memoryLines.ToArray();

    public void Log(string message)
    {
        // Anything logged after Dispose() is dropped rather than thrown at the
        // caller: TryAdd on a completed BlockingCollection throws
        // InvalidOperationException (and ObjectDisposedException once the queue
        // itself is gone), and the callers here are exit and crash handlers —
        // App.CurrentDomain_UnhandledException logs while the app may already have
        // torn the logger down, and an exception raised there replaces the real
        // failure with a logging failure.
        if (_disposed)
            return;

        string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";

        try
        {
            // TryAdd(0) never blocks: a full queue means the writer thread is stalled
            // (e.g. a slow disk), and a hot caller must not stall with it.
            if (!_queue.TryAdd(line, 0))
            {
                Interlocked.Increment(ref _droppedLines);
            }
        }
        catch (InvalidOperationException)
        {
            // Dispose() completed the collection between the check above and
            // here. ObjectDisposedException (same race, one step further
            // along) is covered too — it derives from InvalidOperationException.
        }
    }

    public void LogException(string context, Exception ex)
    {
        Log($"EXCEPTION in {context}: {ex}");
    }

    private void WriterLoop()
    {
        // The outer try covers the enumerator itself, not just the loop body: an
        // exception escaping a background thread terminates the whole process, and
        // GetConsumingEnumerable throws ObjectDisposedException if Dispose() ever
        // gives up waiting for this thread and frees the queue underneath it — a
        // crash on shutdown for nothing more than an unflushed log line.
        try
        {
            foreach (string line in _queue.GetConsumingEnumerable())
            {
                try
                {
                    _batch.Clear();
                    _batch.Append(line).Append(Environment.NewLine);

                    // Opportunistically absorb whatever else is already queued.
                    // TryTake(out _) never blocks, so a quiet logger still writes
                    // its single line immediately — this only coalesces bursts.
                    int lines = 1;
                    while (lines < MaxBatchLines && _queue.TryTake(out string? next))
                    {
                        _batch.Append(next).Append(Environment.NewLine);
                        lines++;
                    }

                    long dropped = Interlocked.Exchange(ref _droppedLines, 0);
                    if (dropped > 0)
                    {
                        _batch.Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                              .Append("] (").Append(dropped).Append(" log line(s) dropped: writer fell behind)")
                              .Append(Environment.NewLine);
                    }

                    WriteBatch();
                }
                catch (Exception ex)
                {
                    // Logger must not throw. Best effort only. Drop the handle so
                    // the next batch reopens it: the most likely cause of a write
                    // failure is the file having been moved or deleted underneath
                    // the open stream.
                    CloseWriter();
                    WriteErrLine($"Failed to log: {ex}");
                }
            }
        }
        catch (Exception ex)
        {
            WriteErrLine($"Log writer stopped: {ex}");
        }
        finally
        {
            CloseWriter();
        }
    }

    /// <summary>
    /// Appends one line to the bounded <c>TabDock.log.err</c> fallback file.
    /// Writer thread only. Two bounds keep a persistent logging failure from
    /// filling the disk: consecutive identical lines are suppressed, and the
    /// file is truncated once it exceeds <see cref="MaxErrFileSize"/>.
    /// </summary>
    private void WriteErrLine(string line)
    {
        if (line == _lastErrLine)
            return;
        _lastErrLine = line;

        try
        {
            string errFile = Path.Combine(_logDirectory, "TabDock.log.err");
            if (File.Exists(errFile) && new FileInfo(errFile).Length > MaxErrFileSize)
                File.Delete(errFile);
            File.AppendAllText(errFile, line + Environment.NewLine);
        }
        catch { }
    }

    /// <summary>
    /// Writes the accumulated batch through the persistent handle and flushes
    /// once. Writer thread only. TextWriter.Write(StringBuilder) streams the
    /// builder's chunks straight out, so batching costs no extra string
    /// allocation on top of the queued lines themselves.
    /// </summary>
    private void WriteBatch()
    {
        if (!_fileBacked)
        {
            string[] lines = _batch.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                _memoryLines.Enqueue(line);
                while (_memoryLines.Count > 512 && _memoryLines.TryDequeue(out _)) { }
            }
            return;
        }
        EnsureWriter();
        if (_writer == null)
            throw new IOException($"Log file '{_logFile}' is not open for append.");

        _writer.Write(_batch);
        // Flush per batch, not per line: a batch is normally a single line, so
        // log durability against a force-kill is unchanged in the quiet case,
        // and a burst costs one flush instead of hundreds.
        _writer.Flush();
        RotateIfNeeded();
    }

    private void EnsureWriter()
    {
        if (!_fileBacked)
            return;
        if (_writer != null)
            return;
        try
        {
            Directory.CreateDirectory(_logDirectory);
            _stream = new FileStream(_logFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 8192);
            // No BOM, matching what File.AppendAllText produced before, so an
            // existing log file keeps a single consistent encoding across the
            // upgrade.
            _writer = new StreamWriter(_stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch
        {
            // Leave nothing half-open, and let the caller's handler record the
            // failure in TabDock.log.err — the same diagnostic the per-line
            // File.AppendAllText produced when it could not open the file.
            CloseWriter();
            throw;
        }
    }

    private void CloseWriter()
    {
        try { _writer?.Dispose(); } catch { }
        try { _stream?.Dispose(); } catch { }
        _writer = null;
        _stream = null;
    }

    /// <summary>
    /// Rotates at <see cref="MaxSize"/> using the stream's own position rather
    /// than re-stat'ing the file: FileStream already tracks it, and after the
    /// batch flush above it is exact. Writer thread only, and only ever called
    /// straight after a flush.
    /// </summary>
    private void RotateIfNeeded()
    {
        if (_stream == null || _stream.Position <= MaxSize)
            return;

        // Back off after a failed rotation: without this, a File.Move that keeps
        // failing (e.g. the file held open by another tool) costs a
        // close/delete/move/open cycle on every batch for the rest of the session.
        if (_batchesUntilRotationRetry > 0)
        {
            _batchesUntilRotationRetry--;
            return;
        }

        // The handle has to go before the move: an open file cannot be renamed
        // on Windows.
        CloseWriter();
        bool rotated = false;
        try
        {
            string backup = _logFile + ".old";
            if (File.Exists(backup))
                File.Delete(backup);
            File.Move(_logFile, backup);
            rotated = true;
        }
        catch
        {
            // Rotation is best effort — schedule the next attempt on a bounded
            // cadence rather than retrying on the very next batch.
            _batchesUntilRotationRetry = RotationRetryEveryBatches;
        }
        if (rotated)
            _batchesUntilRotationRetry = 0;

        // Reopen either way — a failed rotation must not silently stop logging
        // for the rest of the session.
        EnsureWriter();
    }

    /// <summary>
    /// Stops accepting new lines and waits briefly for the writer thread to
    /// flush the queue. Safe to call multiple times.
    /// </summary>
    public void Dispose()
    {
        // Re-entrant across threads: crash paths can race normal shutdown, and a
        // second concurrent caller reaching CompleteAdding() would throw. Exactly
        // one caller wins the exchange and performs the teardown.
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
            return;
        _disposed = true;
        _queue.CompleteAdding();
        // Only free the queue once the writer has genuinely finished with it.
        // Disposing it out from under a still-running writer (a stalled disk
        // outlasting the 2s budget) faults that thread instead, and leaking a
        // BlockingCollection in a process that is exiting anyway costs nothing.
        if (_writerThread.Join(TimeSpan.FromSeconds(2)))
            _queue.Dispose();
    }
}
