using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TabDock.ValidationDriver;

/// <summary>Native-free half-open rectangle used by the topology laboratory.</summary>
internal readonly record struct LabRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Math.Max(0, Right - Left);
    public int Height => Math.Max(0, Bottom - Top);
    public bool IsValid => Right > Left && Bottom > Top;
    public bool Contains(LabRect other)
        => other.Left >= Left && other.Top >= Top && other.Right <= Right && other.Bottom <= Bottom;
    public bool ContainsPoint(int x, int y)
        => x >= Left && x < Right && y >= Top && y < Bottom;
}

internal sealed record LabMonitor(
    string Id,
    LabRect Bounds,
    LabRect WorkArea,
    int EffectiveDpi);

internal sealed record LabTopology(
    string Name,
    string PrimaryMonitorId,
    IReadOnlyList<LabMonitor> Monitors)
{
    public LabMonitor Primary
        => Monitors.Single(monitor => string.Equals(monitor.Id, PrimaryMonitorId, StringComparison.Ordinal));

    public LabRect VirtualBounds
        => new(
            Monitors.Min(monitor => monitor.Bounds.Left),
            Monitors.Min(monitor => monitor.Bounds.Top),
            Monitors.Max(monitor => monitor.Bounds.Right),
            Monitors.Max(monitor => monitor.Bounds.Bottom));

    public bool HasMixedDpi
        => Monitors.Select(monitor => monitor.EffectiveDpi).Distinct().Count() > 1;

    public bool HasNegativeCoordinates
        => VirtualBounds.Left < 0 || VirtualBounds.Top < 0;

    public bool HasNegativeXMonitor
        => Monitors.Any(monitor => monitor.Bounds.Left < 0);

    public bool HasAboveOriginMonitor
        => Monitors.Any(monitor => monitor.Bounds.Top < 0);

    public bool HasStaggeredPlacement
    {
        get
        {
            for (int i = 0; i < Monitors.Count; i++)
            {
                for (int j = i + 1; j < Monitors.Count; j++)
                {
                    LabRect left = Monitors[i].Bounds;
                    LabRect right = Monitors[j].Bounds;
                    bool horizontallyAdjacent = left.Right == right.Left || right.Right == left.Left;
                    bool verticallyAdjacent = left.Bottom == right.Top || right.Bottom == left.Top;
                    if ((horizontallyAdjacent && left.Top != right.Top)
                        || (verticallyAdjacent && left.Left != right.Left))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }

    public bool HasAsymmetricWorkAreas
        => Monitors.Select(monitor => monitor.WorkArea).Distinct().Count() > 1
            && Monitors.Any(monitor => monitor.Bounds != monitor.WorkArea);

    public IReadOnlyList<int> DpiValues
        => Monitors.Select(monitor => monitor.EffectiveDpi).Distinct().OrderBy(value => value).ToArray();

    public bool HasDpiPair(int sourceDpi, int destinationDpi)
        => Monitors.Any(monitor => monitor.EffectiveDpi == sourceDpi)
            && Monitors.Any(monitor => monitor.EffectiveDpi == destinationDpi);

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Name))
            errors.Add("topology name is empty");
        if (Monitors.Count == 0)
            errors.Add("topology has no monitors");
        if (!Monitors.Any(monitor => string.Equals(monitor.Id, PrimaryMonitorId, StringComparison.Ordinal)))
            errors.Add("primary monitor is not present");
        if (Monitors.Select(monitor => monitor.Id).Distinct(StringComparer.Ordinal).Count() != Monitors.Count)
            errors.Add("monitor IDs are not unique");
        foreach (LabMonitor monitor in Monitors)
        {
            if (!monitor.Bounds.IsValid || !monitor.WorkArea.IsValid)
                errors.Add($"monitor {monitor.Id} has a non-positive rectangle");
            if (!monitor.Bounds.Contains(monitor.WorkArea))
                errors.Add($"monitor {monitor.Id} work area leaves its bounds");
            if (monitor.EffectiveDpi <= 0)
                errors.Add($"monitor {monitor.Id} has invalid effective DPI");
        }
        for (int i = 0; i < Monitors.Count; i++)
        {
            for (int j = i + 1; j < Monitors.Count; j++)
            {
                if (IntersectionArea(Monitors[i].Bounds, Monitors[j].Bounds) != 0)
                    errors.Add($"monitors {Monitors[i].Id} and {Monitors[j].Id} overlap");
            }
        }
        return errors;
    }

    public static int IntersectionArea(LabRect left, LabRect right)
    {
        int width = Math.Max(0, Math.Min(left.Right, right.Right) - Math.Max(left.Left, right.Left));
        int height = Math.Max(0, Math.Min(left.Bottom, right.Bottom) - Math.Max(left.Top, right.Top));
        return checked(width * height);
    }
}


