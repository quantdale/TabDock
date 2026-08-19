using System.Text.Json;
using TabDock.Models;
using Xunit;

namespace TabDock.UnitTests;

/// <summary>
/// Headless regression coverage for the persistence DTOs. The real-input
/// ValidationDriver exercises these through the live persist-kill scenario, but
/// a deterministic serialize/deserialize round-trip guards the wire contract
/// (field names, version, and the source-generated JsonContext) without a
/// desktop or any input.
/// </summary>
public class PersistenceTests
{
    [Fact]
    public void PersistedState_VersionConstantIsCurrent()
    {
        Assert.Equal(2, PersistedState.CurrentVersion);
    }

    [Fact]
    public void HiddenWindowJournal_VersionConstantIsCurrent()
    {
        Assert.Equal(3, HiddenWindowJournalFile.CurrentVersion);
        Assert.Equal(1, HiddenWindowJournalFile.LegacyMinimalVersion);
        Assert.Equal(2, HiddenWindowJournalFile.PresentationIdentityVersion);
    }

    [Fact]
    public void PersistedState_RoundTripsThroughJsonContext()
    {
        var state = new PersistedState
        {
            Version = PersistedState.CurrentVersion,
            Groups =
            {
                new PersistedGroup
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    Name = "Work",
                    AccentColor = "#2196F3",
                    ActiveIndex = 1,
                    Tabs =
                    {
                        new PersistedTab
                        {
                            ExePath = @"C:\Windows\notepad.exe",
                            OriginalTitle = "Untitled",
                            CustomLabel = "Notes",
                            Left = 10, Top = 20, Right = 730, Bottom = 540,
                            WasMaximized = false,
                        },
                        new PersistedTab
                        {
                            ExePath = @"C:\App\app.exe",
                            WasMaximized = true,
                        },
                    },
                },
            },
        };

        string json = JsonSerializer.Serialize(state, TabDockJsonContext.Default.PersistedState);
        var restored = JsonSerializer.Deserialize(json, TabDockJsonContext.Default.PersistedState);

        Assert.NotNull(restored);
        Assert.Equal(PersistedState.CurrentVersion, restored!.Version);
        Assert.Single(restored.Groups);

        var group = restored.Groups[0];
        Assert.Equal("Work", group.Name);
        Assert.Equal("#2196F3", group.AccentColor);
        Assert.Equal(1, group.ActiveIndex);
        Assert.Equal(2, group.Tabs.Count);

        Assert.Equal(@"C:\Windows\notepad.exe", group.Tabs[0].ExePath);
        Assert.Equal("Untitled", group.Tabs[0].OriginalTitle);
        Assert.Equal("Notes", group.Tabs[0].CustomLabel);
        Assert.Equal(10, group.Tabs[0].Left);
        Assert.Equal(540, group.Tabs[0].Bottom);
        Assert.False(group.Tabs[0].WasMaximized);

        Assert.True(group.Tabs[1].WasMaximized);
    }

    [Fact]
    public void PersistedState_DeserializesExpectedWireShape()
    {
        const string json = """
        {
          "version": 2,
          "groups": [
            {
              "id": "22222222-2222-2222-2222-222222222222",
              "name": "Reading",
              "accentColor": "#E91E63",
              "activeIndex": 0,
              "tabs": []
            }
          ]
        }
        """;

        var restored = JsonSerializer.Deserialize(json, TabDockJsonContext.Default.PersistedState);
        Assert.NotNull(restored);
        Assert.Equal(2, restored!.Version);
        Assert.Single(restored.Groups);
        Assert.Equal("Reading", restored.Groups[0].Name);
        Assert.Equal("#E91E63", restored.Groups[0].AccentColor);
    }

    [Fact]
    public void HiddenWindowJournal_RoundTrips()
    {
        var journal = new HiddenWindowJournalFile
        {
            Version = HiddenWindowJournalFile.CurrentVersion,
            Entries =
            {
                new HiddenWindowEntry
                {
                    Hwnd = 0x1234,
                    Pid = 99,
                    ExePath = @"C:\App\app.exe",
                    ClassName = "CabinetWClass",
                    DoNotRescue = true,
                },
            },
        };

        string json = JsonSerializer.Serialize(journal, TabDockJsonContext.Default.HiddenWindowJournalFile);
        var restored = JsonSerializer.Deserialize(json, TabDockJsonContext.Default.HiddenWindowJournalFile);

        Assert.NotNull(restored);
        Assert.Equal(HiddenWindowJournalFile.CurrentVersion, restored!.Version);
        Assert.Single(restored.Entries);
        Assert.Equal(0x1234, restored.Entries[0].Hwnd);
        Assert.Equal(99u, restored.Entries[0].Pid);
        Assert.True(restored.Entries[0].DoNotRescue);
    }
}
