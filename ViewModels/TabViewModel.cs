using System;
using System.Windows.Input;
using System.Windows.Media;
using TabDock.Models;
using TabDock.Services;

namespace TabDock.ViewModels;

public sealed class TabViewModel : ViewModelBase
{
    private bool _isActive;
    private ImageSource? _icon;

    public CapturedWindow Model { get; }

    public string Title => Model.DisplayLabel;

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public ImageSource? Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value);
    }

    public ICommand PopOutCommand { get; }
    public ICommand CloseWindowCommand { get; }

    public event EventHandler<TabViewModel>? PopOutRequested;
    public event EventHandler<TabViewModel>? CloseWindowRequested;

    public TabViewModel(CapturedWindow model)
    {
        Model = model;
        PopOutCommand = new RelayCommand(_ => PopOutRequested?.Invoke(this, this));
        CloseWindowCommand = new RelayCommand(_ => CloseWindowRequested?.Invoke(this, this));
    }

    public void RefreshTitle()
    {
        OnPropertyChanged(nameof(Title));
    }
}