/// <summary>
/// Pure placement and topology-transition math. It deliberately has no
/// dependency on monitor enumeration, HWNDs, DPI APIs, or WPF.
/// </summary>
internal static class VirtualTopologyPolicy
{
    public static LabRect Contain(LabRect desired, LabRect workArea)
    {
        if (!workArea.IsValid)
            return workArea;
        int width = Math.Min(Math.Max(0, desired.Width), workArea.Width);
        int height = Math.Min(Math.Max(0, desired.Height), workArea.Height);
        int left = Math.Clamp(desired.Left, workArea.Left, workArea.Right - width);
        int top = Math.Clamp(desired.Top, workArea.Top, workArea.Bottom - height);
        return new LabRect(left, top, left + width, top + height);
    }

    public static (LabRect Left, LabRect Right) Partition(LabRect content)
    {
        int leftWidth = content.Width / 2;
        LabRect left = new(content.Left, content.Top, content.Left + leftWidth, content.Bottom);
        LabRect right = new(content.Left + leftWidth, content.Top, content.Right, content.Bottom);
        return (left, right);
    }

    public static LabRect Project(LabRect rectangle, LabRect fromWorkArea, LabRect toWorkArea)
    {
        if (!fromWorkArea.IsValid || !toWorkArea.IsValid)
            return Contain(rectangle, toWorkArea);
        int width = Math.Min(rectangle.Width, toWorkArea.Width);
        int height = Math.Min(rectangle.Height, toWorkArea.Height);
        double xRatio = (rectangle.Left - fromWorkArea.Left)
            / (double)Math.Max(1, fromWorkArea.Width - rectangle.Width);
        double yRatio = (rectangle.Top - fromWorkArea.Top)
            / (double)Math.Max(1, fromWorkArea.Height - rectangle.Height);
        int left = toWorkArea.Left + (int)Math.Round(
            Math.Clamp(xRatio, 0, 1) * Math.Max(0, toWorkArea.Width - width),
            MidpointRounding.AwayFromZero);
        int top = toWorkArea.Top + (int)Math.Round(
            Math.Clamp(yRatio, 0, 1) * Math.Max(0, toWorkArea.Height - height),
            MidpointRounding.AwayFromZero);
        return Contain(new LabRect(left, top, left + width, top + height), toWorkArea);
    }

    public static LabRect TranslateAndClamp(LabRect rectangle, int deltaX, int deltaY, LabRect workArea)
        => Contain(
            new LabRect(
                rectangle.Left + deltaX,
                rectangle.Top + deltaY,
                rectangle.Right + deltaX,
                rectangle.Bottom + deltaY),
            workArea);

    public static LabRect RestoreAfterTransition(
        LabRect rectangle,
        LabTopology oldTopology,
        LabTopology newTopology,
        string? preferredMonitorId = null)
    {
        LabMonitor oldMonitor = FindMonitorForRectangle(rectangle, oldTopology) ?? oldTopology.Primary;
        LabMonitor newMonitor = preferredMonitorId == null
            ? (newTopology.Monitors.FirstOrDefault(monitor => monitor.Id == oldMonitor.Id) ?? newTopology.Primary)
            : (newTopology.Monitors.FirstOrDefault(monitor => monitor.Id == preferredMonitorId) ?? newTopology.Primary);
        return Project(rectangle, oldMonitor.WorkArea, newMonitor.WorkArea);
    }

