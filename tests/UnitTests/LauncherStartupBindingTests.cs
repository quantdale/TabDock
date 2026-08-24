using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Regression for the release-candidate cold-start crash physically reproduced
/// by the first supervised ValidationDriver run (2026-08-23): the launcher's
/// <c>&lt;Run Text="{Binding Groups.Count}"&gt;</c> bindings omitted an explicit
/// mode. <see cref="Run.TextProperty"/> defaults to TwoWay because Run text is
/// editable inside a RichTextBox, and the binding engine throws
/// InvalidOperationException ("A TwoWay or OneWayToSource binding cannot work
/// on the read-only property 'Count'") while attaching during
/// <c>Window.Show()</c>. Every launch with an empty state died in
/// Application_Startup before the launcher appeared; headless VM tests never
/// instantiate the XAML tree, so only these contracts can see the hazard.
/// </summary>
public sealed class LauncherStartupBindingTests
{
    [Fact]
    public void RunTextProperty_DefaultsToTwoWayBinding()
    {
        // The mechanism behind the crash: an unmoded {Binding} on Run.Text is
        // created as TwoWay. Any read-only source path (ObservableCollection
        // Count) therefore throws at attach time. If a future WPF version ever
        // changes this default, the source contract below stays correct and
        // this pin documents the historical hazard honestly.
        var metadata = Assert.IsType<FrameworkPropertyMetadata>(Run.TextProperty.GetMetadata(typeof(Run)));
        Assert.True(metadata.BindsTwoWayByDefault);
    }

    [Fact]
    public void UnmodedRunBinding_ToReadOnlyCollectionCount_ThrowsDuringAttach()
    {
        RunOnStaThread(() =>
        {
            var (textBlock, _) = CreateRunTextBlock(new Binding("Count"));
            var source = new System.Collections.ObjectModel.ObservableCollection<string> { "a", "b" };
            textBlock.DataContext = source;

            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
                () => AttachAndLayout(textBlock));
            Assert.Contains("read-only property", failure.Message);
        });
    }

    [Fact]
    public void ExplicitOneWayRunBinding_AttachesRendersCount_AndTracksCollectionChanges()
    {
        RunOnStaThread(() =>
        {
            var source = new System.Collections.ObjectModel.ObservableCollection<string> { "a", "b" };
            var (textBlock, run) = CreateRunTextBlock(new Binding("Count") { Mode = BindingMode.OneWay });
            textBlock.DataContext = source;

            AttachAndLayout(textBlock);
            Assert.Equal("2", run.Text);

            source.Add("c");
            textBlock.Dispatcher.DoEvents();
            AttachAndLayout(textBlock);
            Assert.Equal("3", run.Text);
        });
    }

    [Fact]
    public void LauncherXaml_EveryDataBoundRunText_DeclaresModeOneWay()
    {
        string launcherXaml = File.ReadAllText(
            Path.Combine(FindRepoRoot(), "Views", "MainWindow.xaml"));
        var violations = new List<string>();
        foreach (Match run in Regex.Matches(launcherXaml, "<Run\\b[^>]*>", RegexOptions.CultureInvariant))
        {
            string element = run.Value;
            Match binding = Regex.Match(element, "Text=\"\\{Binding ([^\"}]*)\"");
            if (!binding.Success)
                continue;
            bool explicitOneWay = Regex.IsMatch(
                element, "Mode=OneWay\\b", RegexOptions.CultureInvariant);
            if (!explicitOneWay)
                violations.Add($"{element} binds '{binding.Groups[1].Value}' without Mode=OneWay");
        }

        Assert.True(
            violations.Count == 0,
            "Run.Text bindings default to TwoWay and crash on read-only sources at "
            + "Window.Show(); every data-bound Run must declare Mode=OneWay. Violations:\n"
            + string.Join("\n", violations));
    }

    private static (TextBlock Host, Run Bound) CreateRunTextBlock(Binding binding)
    {
        var textBlock = new TextBlock();
        var run = new Run();
        run.SetBinding(Run.TextProperty, binding);
        textBlock.Inlines.Add(run);
        return (textBlock, run);
    }

    /// <summary>
    /// Forces the binding engine to attach exactly like the production crash
    /// path (ContextLayoutManager.UpdateLayout -> DataBindEngine task): a
    /// measure pass over a hosted visual followed by a dispatcher drain.
    /// </summary>
    private static void AttachAndLayout(TextBlock textBlock)
    {
        if (textBlock.Parent is Border previous)
            previous.Child = null;
        var host = new Border { Child = textBlock };
        host.Measure(new Size(200, 40));
        host.Arrange(new Rect(0, 0, 200, 40));
        textBlock.Dispatcher.DoEvents();
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        using var entered = new ManualResetEventSlim(false);
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                entered.Set();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(30)), "STA work did not finish");
        if (failure is not null)
            throw failure;
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TabDock.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("TabDock.sln not found above test output directory.");
    }
}

internal static class LauncherStartupBindingTestDispatcherExtensions
{
    /// <summary>Nests frames until the dispatcher queue is empty at idle.</summary>
    public static void DoEvents(this System.Windows.Threading.Dispatcher dispatcher)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        Action callback = () => frame.Continue = false;
        _ = dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            callback);
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
}
