using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using TabDock.Models;
using TabDock.Services;

namespace TabDock.ViewModels;

/// <summary>
/// View-model for the application's main launcher window.
/// Exposes the list of groups and commands to create/capture/exit.
/// </summary>
public sealed class MainViewModel : ViewModelBase
{
    private readonly GroupManager _manager;
    private Group? _selectedGroup;

    public ObservableCollection<Group> Groups => _manager.Groups;

    public Group? SelectedGroup
    {
        get => _selectedGroup;
        set => SetProperty(ref _selectedGroup, value);
    }

    public ICommand NewGroupCommand { get; }
    public ICommand CaptureCommand { get; }
    public ICommand ExitCommand { get; }
    public ICommand OpenSelectedGroupCommand { get; }

    /// <summary>
    /// Whether the global Ctrl+Alt+G hotkey registered successfully at
    /// startup. Set once by App before the launcher is shown; when false the
    /// Capture button must not advertise the shortcut (another process owns
    /// it). The button itself stays the always-available fallback.
    /// </summary>
    public bool GlobalHotkeyAvailable { get; set; } = true;

    public string CaptureButtonText => GlobalHotkeyAvailable
        ? "Capture windows (Ctrl+Alt+G)"
        : "Capture windows (shortcut unavailable)";

    public event EventHandler? NewGroupRequested;
    public event EventHandler? CaptureRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler<Group>? OpenGroupRequested;

    public MainViewModel(GroupManager manager)
    {
        _manager = manager;

        NewGroupCommand = new RelayCommand(_ => NewGroupRequested?.Invoke(this, EventArgs.Empty));
        CaptureCommand = new RelayCommand(_ => CaptureRequested?.Invoke(this, EventArgs.Empty));
        ExitCommand = new RelayCommand(_ => ExitRequested?.Invoke(this, EventArgs.Empty));
        OpenSelectedGroupCommand = new RelayCommand(parameter =>
        {
            if (parameter is Group group)
                OpenGroupRequested?.Invoke(this, group);
            else if (SelectedGroup != null)
                OpenGroupRequested?.Invoke(this, SelectedGroup);
        });


    }
}