    public static LabMonitor? FindMonitorForRectangle(LabRect rectangle, LabTopology topology)
    {
        long x = (long)rectangle.Left + Math.Max(0, rectangle.Width / 2);
        long y = (long)rectangle.Top + Math.Max(0, rectangle.Height / 2);
        if (x < int.MinValue || x > int.MaxValue || y < int.MinValue || y > int.MaxValue)
            return null;
        return FindMonitorForPoint((int)x, (int)y, topology);
    }

    public static LabMonitor? FindMonitorForPoint(int x, int y, LabTopology topology)
        => topology.Monitors.FirstOrDefault(monitor => monitor.WorkArea.ContainsPoint(x, y));

    public static LabRect CenterTitle(LabRect container, int measuredTitleWidth)
    {
        int width = Math.Clamp(measuredTitleWidth, 0, container.Width);
        int left = container.Left + (container.Width - width) / 2;
        return new LabRect(left, container.Top, left + width, container.Top + 1);
    }

    public static double TitleCenterError(LabRect container, LabRect title)
        => Math.Abs(
            ((long)title.Left + title.Right) / 2.0
            - ((long)container.Left + container.Right) / 2.0);

    public static int ScalePixels(int pixels, int sourceDpi, int destinationDpi)
    {
        if (pixels < 0)
            throw new ArgumentOutOfRangeException(nameof(pixels));
        if (sourceDpi <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceDpi));
        if (destinationDpi <= 0)
            throw new ArgumentOutOfRangeException(nameof(destinationDpi));
        return checked((int)Math.Round(
            pixels * (double)destinationDpi / sourceDpi,
            MidpointRounding.AwayFromZero));
    }
}

internal sealed record VirtualTopologyLabTransition(
    string Name,
    string SourceMonitorId,
    string DestinationMonitorId,
    int SourceDpi,
    int DestinationDpi,
    bool Passed,
    int AssertionCount,
    string? Failure);

internal sealed record VirtualTopologyLabCase(
    string Name,
    int MonitorCount,
    int[] DpiValues,
    bool NegativeCoordinates,
    bool AboveOrigin,
    bool StaggeredPlacement,
    bool AsymmetricWorkAreas,
    bool Passed,
    int AssertionCount,
    int TitleAssertionCount,
    string[] RelativePlacements,
    string? Failure);

internal sealed record VirtualTopologyLabReport(
    int SchemaVersion,
    string Generation,
    bool SyntheticTopology,
    int Seed,
    bool Passed,
    int AssertionCount,
    string NormalizedSha256,
    IReadOnlyList<VirtualTopologyLabCase> Cases,
    IReadOnlyList<VirtualTopologyLabTransition> DpiTransitions);

internal static class VirtualTopologyLab
{
    public const int SchemaVersion = 2;
    public const string Generation = "virtual-topology-lab-2026-08-24-v2";
    public const int Seed = 20260824;

    public static IReadOnlyList<(int SourceDpi, int DestinationDpi)> RequiredDpiTransitions { get; } =
        new[]
        {
            (96, 144), (144, 96),
            (96, 168), (168, 96),
            (96, 192), (192, 96),
            (120, 144), (144, 120),
            (120, 168), (168, 120),
            (120, 192), (192, 120),
        };

    private static readonly (string Name, int Width)[] TitleInputs =
    {
        ("short", 48),
        ("medium", 240),
        ("long", 640),
    };

    private static readonly (string Name, int Width)[] WindowWidthClasses =
    {
        ("narrow", 320),
        ("default", 960),
        ("wide", 1600),
    };

