using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TabDock.Models;
using TabDock.Services;
using TabDock.ViewModels;

namespace TabDock.Performance;

internal static class Program
{
    private const int DistributionSamples = 7;
    private const int TraceShortIterations = 10_000;
    private const int TraceLongIterations = 100_000;
    private const int LoggingIterations = 10_000;
    private const int PickerSamples = 5;
    private const int PersistenceSamples = 5;

    [STAThread]
    private static int Main(string[] args)
    {
        string scenario = GetOption(args, "--scenario") ?? "all";
        string? outputPath = GetOption(args, "--output");
        string root = Path.Combine(Path.GetTempPath(), "TabDock-performance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var result = new PerformanceReport
            {
                TimestampUtc = DateTimeOffset.UtcNow.ToString("O"),
                Scenario = scenario,
                Environment = CollectEnvironment(),
            };

            if (scenario is "all" or "trace")
                AddTraceMeasurements(result);
            if (scenario is "all" or "logging")
                AddLoggingMeasurements(result, root);
            if (scenario is "all" or "picker")
                AddPickerMeasurements(result, root);
            if (scenario is "all" or "persistence")
                AddPersistenceMeasurements(result, root);

            if (outputPath != null)
            {
                string fullOutputPath = Path.GetFullPath(outputPath);
                string? outputDirectory = Path.GetDirectoryName(fullOutputPath);
                if (!string.IsNullOrEmpty(outputDirectory))
                    Directory.CreateDirectory(outputDirectory);
                File.WriteAllText(fullOutputPath, JsonSerializer.Serialize(result, JsonOptions));
            }

            PrintSummary(result, outputPath);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Performance harness failed: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch { }
        }
    }

    private static void AddTraceMeasurements(PerformanceReport report)
    {
        AddDistribution(report, "diagnostic-trace-no-data-10k", DistributionSamples, () =>
        {
            var trace = new DiagnosticTrace(TraceShortIterations + 1);
            return Measure(() =>
            {
                for (int i = 0; i < TraceShortIterations; i++)
                    trace.Record("perf");
            });
        });

        AddDistribution(report, "diagnostic-trace-small-data-10k", DistributionSamples, () =>
        {
            var trace = new DiagnosticTrace(TraceShortIterations + 1);
            return Measure(() =>
            {
                for (int i = 0; i < TraceShortIterations; i++)
                {
                    var data = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["key"] = "value",
                    };
                    trace.Record("perf", data: data);
                }
            });
        });

