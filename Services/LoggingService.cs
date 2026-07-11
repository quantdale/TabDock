using System;
using System.IO;
using System.Threading;

namespace TabDock.Services;

/// <summary>
/// Small rotating file logger. Writes to %APPDATA%\TabDock\logs\TabDock.log.
/// </summary>
public sealed class LoggingService
{
    private readonly string _logDirectory;
    private readonly string _logFile;
    private readonly ReaderWriterLockSlim _lock = new();
    private const long MaxSize = 1 * 1024 * 1024; // 1 MB

    public LoggingService()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _logDirectory = Path.Combine(appData, "TabDock", "logs");
        _logFile = Path.Combine(_logDirectory, "TabDock.log");
        Directory.CreateDirectory(_logDirectory);
    }

    public void Log(string message)
    {
        string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
        _lock.EnterWriteLock();
        try
        {
            RotateIfNeeded();
            File.AppendAllText(_logFile, line + Environment.NewLine);
        }
        catch (Exception ex)
        {
            // Logger must not throw. Best effort only.
            try { File.AppendAllText(Path.Combine(_logDirectory, "TabDock.log.err"), $"Failed to log: {ex}{Environment.NewLine}"); }
            catch { }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public void LogException(string context, Exception ex)
    {
        Log($"EXCEPTION in {context}: {ex}");
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
}