    public static VirtualTopologyLabReport Run()
    {
        LabTopology[] topologies = FixedTopologies().ToArray();
        var cases = new List<VirtualTopologyLabCase>(topologies.Length + 1);
        foreach (LabTopology topology in topologies)
            cases.Add(EvaluateTopology(topology));

        LabTopology transitionMatrix = topologies.Single(
            topology => string.Equals(topology.Name, "mixed-100-125-150-175-200", StringComparison.Ordinal));
        VirtualTopologyLabTransition[] dpiTransitions = RequiredDpiTransitions
            .Select(pair => EvaluateDpiTransition(transitionMatrix, pair.SourceDpi, pair.DestinationDpi))
            .ToArray();

        int randomAssertions = 0;
        string? randomFailure = null;
        var random = new Random(Seed);
        for (int iteration = 0; iteration < 256; iteration++)
        {
            LabTopology topology = topologies[random.Next(topologies.Length)];
            LabMonitor monitor = topology.Monitors[random.Next(topology.Monitors.Count)];
            int width = random.Next(1, Math.Max(2, monitor.WorkArea.Width + 1));
            int height = random.Next(1, Math.Max(2, monitor.WorkArea.Height + 1));
            int desiredLeft = monitor.WorkArea.Left - random.Next(0, 2000);
            int desiredTop = monitor.WorkArea.Top - random.Next(0, 2000);
            LabRect desired = new(
                desiredLeft,
                desiredTop,
                desiredLeft + width,
                desiredTop + height);
            LabRect contained = VirtualTopologyPolicy.Contain(desired, monitor.WorkArea);
            randomAssertions += 5;
            if (!monitor.WorkArea.Contains(contained))
            {
                randomFailure = $"containment failed at iteration {iteration} seed {Seed}";
                break;
            }
            (LabRect left, LabRect right) = VirtualTopologyPolicy.Partition(monitor.WorkArea);
            if (left.Right != right.Left
                || left.Left != monitor.WorkArea.Left
                || right.Right != monitor.WorkArea.Right)
            {
                randomFailure = $"partition failed at iteration {iteration} seed {Seed}";
                break;
            }
            if (VirtualTopologyPolicy.FindMonitorForRectangle(contained, topology) != monitor)
            {
                randomFailure = $"monitor identity failed at iteration {iteration} seed {Seed}";
                break;
            }
            LabRect title = VirtualTopologyPolicy.CenterTitle(
                contained,
                Math.Min(120, Math.Max(1, contained.Width)));
            if (!contained.Contains(title) || VirtualTopologyPolicy.TitleCenterError(contained, title) > 0.5)
            {
                randomFailure = $"title centering failed at iteration {iteration} seed {Seed}";
                break;
            }
            LabTopology nextTopology = topologies[(iteration + 1) % topologies.Length];
            LabRect projected = VirtualTopologyPolicy.RestoreAfterTransition(contained, topology, nextTopology);
            if (!nextTopology.Monitors.Any(candidate => candidate.WorkArea.Contains(projected)))
            {
                randomFailure = $"transition projection failed at iteration {iteration} seed {Seed}";
                break;
            }
        }

        cases.Add(new VirtualTopologyLabCase(
            "seeded-transition-stress",
            randomFailure == null ? topologies.Length : 0,
            randomFailure == null
                ? topologies.SelectMany(topology => topology.Monitors.Select(monitor => monitor.EffectiveDpi))
                    .Distinct().OrderBy(value => value).ToArray()
                : Array.Empty<int>(),
            randomFailure == null && topologies.Any(topology => topology.HasNegativeCoordinates),
            randomFailure == null && topologies.Any(topology => topology.HasAboveOriginMonitor),
            randomFailure == null && topologies.Any(topology => topology.HasStaggeredPlacement),
            randomFailure == null && topologies.Any(topology => topology.HasAsymmetricWorkAreas),
            randomFailure == null,
            randomAssertions,
            0,
            Array.Empty<string>(),
            randomFailure));

        string normalized = JsonSerializer.Serialize(new
        {
            generation = Generation,
            seed = Seed,
            syntheticTopology = true,
            cases,
            dpiTransitions,
        });
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
        return new VirtualTopologyLabReport(
            SchemaVersion,
            Generation,
            true,
            Seed,
            cases.All(item => item.Passed) && dpiTransitions.All(item => item.Passed),
            cases.Sum(item => item.AssertionCount) + dpiTransitions.Sum(item => item.AssertionCount),
            hash,
            cases,
            dpiTransitions);
    }

