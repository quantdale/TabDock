using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using TabDock.Infrastructure;
using Xunit;

namespace TabDock.UnitTests;

[CollectionDefinition("BindingTraceDiagnostics", DisableParallelization = true)]
public sealed class BindingTraceDiagnosticsCollection
{
}

/// <summary>
/// The binding-trace switch is process-wide, so these tests run isolated from
/// the rest of the suite and restore the WPF listener list afterwards.
/// </summary>
[Collection("BindingTraceDiagnostics")]
public sealed class BindingTraceDiagnosticsTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"tabdock-binding-trace-{Guid.NewGuid():N}.log");

    [Fact]
    public void EnableIfRequestedIsANoOpWhenUnset()
    {
        Environment.SetEnvironmentVariable(BindingTraceDiagnostics.VariableName, null);
        int before = PresentationTraceSources.DataBindingSource.Listeners.Count;

        BindingTraceDiagnostics.EnableIfRequested();

        Assert.Equal(before, PresentationTraceSources.DataBindingSource.Listeners.Count);
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void EnableIfRequestedInstallsAFileListenerWhenConfigured()
    {
        Environment.SetEnvironmentVariable(BindingTraceDiagnostics.VariableName, _path);
        int before = PresentationTraceSources.DataBindingSource.Listeners.Count;
        try
        {
            BindingTraceDiagnostics.EnableIfRequested();

            Assert.Equal(before + 1, PresentationTraceSources.DataBindingSource.Listeners.Count);
            TraceListener added = PresentationTraceSources.DataBindingSource.Listeners
                .Cast<TraceListener>()
                .Last();
            Assert.IsType<TextWriterTraceListener>(added);
            Assert.Equal(SourceLevels.Warning, PresentationTraceSources.DataBindingSource.Switch.Level);
        }
        finally
        {
            Environment.SetEnvironmentVariable(BindingTraceDiagnostics.VariableName, null);
            while (PresentationTraceSources.DataBindingSource.Listeners.Count > before)
            {
                PresentationTraceSources.DataBindingSource.Listeners.RemoveAt(
                    PresentationTraceSources.DataBindingSource.Listeners.Count - 1);
            }
        }
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_path))
                File.Delete(_path);
        }
        catch
        {
            // Temp cleanup only.
        }
    }
}
