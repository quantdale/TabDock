using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TabDock.ValidationDriver;

/// <summary>A bounded, privacy-safe native interaction event.</summary>
internal sealed record NativeInteractionEvent(
    long Sequence,
    DateTimeOffset TimestampUtc,
    long ElapsedMilliseconds,
    string Type,
    string Role,
    string Hwnd,
    IReadOnlyDictionary<string, string> Data);

/// <summary>
/// Records the smallest useful physical-validation timeline. The ring buffer
/// is intentionally bounded so an event storm cannot turn evidence capture
/// into a second failure or persist arbitrary desktop content.
/// </summary>
internal sealed class NativeInteractionTimeline
{
    private const int DefaultCapacity = 1024;
    private readonly object _sync = new();
    private readonly Queue<NativeInteractionEvent> _events;
    private readonly int _capacity;
    private readonly long _startTimestamp;
    private readonly DateTimeOffset _startUtc;
    private long _sequence;

    public NativeInteractionTimeline(int capacity = DefaultCapacity)
    {
        if (capacity < 16)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Timeline capacity must be at least 16.");

        _capacity = capacity;
        _events = new Queue<NativeInteractionEvent>(capacity);
        _startTimestamp = Stopwatch.GetTimestamp();
        _startUtc = DateTimeOffset.UtcNow;
    }

    public int Capacity => _capacity;

    public int Count
    {
        get
        {
            lock (_sync)
                return _events.Count;
        }
    }

    public void Record(
        string type,
        string role = "Unknown",
        IntPtr hwnd = default,
        IReadOnlyDictionary<string, string>? data = null)
    {
        string safeType = SafeToken(type, "unknown-event");
        string safeRole = SafeToken(role, "Unknown");
        var safeData = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (data != null)
        {
            foreach (KeyValuePair<string, string> pair in data)
            {
                string key = SafeToken(pair.Key, "value");
                safeData[key] = SanitizeValue(key, pair.Value);
            }
        }

        long now = Stopwatch.GetTimestamp();
        long elapsed = (long)Math.Max(0,
            (now - _startTimestamp) * 1000.0 / Stopwatch.Frequency);
        var item = new NativeInteractionEvent(
            Sequence: System.Threading.Interlocked.Increment(ref _sequence),
            TimestampUtc: _startUtc.AddMilliseconds(elapsed),
            ElapsedMilliseconds: elapsed,
            Type: safeType,
            Role: safeRole,
            Hwnd: hwnd == IntPtr.Zero ? "0x0" : $"0x{hwnd.ToInt64():X}",
            Data: safeData);

        lock (_sync)
        {
            if (_events.Count == _capacity)
                _events.Dequeue();
            _events.Enqueue(item);
        }
    }

    public IReadOnlyList<NativeInteractionEvent> Snapshot()
    {
        lock (_sync)
            return _events.ToArray();
    }

    public void Write(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var payload = new
        {
            capacity = Capacity,
            retained = Count,
            events = Snapshot(),
        };
        File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
        }));
    }

    private static string SafeToken(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        string token = value.Trim();
        return token.Length > 96 ? token[..96] : token;
    }

    private static string SanitizeValue(string key, string? value)
    {
        string lower = key.ToLowerInvariant();
        if (lower.Contains("title", StringComparison.Ordinal)
            || lower.Contains("url", StringComparison.Ordinal)
            || lower.Contains("path", StringComparison.Ordinal)
            || lower.Contains("content", StringComparison.Ordinal)
            || lower.Contains("document", StringComparison.Ordinal)
            || lower.Contains("text", StringComparison.Ordinal))
            return "<redacted>";

        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string result = value.Trim();
        return result.Length > 160 ? result[..160] : result;
    }
}
