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
    private bool _globalHotkeyAvailable = true;
    private bool _globalTabNavigationHotkeysAvailable = true;
    private PendingRecoveryAttention _pendingRecoveryAttention;

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
    public bool GlobalHotkeyAvailable
    {
        get => _globalHotkeyAvailable;
        set
        {
            if (SetProperty(ref _globalHotkeyAvailable, value))
            {
                OnPropertyChanged(nameof(CaptureButtonText));
                OnPropertyChanged(nameof(CaptureButtonToolTip));
            }
        }
    }

    /// <summary>
    /// Whether both focus-independent previous/next registrations are
    /// available. This is intentionally separate from capture admission and
    /// from the Ctrl+Alt+G registration state.
    /// </summary>
    public bool GlobalTabNavigationHotkeysAvailable
    {
        get => _globalTabNavigationHotkeysAvailable;
        set
        {
            if (SetProperty(ref _globalTabNavigationHotkeysAvailable, value))
                OnPropertyChanged(nameof(TabNavigationAvailabilityText));
        }
    }

    public bool CaptureAllowed => _manager.CaptureAllowed;
    public string CaptureAdmissionReason => _manager.CaptureAdmissionReason;
    public bool CaptureAdmissionBlocked => !CaptureAllowed;

    public int PendingRecoveryCount => _pendingRecoveryAttention.PendingFileCount;
    public bool HasPendingRecoveryAttention => _pendingRecoveryAttention.HasAttention;
    public string PendingRecoveryBannerText => _pendingRecoveryAttention.SummaryText;

    public string CaptureButtonText
        => !CaptureAllowed
            ? "Capture unavailable"
            : GlobalHotkeyAvailable
                ? "Capture windows (Ctrl+Alt+G)"
                : "Capture windows (shortcut unavailable)";

    public string CaptureButtonToolTip
        => !CaptureAllowed
            ? $"Capture is unavailable: {CaptureAdmissionReason}"
            : GlobalHotkeyAvailable
                ? "Open the capture picker. The global shortcut is Ctrl+Alt+G."
                : "The global capture shortcut is unavailable; use this button to capture windows.";

    public string TabNavigationAvailabilityText
        => GlobalTabNavigationHotkeysAvailable
            ? "Global tab navigation: Ctrl+Alt+PageUp / Ctrl+Alt+PageDown"
            : "Global tab navigation is unavailable; local Ctrl+Tab remains available.";

    public event EventHandler? NewGroupRequested;
    public event EventHandler? CaptureRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler<Group>? OpenGroupRequested;

    public MainViewModel(GroupManager manager)
    {
        _manager = manager;
        _manager.CaptureAdmissionChanged += Manager_CaptureAdmissionChanged;

        NewGroupCommand = new RelayCommand(_ => NewGroupRequested?.Invoke(this, EventArgs.Empty));
        CaptureCommand = new RelayCommand(
            _ => CaptureRequested?.Invoke(this, EventArgs.Empty),
            _ => _manager.CaptureAllowed);
        ExitCommand = new RelayCommand(_ => ExitRequested?.Invoke(this, EventArgs.Empty));
        OpenSelectedGroupCommand = new RelayCommand(parameter =>
        {
            if (parameter is Group group)
                OpenGroupRequested?.Invoke(this, group);
            else if (SelectedGroup != null)
                OpenGroupRequested?.Invoke(this, SelectedGroup);
        });


    }

    /// <summary>Projects the startup read-only pending-recovery catalog into the launcher.</summary>
    internal void SetPendingRecoveryAttention(PendingRecoveryAttention attention)
    {
        if (_pendingRecoveryAttention == attention)
            return;
        _pendingRecoveryAttention = attention;
        OnPropertyChanged(nameof(PendingRecoveryCount));
        OnPropertyChanged(nameof(HasPendingRecoveryAttention));
        OnPropertyChanged(nameof(PendingRecoveryBannerText));
    }

    private void Manager_CaptureAdmissionChanged(object? sender, CaptureAdmissionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(CaptureAllowed));
        OnPropertyChanged(nameof(CaptureAdmissionReason));
        OnPropertyChanged(nameof(CaptureAdmissionBlocked));
        OnPropertyChanged(nameof(CaptureButtonText));
        OnPropertyChanged(nameof(CaptureButtonToolTip));
        ((RelayCommand)CaptureCommand).RaiseCanExecuteChanged();
    }
}