    public static IReadOnlyList<LabTopology> FixedTopologies()
    {
        return new[]
        {
            new LabTopology("single-96", "primary", new[]
            {
                new LabMonitor("primary", new LabRect(0, 0, 1920, 1080), new LabRect(0, 0, 1920, 1040), 96),
            }),
            new LabTopology("dual-horizontal", "left", new[]
            {
                new LabMonitor("left", new LabRect(0, 0, 1920, 1080), new LabRect(0, 0, 1920, 1040), 96),
                new LabMonitor("right", new LabRect(1920, 0, 3840, 1080), new LabRect(1920, 0, 3840, 1080), 96),
            }),
            new LabTopology("dual-vertical", "top", new[]
            {
                new LabMonitor("top", new LabRect(0, 0, 1920, 1080), new LabRect(0, 0, 1920, 1040), 96),
                new LabMonitor("bottom", new LabRect(0, 1080, 1920, 2160), new LabRect(0, 1080, 1920, 2160), 120),
            }),
            new LabTopology("negative-left", "right", new[]
            {
                new LabMonitor("left", new LabRect(-1920, 0, 0, 1080), new LabRect(-1920, 0, 0, 1080), 144),
                new LabMonitor("right", new LabRect(0, 0, 1920, 1080), new LabRect(0, 0, 1920, 1040), 96),
            }),
            new LabTopology("above-origin", "lower", new[]
            {
                new LabMonitor("upper", new LabRect(0, -1200, 1920, 0), new LabRect(0, -1200, 1920, 0), 192),
                new LabMonitor("lower", new LabRect(0, 0, 1920, 1080), new LabRect(0, 0, 1920, 1040), 96),
            }),
            new LabTopology("staggered-work-areas", "primary", new[]
            {
                new LabMonitor("primary", new LabRect(0, 0, 1920, 1080), new LabRect(0, 0, 1920, 1040), 120),
                new LabMonitor("staggered", new LabRect(1920, 120, 3840, 1200), new LabRect(1920, 120, 3840, 1160), 144),
            }),
            new LabTopology("asymmetric-work-areas", "primary", new[]
            {
                new LabMonitor("primary", new LabRect(-1600, -200, 1600, 1600), new LabRect(-1580, -180, 1580, 1520), 120),
                new LabMonitor("side", new LabRect(1600, 100, 2880, 1123), new LabRect(1600, 100, 2880, 1090), 144),
            }),
            new LabTopology("mixed-100-125-150-175-200", "m1", new[]
            {
                new LabMonitor("m1", new LabRect(-2560, 0, -640, 1440), new LabRect(-2560, 0, -640, 1400), 96),
                new LabMonitor("m2", new LabRect(-640, 0, 1280, 1440), new LabRect(-640, 0, 1280, 1400), 120),
                new LabMonitor("m3", new LabRect(1280, 0, 3200, 1440), new LabRect(1280, 0, 3200, 1380), 144),
                new LabMonitor("m4", new LabRect(3200, 0, 5120, 1440), new LabRect(3200, 0, 5120, 1400), 168),
                new LabMonitor("m5", new LabRect(5120, -400, 7040, 1040), new LabRect(5120, -400, 7040, 1000), 192),
            }),
            new LabTopology("odd-width", "odd", new[]
            {
                new LabMonitor("odd", new LabRect(0, 0, 1919, 1079), new LabRect(0, 0, 1919, 1037), 96),
            }),
            new LabTopology("narrow-work-area", "narrow", new[]
            {
                new LabMonitor("narrow", new LabRect(0, 0, 320, 180), new LabRect(0, 0, 320, 120), 96),
            }),
            new LabTopology("large-coordinates", "large", new[]
            {
                new LabMonitor("large", new LabRect(1_000_000, -900_000, 1_003_840, -897_840), new LabRect(1_000_000, -900_000, 1_003_840, -898_000), 144),
            }),
            TransitionTopology(),
        };
    }

    private static LabTopology TransitionTopology()
        => new("removal-reorder-transition", "new-primary", new[]
        {
            new LabMonitor("new-primary", new LabRect(0, 0, 1600, 900), new LabRect(0, 0, 1600, 860), 96),
            new LabMonitor("survivor", new LabRect(1600, 80, 2880, 900), new LabRect(1600, 80, 2880, 860), 144),
        });

