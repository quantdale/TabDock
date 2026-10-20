using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using TabDock.Services;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Serializes every test that redirects Console or uses the scoped
/// ProductStorage override against real default-path consumers. xUnit runs
/// DisableParallelization collections without concurrent unrelated
/// collections, so no unrelated test can interleave with fixture globals.
/// </summary>
[CollectionDefinition("ValidationStorageGlobal", DisableParallelization = true)]
public sealed class ValidationStorageGlobalCollection
{
}

/// <summary>
/// A02 validation-storage isolation coverage: paired bootstrap inputs select
/// a verified run-owned product directory or fail closed — invalid overrides
/// (including explicitly provided blank/whitespace values) never fall back to
/// the production profile, and alternate Win32 spellings of production are
/// rejected as the same identity. Every default storage role resolves beneath
/// the one effective root, and the read-only storage probe reports proof
/// without mutating anything. Global state is scoped (async-context override)
/// and restored with reliable teardown.
/// </summary>
[Collection("ValidationStorageGlobal")]
public sealed class ValidationStorageRootTests : IDisposable
{
    private readonly string _root;
    private readonly string _productDir;
    private readonly string _productionDir;
    private readonly string _candidatePath;
    private IDisposable? _storageScope;
    private const string RunId = "run-2026-10-20-fixture";
    private const string ProductDirPlaceholder = "<product-dir>";