        AddDistribution(report, "diagnostic-trace-no-data-100k", DistributionSamples, () =>
        {
            var trace = new DiagnosticTrace(TraceLongIterations + 1);
            return Measure(() =>
            {
                for (int i = 0; i < TraceLongIterations; i++)
                    trace.Record("perf");
            });
        });
    }

    private static void AddLoggingMeasurements(PerformanceReport report, string root)
    {
        var samples = new List<Measurement>();
        int lastWrittenLines = 0;
        int lastDropMarkers = 0;

        for (int sample = 0; sample < DistributionSamples; sample++)
        {
            string directory = Path.Combine(root, "logging-" + sample);
            var measurement = Measure(() =>
            {
                using var log = new LoggingService(directory);
                for (int i = 0; i < LoggingIterations; i++)
                    log.Log($"SHEPHERD[position] guest=0x{i:X} rect=0,0,800x600");
            });
            samples.Add(measurement);

            string logPath = Path.Combine(directory, "TabDock.log");
            if (File.Exists(logPath))
            {
                string content = File.ReadAllText(logPath);
                lastWrittenLines = content.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).Length;
                lastDropMarkers = Regex.Matches(content, "log line\\(s\\) dropped", RegexOptions.CultureInvariant).Count;
            }
        }

        report.Measurements.Add(Distribution("logging-burst-10k", samples, LoggingIterations,
            new Dictionary<string, object>
            {
                ["linesAttempted"] = LoggingIterations,
                ["lastRunLinesWritten"] = lastWrittenLines,
                ["lastRunDropMarkers"] = lastDropMarkers,
                ["exercisesProductionLogger"] = true,
            }));
    }

    private static void AddPickerMeasurements(PerformanceReport report, string root)
    {
        string directory = Path.Combine(root, "picker");
        using var log = new LoggingService(Path.Combine(directory, "logs"));
        var shepherd = new WindowShepherdService(log, Path.Combine(directory, "hidden-windows.json"));
        var persistence = new PersistenceService(log, Path.Combine(directory, "state.json"));
        var manager = new GroupManager(shepherd, persistence, log);

        // A fresh IconService gives a cold cache. The same view-model and
        // service are then refreshed repeatedly to measure warm reuse. Refresh
        // logs carry windowsSeen/candidates because EnumWindows is native and
        // intentionally remains a production path rather than a synthetic loop.
        var coldSamples = new List<Measurement>();
        var coldIconSamples = new List<Measurement>();
        var warmSamples = new List<Measurement>();
        var warmIconSamples = new List<Measurement>();
        int coldCandidates = 0;
        int warmCandidates = 0;
        for (int sample = 0; sample < PickerSamples; sample++)
        {
            var icons = new IconService(log);
            CapturePickerViewModel? coldPicker = null;
            var cold = Measure(() =>
            {
                coldPicker = new CapturePickerViewModel(manager, icons, log);
                coldCandidates = coldPicker.Windows.Count;
            });
            coldSamples.Add(cold);
            coldIconSamples.Add(Measure(() => coldPicker!.IconResolutionCompletion.GetAwaiter().GetResult()));
            coldPicker!.Dispose();
        }

        var warmIcons = new IconService(log);
        var warmPicker = new CapturePickerViewModel(manager, warmIcons, log);
        warmPicker.IconResolutionCompletion.GetAwaiter().GetResult();
        for (int sample = 0; sample < PickerSamples; sample++)
        {
            Task? warmCompletion = null;
            var warm = Measure(() =>
            {
                warmPicker.Refresh();
                warmCompletion = warmPicker.IconResolutionCompletion;
                warmCandidates = warmPicker.Windows.Count;
            });
            warmSamples.Add(warm);
            warmIconSamples.Add(Measure(() => warmCompletion!.GetAwaiter().GetResult()));
        }
        warmPicker.Dispose();

        log.Dispose();
        List<(int WindowsSeen, int Candidates)> refreshes = ReadPickerRefreshes(
            Path.Combine(directory, "logs", "TabDock.log"));
        int coldWindowsSeen = refreshes.Count > 0 ? refreshes[0].WindowsSeen : -1;
        int warmWindowsSeen = refreshes.Count > 0 ? refreshes[^1].WindowsSeen : -1;

        report.Measurements.Add(Distribution("capture-picker-refresh-cold-icon-cache", coldSamples, 1,
            new Dictionary<string, object>
            {
                ["candidateCount"] = coldCandidates,
                ["windowCountSeen"] = coldWindowsSeen,
                ["iconCache"] = "cold-per-sample",
                ["nativeEnumeration"] = true,
            }));
        report.Measurements.Add(Distribution("capture-picker-icons-cold-completion", coldIconSamples, 1,
            new Dictionary<string, object>
            {
                ["candidateCount"] = coldCandidates,
                ["meaning"] = "background extraction completion after rows were available",
            }));
        report.Measurements.Add(Distribution("capture-picker-refresh-warm-icon-cache", warmSamples, 1,
            new Dictionary<string, object>
            {
                ["candidateCount"] = warmCandidates,
                ["windowCountSeen"] = warmWindowsSeen,
                ["iconCache"] = "warm-single-service",
                ["nativeEnumeration"] = true,
            }));
        report.Measurements.Add(Distribution("capture-picker-icons-warm-completion", warmIconSamples, 1,
            new Dictionary<string, object>
            {
                ["candidateCount"] = warmCandidates,
                ["meaning"] = "background extraction completion after rows were available",
            }));
    }

    private static void AddPersistenceMeasurements(PerformanceReport report, string root)
    {
        string directory = Path.Combine(root, "persistence");
        using var log = new LoggingService(Path.Combine(directory, "logs"));
        var groups = CreateGroups(groupCount: 3, tabsPerGroup: 4);

        var changedSamples = new List<Measurement>();
        for (int sample = 0; sample < PersistenceSamples; sample++)
        {
            string path = Path.Combine(directory, "changed-" + sample, "state.json");
            var persistence = new PersistenceService(log, path);
            var measurement = Measure(() =>
            {
                groups[0].Name = "Changed-" + Guid.NewGuid().ToString("N");
                persistence.Save(groups);
            });
            changedSamples.Add(measurement);
        }

        var unchangedSamples = new List<Measurement>();
        string unchangedPath = Path.Combine(directory, "unchanged", "state.json");
        var unchangedPersistence = new PersistenceService(log, unchangedPath);
        unchangedPersistence.Save(groups);
        for (int sample = 0; sample < PersistenceSamples; sample++)
            unchangedSamples.Add(Measure(() => unchangedPersistence.Save(groups)));

        report.Measurements.Add(Distribution("persistence-changed-save-3x4", changedSamples, 1,
            new Dictionary<string, object>
            {
                ["groups"] = 3,
                ["tabsPerGroup"] = 4,
                ["durabilityPath"] = "production FileStream flush + atomic move",
            }));
        report.Measurements.Add(Distribution("persistence-unchanged-save-3x4", unchangedSamples, 1,
            new Dictionary<string, object>
            {
                ["groups"] = 3,
                ["tabsPerGroup"] = 4,
                ["identicalSaveSkip"] = true,
            }));
    }

    private static List<TabDock.Models.Group> CreateGroups(int groupCount, int tabsPerGroup)
    {
        var groups = new List<TabDock.Models.Group>(groupCount);
        for (int groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            var group = new TabDock.Models.Group { Name = "Perf group " + groupIndex };
            for (int tabIndex = 0; tabIndex < tabsPerGroup; tabIndex++)
            {
                group.Members.Add(new CapturedWindow
                {
                    Hwnd = new IntPtr(0x1000 + groupIndex * 100 + tabIndex),
                    ProcessId = (uint)(1000 + groupIndex * 10 + tabIndex),
                    WindowThreadId = (uint)(2000 + groupIndex * 10 + tabIndex),
                    WindowIdentityToken = 0x5000 + tabIndex,
                    ProcessStartTimeUtcTicks = 638000000000000000L + tabIndex,
                    ExePath = $"C:\\Perf\\App{groupIndex}.exe",
                    OriginalClassName = "PerfWindow",
                    OriginalTitle = $"Perf tab {groupIndex}-{tabIndex}",
                    OriginallyVisible = true,
                });
            }
            group.ActiveIndex = 1;
            groups.Add(group);
        }
        return groups;
    }

    private static List<(int WindowsSeen, int Candidates)> ReadPickerRefreshes(string logPath)
    {
        var result = new List<(int WindowsSeen, int Candidates)>();
        if (!File.Exists(logPath))
            return result;

        const string pattern = @"PICKER\[refresh\] windowsSeen=(\d+) candidates=(\d+)";
        foreach (Match match in Regex.Matches(File.ReadAllText(logPath), pattern, RegexOptions.CultureInvariant))
        {
            if (int.TryParse(match.Groups[1].Value, out int windowsSeen)
                && int.TryParse(match.Groups[2].Value, out int candidates))
            {
                result.Add((windowsSeen, candidates));
            }
        }
        return result;
    }

    private static void AddDistribution(
        PerformanceReport report,
        string name,
        int samples,
        Func<Measurement> operation)
    {
        var measurements = new List<Measurement>(samples);
        for (int i = 0; i < samples; i++)
            measurements.Add(operation());
        report.Measurements.Add(Distribution(name, measurements, 1, null));
    }

    private static Measurement Measure(Action action)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        Stopwatch stopwatch = Stopwatch.StartNew();
        action();
        stopwatch.Stop();
        long allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
        return new Measurement
        {
            ElapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds,
            AllocatedBytes = Math.Max(0, allocatedAfter - allocatedBefore),
        };
    }

    private static MeasurementSummary Distribution(
        string name,
        IReadOnlyList<Measurement> samples,
        int iterations,
        Dictionary<string, object>? metadata)
    {
        double[] elapsed = samples.Select(sample => sample.ElapsedMilliseconds).OrderBy(value => value).ToArray();
        long[] allocations = samples.Select(sample => sample.AllocatedBytes).OrderBy(value => value).ToArray();
        return new MeasurementSummary
        {
            Name = name,
            Iterations = iterations,
            SampleCount = samples.Count,
            ElapsedMilliseconds = Percentiles(elapsed),
            AllocatedBytes = Percentiles(allocations.Select(value => (double)value).ToArray()),
            Metadata = metadata ?? new Dictionary<string, object>(),
        };
    }

    private static PercentileSummary Percentiles(IReadOnlyList<double> sorted)
        => new()
        {
            Min = sorted[0],
            Median = Percentile(sorted, 0.50),
            P90 = Percentile(sorted, 0.90),
            P95 = Percentile(sorted, 0.95),
            Max = sorted[^1],
        };

    private static double Percentile(IReadOnlyList<double> sorted, double percentile)
    {
        if (sorted.Count == 1)
            return sorted[0];
        double position = (sorted.Count - 1) * percentile;
        int lower = (int)Math.Floor(position);
        int upper = (int)Math.Ceiling(position);
        if (lower == upper)
            return sorted[lower];
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
    }

    private static EnvironmentSummary CollectEnvironment()
    {
        Assembly app = typeof(DiagnosticTrace).Assembly;
        return new EnvironmentSummary
        {
            OperatingSystem = RuntimeInformation.OSDescription,
            OsVersion = Environment.OSVersion.VersionString,
            Runtime = RuntimeInformation.FrameworkDescription,
            ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            ProcessorCount = Environment.ProcessorCount,
            GcServer = System.Runtime.GCSettings.IsServerGC,
            BuildConfiguration = Environment.GetEnvironmentVariable("TABDOCK_PERF_CONFIGURATION") ?? "unknown",
            GitSha = Environment.GetEnvironmentVariable("TABDOCK_PERF_GIT_SHA") ?? "unknown",
            AppAssembly = app.GetName().Name ?? "unknown",
            AppInformationalVersion = app.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown",
            AppFileVersion = File.Exists(app.Location) ? FileVersionInfo.GetVersionInfo(app.Location).FileVersion ?? "unknown" : "unknown",
        };
    }

    private static void PrintSummary(PerformanceReport report, string? outputPath)
    {
        Console.WriteLine($"TabDock performance harness: scenario={report.Scenario} samples={DistributionSamples}");
        Console.WriteLine($"Environment: {report.Environment.OperatingSystem}; runtime={report.Environment.Runtime}; sha={report.Environment.GitSha}");
        foreach (MeasurementSummary measurement in report.Measurements)
        {
            Console.WriteLine($"{measurement.Name}: median={measurement.ElapsedMilliseconds.Median:F3}ms p95={measurement.ElapsedMilliseconds.P95:F3}ms " +
                $"allocMedian={measurement.AllocatedBytes.Median:F0}B iterations={measurement.Iterations}");
        }
        if (outputPath != null)
            Console.WriteLine($"JSON: {Path.GetFullPath(outputPath)}");
    }

    private static string? GetOption(string[] args, string option)
    {
        for (int i = 0; i + 1 < args.Length; i++)
            if (args[i].Equals(option, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private sealed class PerformanceReport
    {
        public string TimestampUtc { get; set; } = string.Empty;
        public string Scenario { get; set; } = string.Empty;
        public EnvironmentSummary Environment { get; set; } = new();
        public List<MeasurementSummary> Measurements { get; } = new();
    }

    private sealed class EnvironmentSummary
    {
        public string OperatingSystem { get; set; } = string.Empty;
        public string OsVersion { get; set; } = string.Empty;
        public string Runtime { get; set; } = string.Empty;
        public string ProcessArchitecture { get; set; } = string.Empty;
        public int ProcessorCount { get; set; }
        public bool GcServer { get; set; }
        public string BuildConfiguration { get; set; } = string.Empty;
        public string GitSha { get; set; } = string.Empty;
        public string AppAssembly { get; set; } = string.Empty;
        public string AppInformationalVersion { get; set; } = string.Empty;
        public string AppFileVersion { get; set; } = string.Empty;
    }

    private sealed class MeasurementSummary
    {
        public string Name { get; set; } = string.Empty;
        public int Iterations { get; set; }
        public int SampleCount { get; set; }
        public PercentileSummary ElapsedMilliseconds { get; set; } = new();
        public PercentileSummary AllocatedBytes { get; set; } = new();
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    private sealed class PercentileSummary
    {
        public double Min { get; set; }
        public double Median { get; set; }
        public double P90 { get; set; }
        public double P95 { get; set; }
        public double Max { get; set; }
    }

    private sealed class Measurement
    {
        public double ElapsedMilliseconds { get; set; }
        public long AllocatedBytes { get; set; }
    }
}