    private static VirtualTopologyLabCase EvaluateTopology(LabTopology topology)
    {
        var errors = new List<string>(topology.Validate());
        int assertions = 1;
        if (!topology.Primary.Bounds.Contains(topology.Primary.WorkArea))
            errors.Add("primary work area is not contained");
        assertions++;

        foreach (LabMonitor monitor in topology.Monitors)
        {
            LabRect desired = new(
                monitor.WorkArea.Left - 400,
                monitor.WorkArea.Top - 300,
                monitor.WorkArea.Right + 400,
                monitor.WorkArea.Bottom + 300);
            LabRect contained = VirtualTopologyPolicy.Contain(desired, monitor.WorkArea);
            if (!monitor.WorkArea.Contains(contained))
                errors.Add($"containment escaped {monitor.Id}");
            assertions++;

            (LabRect left, LabRect right) = VirtualTopologyPolicy.Partition(monitor.WorkArea);
            if (!monitor.WorkArea.Contains(left)
                || !monitor.WorkArea.Contains(right)
                || left.Right != right.Left
                || left.Left != monitor.WorkArea.Left
                || right.Right != monitor.WorkArea.Right
                || left.Top != right.Top
                || left.Bottom != right.Bottom)
            {
                errors.Add($"partition invariant failed {monitor.Id}");
            }
            assertions++;

            LabRect dragged = VirtualTopologyPolicy.TranslateAndClamp(contained, 9999, -9999, monitor.WorkArea);
            LabRect reverseDragged = VirtualTopologyPolicy.TranslateAndClamp(contained, -9999, 9999, monitor.WorkArea);
            if (!monitor.WorkArea.Contains(dragged) || !monitor.WorkArea.Contains(reverseDragged))
                errors.Add($"drag clamp escaped {monitor.Id}");
            assertions++;

            if (VirtualTopologyPolicy.FindMonitorForRectangle(contained, topology) != monitor)
                errors.Add($"monitor identity escaped {monitor.Id}");
            assertions++;
        }

        int titleAssertions = 0;
        foreach (LabMonitor monitor in topology.Monitors)
        {
            foreach ((string widthClass, int width) in WindowWidthClasses)
            {
                int containerWidth = Math.Min(width, monitor.WorkArea.Width);
                LabRect container = new(
                    monitor.WorkArea.Left,
                    monitor.WorkArea.Top,
                    monitor.WorkArea.Left + containerWidth,
                    monitor.WorkArea.Top + Math.Min(600, monitor.WorkArea.Height));
                foreach ((string titleClass, int measuredWidth) in TitleInputs)
                {
                    int visibleWidth = Math.Min(measuredWidth, Math.Max(1, container.Width - 8));
                    LabRect title = VirtualTopologyPolicy.CenterTitle(container, visibleWidth);
                    if (!container.Contains(title) || VirtualTopologyPolicy.TitleCenterError(container, title) > 0.5)
                        errors.Add($"title centering escaped {topology.Name}/{monitor.Id}/{widthClass}/{titleClass}");
                    titleAssertions++;
                }
            }
        }
        assertions += titleAssertions;

        LabTopology transition = TransitionTopology();
        LabRect restored = VirtualTopologyPolicy.RestoreAfterTransition(
            new LabRect(
                topology.Primary.WorkArea.Left + 10,
                topology.Primary.WorkArea.Top + 10,
                topology.Primary.WorkArea.Left + Math.Min(400, topology.Primary.WorkArea.Width),
                topology.Primary.WorkArea.Top + Math.Min(300, topology.Primary.WorkArea.Height)),
            topology,
            transition);
        if (!transition.Primary.WorkArea.Contains(restored))
            errors.Add("monitor removal/reorder restore escaped new primary");
        assertions++;

        if (string.Equals(topology.Name, "staggered-work-areas", StringComparison.Ordinal)
            && !topology.HasStaggeredPlacement)
        {
            errors.Add("staggered topology was not classified as staggered");
        }
        if (string.Equals(topology.Name, "asymmetric-work-areas", StringComparison.Ordinal)
            && !topology.HasAsymmetricWorkAreas)
        {
            errors.Add("asymmetric topology was not classified as asymmetric");
        }
        assertions += 2;

        return new VirtualTopologyLabCase(
            topology.Name,
            topology.Monitors.Count,
            topology.DpiValues.ToArray(),
            topology.HasNegativeCoordinates,
            topology.HasAboveOriginMonitor,
            topology.HasStaggeredPlacement,
            topology.HasAsymmetricWorkAreas,
            errors.Count == 0,
            assertions,
            titleAssertions,
            RelativePlacements(topology),
            errors.Count == 0 ? null : string.Join("; ", errors));
    }