    public ValidationStorageRootTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "TabDock-validation-root-" + Guid.NewGuid().ToString("N"));
        _productDir = Path.Combine(_root, "product");
        _productionDir = Path.Combine(_root, "production-profile");
        Directory.CreateDirectory(_productDir);
        Directory.CreateDirectory(_productionDir);
        _candidatePath = Path.Combine(_root, "TabDock-candidate.exe");
    }

    public void Dispose()
    {
        // Reliable teardown: the scoped override is restored even if a test
        // failed mid-assertion, so no later consumer observes fixture state.
        _storageScope?.Dispose();
        _storageScope = null;
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch { }
    }

    private ProductStorage UseScope(ProductStorage storage)
    {
        _storageScope?.Dispose();
        _storageScope = ProductStorage.OverrideForTests(storage);
        return storage;
    }

    private static Dictionary<string, (byte[] Bytes, DateTime LastWriteUtc)> Snapshot(string dir)
        => Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(dir, path),
                path => (File.ReadAllBytes(path), File.GetLastWriteTimeUtc(path)),
                StringComparer.OrdinalIgnoreCase);

    private static void AssertSnapshotUnchanged(
        Dictionary<string, (byte[] Bytes, DateTime LastWriteUtc)> before,
        string dir)
    {
        Dictionary<string, (byte[] Bytes, DateTime LastWriteUtc)> after = Snapshot(dir);
        Assert.Equal(
            before.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase),
            after.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
        foreach ((string name, (byte[] bytes, DateTime lastWriteUtc)) in before)
        {
            Assert.Equal(bytes, after[name].Bytes);
            Assert.Equal(lastWriteUtc, after[name].LastWriteUtc);
        }
    }

    private void WriteMarker()
        => ProductStorage.WriteRunMarker(_productDir, RunId, _candidatePath);

    private ProductStorage Resolve(string? root, string? runId, Func<string, FileAttributes>? getAttributes = null)
        => ProductStorage.Resolve(root, runId, _candidatePath, _productionDir, getAttributes);

    // ---- Task 1.2: resolver/probe refusal fixtures ----

    [Fact]
    public void WithoutValidationInputs_DefaultsToProductionProfile()
    {
        ProductStorage storage = Resolve(null, null);

        Assert.True(storage.IsUsable);
        Assert.False(storage.IsValidationRoot);
        Assert.Null(storage.RunId);
        Assert.Equal(_productionDir, storage.EffectiveDirectory);
    }

    [Theory]
    [InlineData("only-root")]
    [InlineData("only-run-id")]
    public void IncompletePairedInputs_AreRefusedWithoutProductionFallback(string shape)
    {
        string? root = shape == "only-root" ? _productDir : null;
        string? runId = shape == "only-run-id" ? RunId : null;

        ProductStorage storage = Resolve(root, runId);

        Assert.False(storage.IsUsable);
        Assert.Equal("incomplete-paired-inputs", storage.RefusalReason);
        Assert.Throws<InvalidOperationException>(() => storage.EffectiveDirectory);
    }

    /// <summary>
    /// S1: explicitly provided blank/whitespace values are invalid
    /// configuration. They must be refused; collapsing them into "absent"
    /// silently selected the production profile.
    /// </summary>
    [Theory]
    [InlineData("", "")]
    [InlineData(" ", " ")]
    [InlineData("\t", " ")]
    [InlineData("  ", RunId)]
    [InlineData(" ", RunId)]
    [InlineData("", RunId)]
    [InlineData(ProductDirPlaceholder, " ")]
    [InlineData(ProductDirPlaceholder, "")]
    [InlineData(ProductDirPlaceholder, "\t")]
    public void ExplicitWhitespaceInputs_AreRefusedWithoutProductionFallback(string root, string runId)
    {
        string effectiveRoot = root == ProductDirPlaceholder ? _productDir : root;

        ProductStorage storage = Resolve(effectiveRoot, runId);

        Assert.False(storage.IsUsable);
        Assert.Equal("incomplete-paired-inputs", storage.RefusalReason);
    }

    /// <summary>
    /// S1: an extended-path spelling of the production directory is the same
    /// directory, not a distinct root. Unsupported device/extended namespaces
    /// are refused outright so no alternate Win32 spelling can bypass the
    /// production-overlap exclusion.
    /// </summary>
    [Fact]
    public void ExtendedNamespaceAliasOfProduction_IsRefusedWithoutProductionFallback()
    {
        // The reviewer's fixture shape: a MATCHING marker whose ProductRoot is
        // spelled with the same extended prefix, so a lexical resolver accepts
        // the alias as self-consistent. Refusal must come from namespace/
        // identity handling, not marker mismatch.
        string alias = @"\\?\" + Path.GetFullPath(_productionDir);
        WriteMarkerFor(_productionDir, productRoot: alias);

        ProductStorage storage = Resolve(alias, RunId);

        Assert.False(storage.IsUsable);
        Assert.Equal("root-not-canonical", storage.RefusalReason);
    }

    [Theory]
    [InlineData("\\\\.\\")]
    [InlineData("//?/")]
    public void DeviceNamespaceAliasesOfProduction_AreRefusedWithoutProductionFallback(string prefix)
    {
        string alias = prefix + Path.GetFullPath(_productionDir).Replace('\\', prefix == "//?/" ? '/' : '\\');
        WriteMarkerFor(_productionDir, productRoot: alias);

        ProductStorage storage = Resolve(alias, RunId);

        Assert.False(storage.IsUsable);
        Assert.Equal("root-not-canonical", storage.RefusalReason);
    }

    /// <summary>
    /// S1: an 8.3 short-name spelling of production converges on the same
    /// verified directory identity and must be refused as production overlap.
    /// Volumes with 8.3 generation disabled cannot construct the alias; the
    /// fixture then verifies only that resolution stays fail-closed.
    /// </summary>
    [Fact]
    public void ShortNameAliasOfProduction_IsRefusedWhenVolumeExposesShortNames()
    {
        WriteMarkerFor(_productionDir);
        string? shortForm = GetShortPathOrNull(_productionDir);
        if (shortForm == null)
        {
            // No short names exist on this volume; the alias is unconstructible.
            Assert.Null(GetShortPathOrNull(_productDir));
            return;
        }

        ProductStorage storage = Resolve(shortForm, RunId);

        // The contract is fail-closed refusal: whichever check catches the
        // alias first (canonical-form unification or identity overlap), the
        // short-name spelling of production must never be accepted.
        Assert.False(storage.IsUsable);
        Assert.Contains(storage.RefusalReason!, new[] { "root-not-canonical", "production-overlap" });
    }

    [Fact]
    public void RelativeRoot_IsRefused()
    {
        ProductStorage storage = Resolve(Path.Combine("relative", "product"), RunId);
        Assert.Equal("root-not-absolute", storage.RefusalReason);
    }

    [Fact]
    public void NonCanonicalRoot_IsRefused()
    {
        ProductStorage storage = Resolve(_productDir + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + "product", RunId);
        Assert.Equal("root-not-canonical", storage.RefusalReason);
    }

    [Fact]
    public void UnavailableRoot_IsRefused()
    {
        ProductStorage storage = Resolve(Path.Combine(_root, "missing-product"), RunId);
        Assert.Equal("root-unavailable", storage.RefusalReason);
    }

    [Theory]
    [InlineData("same")]
    [InlineData("inside-production")]
    [InlineData("contains-production")]
    public void ProductionOverlapOrAlias_IsRefused(string shape)
    {
        string root = shape switch
        {
            "same" => _productionDir,
            "inside-production" => Path.Combine(_productionDir, "inner"),
            _ => _root,
        };
        WriteMarkerFor(root);

        ProductStorage storage = Resolve(root, RunId);

        Assert.Equal("production-overlap", storage.RefusalReason);
    }

    [Fact]
    public void ReparsePointComponent_IsRefused()
    {
        WriteMarker();
        static FileAttributes WithReparse(string path)
            => path.EndsWith("product", StringComparison.OrdinalIgnoreCase)
                ? FileAttributes.Directory | FileAttributes.ReparsePoint
                : FileAttributes.Directory;

        ProductStorage storage = Resolve(_productDir, RunId, WithReparse);

        Assert.Equal("reparse-point-component", storage.RefusalReason);
    }

    [Fact]
    public void MissingMarker_IsRefused()
    {
        ProductStorage storage = Resolve(_productDir, RunId);
        Assert.Equal("marker-missing", storage.RefusalReason);
    }

    [Fact]
    public void MalformedMarker_IsRefused()
    {
        File.WriteAllText(Path.Combine(_productDir, ProductStorage.MarkerFileName), "not-json");
        ProductStorage storage = Resolve(_productDir, RunId);
        Assert.Equal("marker-malformed", storage.RefusalReason);
    }

    [Fact]
    public void UnsupportedMarkerSchema_IsRefused()
    {
        WriteMarkerFor(_productDir, schemaVersion: ProductStorage.MarkerSchemaVersion + 1, runId: RunId);
        ProductStorage storage = Resolve(_productDir, RunId);
        Assert.Equal("marker-schema-unsupported", storage.RefusalReason);
    }

    [Fact]
    public void StaleRunId_IsRefused()
    {
        WriteMarkerFor(_productDir, runId: "a-different-run");
        ProductStorage storage = Resolve(_productDir, RunId);
        Assert.Equal("marker-run-id-mismatch", storage.RefusalReason);
    }

    [Fact]
    public void MarkerRootMismatch_IsRefused()
    {
        File.WriteAllText(
            Path.Combine(_productDir, ProductStorage.MarkerFileName),
            "{\"SchemaVersion\":" + ProductStorage.MarkerSchemaVersion
            + ",\"RunId\":\"" + RunId + "\",\"ProductRoot\":\"C:\\\\elsewhere\",\"CandidatePath\":\""
            + _candidatePath.Replace("\\", "\\\\") + "\"}");
        ProductStorage storage = Resolve(_productDir, RunId);
        Assert.Equal("marker-root-mismatch", storage.RefusalReason);
    }

    [Fact]
    public void MarkerCandidateMismatch_IsRefused()
    {
        WriteMarkerFor(_productDir, runId: RunId, candidatePath: Path.Combine(_root, "other-candidate.exe"));
        ProductStorage storage = Resolve(_productDir, RunId);
        Assert.Equal("marker-candidate-mismatch", storage.RefusalReason);
    }

    [Fact]
    public void VerifiedRootRunCandidatePair_IsAccepted()
    {
        WriteMarker();
        ProductStorage storage = Resolve(_productDir, RunId);

        Assert.True(storage.IsUsable);
        Assert.True(storage.IsValidationRoot);
        Assert.Equal(RunId, storage.RunId);
        Assert.Equal(Path.GetFullPath(_productDir), storage.EffectiveDirectory);
    }

    [Fact]
    public void MarkerRecord_BindsRootRunAndCandidateIdentity()
    {
        WriteMarker();
        string markerJson = File.ReadAllText(Path.Combine(_productDir, ProductStorage.MarkerFileName));
        System.Text.Json.Nodes.JsonNode marker = System.Text.Json.Nodes.JsonNode.Parse(markerJson)!;

        Assert.Equal(ProductStorage.MarkerSchemaVersion, marker["SchemaVersion"]!.GetValue<int>());
        Assert.Equal(RunId, marker["RunId"]!.GetValue<string>());
        Assert.Equal(Path.GetFullPath(_productDir), marker["ProductRoot"]!.GetValue<string>(), ignoreCase: true);
        Assert.Equal(Path.GetFullPath(_candidatePath), marker["CandidatePath"]!.GetValue<string>(), ignoreCase: true);
    }

    // ---- Task 2.3: read-only probe ----

    [Fact]
    public void Probe_EmitsMachineReadableProofWithoutMutatingAnything()
    {
        WriteMarker();
        File.WriteAllText(Path.Combine(_productDir, "sentinel.txt"), "evidence");
        ProductStorage storage = UseScope(Resolve(_productDir, RunId));
        var before = Snapshot(_root);

        string output = RunProbe(out int exitCode);

        Assert.Equal(0, exitCode);
        System.Text.Json.Nodes.JsonNode proof = System.Text.Json.Nodes.JsonNode.Parse(output)!;
        Assert.Equal("ok", proof["Status"]!.GetValue<string>());
        Assert.Equal(Path.GetFullPath(_productDir), proof["EffectiveRoot"]!.GetValue<string>(), ignoreCase: true);
        Assert.Equal(RunId, proof["RunId"]!.GetValue<string>());
        Assert.Contains("diagnostic-inspection", output, StringComparison.Ordinal);
        AssertSnapshotUnchanged(before, _root);
    }

    [Fact]
    public void Probe_ReportsRefusalAndExitNonzero_WithoutProductionFallback()
    {
        UseScope(Resolve(_productDir, "stale-run"));
        var before = Snapshot(_root);

        string output = RunProbe(out int exitCode);

        Assert.Equal(4, exitCode);
        System.Text.Json.Nodes.JsonNode proof = System.Text.Json.Nodes.JsonNode.Parse(output)!;
        Assert.Equal("refused", proof["Status"]!.GetValue<string>());
        Assert.Equal("marker-missing", proof["Reason"]!.GetValue<string>());
        AssertSnapshotUnchanged(before, _root);
    }

    // ---- Tasks 3.1-3.2: every default storage role routes through the root ----

    [Fact]
    public void DefaultStorageRoles_ResolveUnderTheVerifiedRoot()
    {
        WriteMarker();
        UseScope(Resolve(_productDir, RunId));
        var productionBefore = Snapshot(_productionDir);

        var log = new LoggingService();
        var persistence = new PersistenceService(log);
        var shepherd = new WindowShepherdService(log);

        string productRoot = Path.GetFullPath(_productDir);
        Assert.Equal(Path.Combine(productRoot, "state.json"), Path.GetFullPath(persistence.StatePath));
        Assert.Equal(
            Path.Combine(productRoot, "hidden-windows.json"),
            Path.GetFullPath(shepherd.JournalPath));

        // Default pending discovery targets the same root (read-only here).
        PendingRecoveryCatalog catalog = PendingRecoveryService.Discover();
        Assert.Equal(productRoot, Path.GetFullPath(catalog.Directory));
        Assert.Null(catalog.Error);

        // Default diagnostics inspection reads the same root and stays read-only.
        var productBeforeInspection = Snapshot(_productDir);
        TabDock.Models.PersistenceSnapshot snapshot = DiagnosticEnvironmentService.InspectPersistence();
        Assert.Equal("absent", snapshot.StateStatus);
        Assert.Equal("absent", snapshot.JournalStatus);
        AssertSnapshotUnchanged(productBeforeInspection, _productDir);

        // A default-path log line and save land inside the run-owned root only.
        log.Log("validation-storage routing fixture");
        string logPath = Path.Combine(productRoot, "logs", "TabDock.log");
        Assert.True(WaitForFile(logPath), "default log did not appear under the verified root");
        persistence.Save(Array.Empty<TabDock.Models.Group>());
        Assert.True(File.Exists(Path.Combine(productRoot, "state.json")));
        AssertSnapshotUnchanged(productionBefore, _productionDir);
        log.Dispose();
    }

    private static bool WaitForFile(string path)
    {
        for (int i = 0; i < 200; i++)
        {
            if (File.Exists(path))
                return true;
            Thread.Sleep(10);
        }
        return false;
    }

    [Fact]
    public void RefusedStorage_BlocksDefaultConsumersWithoutProductionFallback()
    {
        UseScope(Resolve(_productDir, "stale-run"));

        Assert.Throws<InvalidOperationException>(() => new LoggingService());
        Assert.Throws<InvalidOperationException>(() => new PersistenceService(new LoggingService(_root)));
    }

    /// <summary>
    /// S6 isolation regression: the reproduced hazard was an unrelated test
    /// class (RuntimeStabilizationTests) observing this fixture's refused root
    /// through a default-path consumer. The consumer below runs on its own
    /// execution context, started before the override exists, exactly like an
    /// unrelated parallel test: it must never observe the scoped fixture root.
    /// </summary>
    [Fact]
    public void ScopedOverride_IsInvisibleToUnrelatedDefaultPathConsumers()
    {
        Exception? consumerFailure = null;
        ProductStorage? consumerStorage = null;
        string? consumerJournal = null;
        using var go = new ManualResetEventSlim(false);

        // Started BEFORE any override exists: this thread's execution context
        // can never see the scoped value, mirroring an unrelated test class.
        var consumer = new Thread(() =>
        {
            go.Wait(TimeSpan.FromSeconds(10));
            try
            {
                consumerStorage = ProductStorage.Current;
                using var log = new LoggingService(Path.Combine(_root, "consumer-logs"));
                // The exact default-path consumer the reproduced hazard used.
                consumerJournal = new WindowShepherdService(log).JournalPath;
            }
            catch (Exception ex)
            {
                consumerFailure = ex;
            }
        });
        consumer.Start();
        try
        {
            ProductStorage refused = UseScope(Resolve(_productDir, "stale-run"));
            go.Set();

            // The override is visible in ITS OWN context only.
            Assert.True(ReferenceEquals(ProductStorage.Current, refused));

            Assert.True(consumer.Join(TimeSpan.FromSeconds(20)), "unrelated consumer did not finish");
            Assert.Null(consumerFailure);
            Assert.NotNull(consumerStorage);
            Assert.False(ReferenceEquals(consumerStorage, refused));
            Assert.NotNull(consumerJournal);
            Assert.False(
                string.Equals(
                    Path.GetFullPath(_productDir),
                    Path.GetFullPath(consumerJournal!),
                    StringComparison.OrdinalIgnoreCase),
                "an unrelated default-path consumer must never observe the scoped fixture root");
        }
        finally
        {
            go.Set();
            consumer.Join(TimeSpan.FromSeconds(20));
        }
    }

    // ---- Task 3.3: no default role escapes to a separate known-folder calculation ----

    [Fact]
    public void StorageRoleConsumers_DoNotRecomputeKnownFolderPaths()
    {
        string repoRoot = FindRepoRoot();
        string[] roleFiles =
        {
            Path.Combine("Services", "LoggingService.cs"),
            Path.Combine("Services", "PersistenceService.cs"),
            Path.Combine("Services", "WindowShepherdService.cs"),
            Path.Combine("Services", "PendingRecoveryService.cs"),
            Path.Combine("App.xaml.cs"),
        };
        foreach (string roleFile in roleFiles)
        {
            string text = File.ReadAllText(Path.Combine(repoRoot, roleFile));
            Assert.DoesNotContain(
                "SpecialFolder.ApplicationData",
                text,
                StringComparison.Ordinal);
        }

        // The single authority is the resolver itself.
        Assert.Contains(
            "SpecialFolder.ApplicationData",
            File.ReadAllText(Path.Combine(repoRoot, "Services", "ProductStorage.cs")),
            StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "TabDock.sln")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return dir!;
    }

    private void WriteMarkerFor(string root, int schemaVersion = ProductStorage.MarkerSchemaVersion, string? runId = null, string? candidatePath = null, string? productRoot = null)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(
            Path.Combine(root, ProductStorage.MarkerFileName),
            "{\"SchemaVersion\":" + schemaVersion
            + ",\"RunId\":\"" + (runId ?? RunId) + "\""
            + ",\"ProductRoot\":\"" + (productRoot ?? root).Replace("\\", "\\\\") + "\""
            + ",\"CandidatePath\":\"" + (candidatePath ?? _candidatePath).Replace("\\", "\\\\") + "\"}");
    }

    private static string? GetShortPathOrNull(string path)
    {
        StringBuilder buffer = new(1024);
        uint length = NativeMethods.GetShortPathName(path, buffer, (uint)buffer.Capacity);
        if (length == 0 || length >= buffer.Capacity)
            return null;
        string shortPath = buffer.ToString();
        return string.Equals(shortPath, path, StringComparison.OrdinalIgnoreCase)
            ? null
            : shortPath;
    }

    private static string RunProbe(out int exitCode)
    {
        TextWriter original = Console.Out;
        var capture = new StringWriter();
        Console.SetOut(capture);
        try
        {
            exitCode = DiagnosticCommandLine.Run(new DiagnosticCommandRequest
            {
                Kind = DiagnosticCommandKind.ValidationStorageProbe,
            });
        }
        finally
        {
            Console.SetOut(original);
        }
        return capture.ToString();
    }
}
