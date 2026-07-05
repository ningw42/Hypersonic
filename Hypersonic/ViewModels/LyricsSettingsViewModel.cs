using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Hypersonic.Contracts.Services;

namespace Hypersonic.ViewModels;

/// <summary>
/// Drives the "Lyrics" settings section. Two collections model the enabled (reorderable) and disabled
/// (toggle-only) groups; all state changes flow through <see cref="ILyricsPreferences"/>.
/// </summary>
public partial class LyricsSettingsViewModel : ObservableObject
{
    private const double Epsilon = 0.001;

    private readonly ILyricsPreferences _preferences;
    private bool _initializing = true;
    private bool _mutating;

    public ObservableCollection<LyricsProviderItem> EnabledProviders { get; } = [];
    public ObservableCollection<LyricsProviderItem> DisabledProviders { get; } = [];

    [ObservableProperty]
    public partial bool IsInitialized { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutoLoadText))]
    [NotifyPropertyChangedFor(nameof(CanResetAutoLoad))]
    public partial double AutoLoadSeconds { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ManualRefreshText))]
    [NotifyPropertyChangedFor(nameof(CanResetManualRefresh))]
    public partial double ManualRefreshSeconds { get; set; }

    public bool ShowDisabledDivider => DisabledProviders.Count > 0;
    public string AutoLoadText => $"{AutoLoadSeconds:0.0} s";
    public string ManualRefreshText => $"{ManualRefreshSeconds:0.0} s";

    public bool CanResetAutoLoad => Math.Abs(AutoLoadSeconds - _preferences.DefaultAutoLoadBudget.TotalSeconds) > Epsilon;
    public bool CanResetManualRefresh => Math.Abs(ManualRefreshSeconds - _preferences.DefaultManualRefreshBudget.TotalSeconds) > Epsilon;

    public bool CanResetProviders =>
        DisabledProviders.Count > 0 ||
        !EnabledProviders.Select(p => p.Id).SequenceEqual(_preferences.DefaultProviderOrder);

    public ICommand ResetProvidersCommand { get; }
    public ICommand ResetAutoLoadCommand { get; }
    public ICommand ResetManualRefreshCommand { get; }

    public LyricsSettingsViewModel(ILyricsPreferences preferences)
    {
        _preferences = preferences;
        EnabledProviders.CollectionChanged += OnEnabledCollectionChanged;

        ResetProvidersCommand = new RelayCommand(ResetProviders, () => CanResetProviders);
        ResetAutoLoadCommand = new RelayCommand(() => AutoLoadSeconds = _preferences.DefaultAutoLoadBudget.TotalSeconds, () => CanResetAutoLoad);
        ResetManualRefreshCommand = new RelayCommand(() => ManualRefreshSeconds = _preferences.DefaultManualRefreshBudget.TotalSeconds, () => CanResetManualRefresh);
    }

    public async Task InitializeAsync()
    {
        await _preferences.EnsureLoadedAsync();

        _initializing = true;
        try
        {
            RebuildCollections();
            AutoLoadSeconds = _preferences.AutoLoadBudget.TotalSeconds;
            ManualRefreshSeconds = _preferences.ManualRefreshBudget.TotalSeconds;
        }
        finally
        {
            _initializing = false;
        }

        IsInitialized = true;
    }

    private void RebuildCollections()
    {
        _mutating = true;
        try
        {
            foreach (var item in EnabledProviders)
            {
                item.PropertyChanged -= OnItemPropertyChanged;
            }

            foreach (var item in DisabledProviders)
            {
                item.PropertyChanged -= OnItemPropertyChanged;
            }

            EnabledProviders.Clear();
            DisabledProviders.Clear();

            foreach (var view in _preferences.GetProviders())
            {
                var item = new LyricsProviderItem(view.Id, view.DisplayName, view.Enabled);
                item.PropertyChanged += OnItemPropertyChanged;
                (view.Enabled ? EnabledProviders : DisabledProviders).Add(item);
            }
        }
        finally
        {
            _mutating = false;
        }

        RecomputeToggleLocks();
        RaiseListDerivedChanged();
    }

    private void OnItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_initializing || _mutating || sender is not LyricsProviderItem item || e.PropertyName != nameof(LyricsProviderItem.IsEnabled))
        {
            return;
        }

        _mutating = true;
        try
        {
            if (!item.IsEnabled)
            {
                // Guard: never disable the last enabled provider (defensive; the checkbox is also locked).
                if (EnabledProviders.Count <= 1)
                {
                    item.IsEnabled = true;
                    return;
                }

                EnabledProviders.Remove(item);
                DisabledProviders.Add(item); // sink to the bottom
            }
            else
            {
                DisabledProviders.Remove(item);
                EnabledProviders.Add(item); // end of enabled run
            }

            _preferences.SetEnabled(item.Id, item.IsEnabled);
        }
        finally
        {
            _mutating = false;
        }

        RecomputeToggleLocks();
        RaiseListDerivedChanged();
    }

    private void OnEnabledCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_initializing || _mutating)
        {
            return;
        }

        // A drag-reorder inside the enabled ListView — persist the new priority order.
        _preferences.Reorder(EnabledProviders.Select(p => p.Id).ToArray());
        RecomputeToggleLocks();
        RaiseListDerivedChanged();
    }

    private void ResetProviders()
    {
        _preferences.ResetProviders();
        RebuildCollections();
    }

    private void RecomputeToggleLocks()
    {
        var canDisable = EnabledProviders.Count > 1;
        foreach (var item in EnabledProviders)
        {
            item.CanToggle = canDisable;
        }

        foreach (var item in DisabledProviders)
        {
            item.CanToggle = true;
        }
    }

    private void RaiseListDerivedChanged()
    {
        OnPropertyChanged(nameof(ShowDisabledDivider));
        OnPropertyChanged(nameof(CanResetProviders));
        (ResetProvidersCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    partial void OnAutoLoadSecondsChanged(double value)
    {
        (ResetAutoLoadCommand as RelayCommand)?.NotifyCanExecuteChanged();
        if (_initializing)
        {
            return;
        }

        _preferences.SetAutoLoadBudget(TimeSpan.FromSeconds(value));
    }

    partial void OnManualRefreshSecondsChanged(double value)
    {
        (ResetManualRefreshCommand as RelayCommand)?.NotifyCanExecuteChanged();
        if (_initializing)
        {
            return;
        }

        _preferences.SetManualRefreshBudget(TimeSpan.FromSeconds(value));
    }
}