    private static VirtualTopologyLabTransition EvaluateDpiTransition(
        LabTopology topology,
        int sourceDpi,
        int destinationDpi)
    {
        var errors = new List<string>();
        LabMonitor? source = topology.Monitors.FirstOrDefault(monitor => monitor.EffectiveDpi == sourceDpi);
        LabMonitor? destination = topology.Monitors.FirstOrDefault(monitor => monitor.EffectiveDpi == destinationDpi);
        int assertions = 0;
        assertions++;
        if (source == null)
            errors.Add($"source DPI {sourceDpi} is absent");
        assertions++;
        if (destination == null)
            errors.Add($"destination DPI {destinationDpi} is absent");
        if (source != null && destination != null)
        {
            LabRect sourceRect = new(
                source.WorkArea.Left + 10,
                source.WorkArea.Top + 10,
                source.WorkArea.Left + Math.Min(640, source.WorkArea.Width),
                source.WorkArea.Top + Math.Min(360, source.WorkArea.Height));
            LabRect projected = VirtualTopologyPolicy.Project(
                sourceRect,
                source.WorkArea,
                destination.WorkArea);
            assertions++;
            if (!destination.WorkArea.Contains(projected))
                errors.Add("projected rectangle escaped destination work area");

            int scaled = VirtualTopologyPolicy.ScalePixels(320, sourceDpi, destinationDpi);
            int roundTrip = VirtualTopologyPolicy.ScalePixels(scaled, destinationDpi, sourceDpi);
            assertions++;
            if (Math.Abs(roundTrip - 320) > 1)
                errors.Add($"DPI round trip drifted: {sourceDpi}->{destinationDpi}->{sourceDpi}");

            LabRect title = VirtualTopologyPolicy.CenterTitle(projected, Math.Min(240, projected.Width));
            assertions++;
            if (VirtualTopologyPolicy.TitleCenterError(projected, title) > 0.5)
                errors.Add("title inputs were not centered after DPI projection");
        }

        return new VirtualTopologyLabTransition(
            $"dpi-{sourceDpi}-to-{destinationDpi}",
            source?.Id ?? "unavailable",
            destination?.Id ?? "unavailable",
            sourceDpi,
            destinationDpi,
            errors.Count == 0,
            assertions,
            errors.Count == 0 ? null : string.Join("; ", errors));
    }

    private static string[] RelativePlacements(LabTopology topology)
    {
        LabMonitor primary = topology.Primary;
        return topology.Monitors
            .Select(monitor =>
            {
                if (monitor.Id == primary.Id)
                    return $"{monitor.Id}:primary";
                var labels = new List<string>();
                if (monitor.Bounds.Right <= primary.Bounds.Left)
                    labels.Add("left");
                if (monitor.Bounds.Left >= primary.Bounds.Right)
                    labels.Add("right");
                if (monitor.Bounds.Bottom <= primary.Bounds.Top)
                    labels.Add("above");
                if (monitor.Bounds.Top >= primary.Bounds.Bottom)
                    labels.Add("below");
                if (monitor.Bounds.Top != primary.Bounds.Top
                    && monitor.Bounds.Left != primary.Bounds.Left)
                {
                    labels.Add("staggered");
                }
                if (labels.Count == 0)
                    labels.Add("overlapping-axis");
                return $"{monitor.Id}:{string.Join("+", labels)}";
            })
            .ToArray();
    }
}
