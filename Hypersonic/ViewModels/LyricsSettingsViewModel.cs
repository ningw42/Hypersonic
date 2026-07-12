using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Hypersonic.Contracts.Services;

namespace Hypersonic.ViewModels;

/// <summary>
/// Drives the "Lyrics" settings section. Enabled providers are chips (priority order) bound to the tokenizing box;
/// disabled providers are the autosuggest candidate pool. Enable/disable/reorder each route through an explicit
/// intent that commits to <see cref="ILyricsPreferences"/>.
/// </summary>
public partial class LyricsSettingsViewModel : ObservableObject
{
    private const double Epsilon = 0.001;

    private readonly ILyricsPreferences _preferences;
    private readonly List<LyricsProviderItem> _candidates = []; // disabled providers (source for suggestions)
    private bool _initializing = true;
    private bool _mutating;

    public ObservableCollection<LyricsProviderItem> EnabledProviders { get; } = [];
    public ObservableCollection<LyricsProviderItem> ProviderSuggestions { get; } = [];

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

    public bool CanRemove => EnabledProviders.Count > 1;
    public string AutoLoadText => $"{AutoLoadSeconds:0.0} s";
    public string ManualRefreshText => $"{ManualRefreshSeconds:0.0} s";

    public bool CanResetAutoLoad => Math.Abs(AutoLoadSeconds - _preferences.DefaultAutoLoadBudget.TotalSeconds) > Epsilon;
    public bool CanResetManualRefresh => Math.Abs(ManualRefreshSeconds - _preferences.DefaultManualRefreshBudget.TotalSeconds) > Epsilon;

    public bool CanResetProviders =>
        !EnabledProviders.Select(p => p.Id).SequenceEqual(_preferences.DefaultEnabledProviderIds);

    public ICommand ResetProvidersCommand { get; }
    public ICommand ResetAutoLoadCommand { get; }
    public ICommand ResetManualRefreshCommand { get; }

    public LyricsSettingsViewModel(ILyricsPreferences preferences)
    {
        _preferences = preferences;
        EnabledProviders.CollectionChanged += OnEnabledProvidersChanged;

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
            RebuildFromSnapshot();
            AutoLoadSeconds = _preferences.AutoLoadBudget.TotalSeconds;
            ManualRefreshSeconds = _preferences.ManualRefreshBudget.TotalSeconds;
        }
        finally
        {
            _initializing = false;
        }

        IsInitialized = true;
    }

    /// <summary>Exact-name (ordinal) match against the current disabled candidates — resolves typed token text
    /// into the stable provider item, or null to reject unknown/already-enabled text.</summary>
    public LyricsProviderItem? ResolveCandidate(string text)
        => _candidates.FirstOrDefault(c => string.Equals(c.DisplayName, text, StringComparison.Ordinal));

    /// <summary>Repopulates the visible suggestion list from the disabled candidates, filtered by query.</summary>
    public void FilterSuggestions(string? query)
    {
        ProviderSuggestions.Clear();
        var matches = string.IsNullOrWhiteSpace(query)
            ? _candidates
            : _candidates.Where(c => c.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        foreach (var candidate in matches)
        {
            ProviderSuggestions.Add(candidate);
        }
    }

    public void EnableProvider(LyricsProviderItem item)
    {
        // Normalize to the end so the visual order matches the service's append-to-end (a token can commit mid-strip).
        if (EnabledProviders.Count == 0 || !ReferenceEquals(EnabledProviders[^1], item))
        {
            if (EnabledProviders.Remove(item))
            {
                EnabledProviders.Add(item);
            }
        }

        _candidates.Remove(item);
        _preferences.SetEnabled(item.Id, true);
        FilterSuggestions(query: null);
        RaiseListDerivedChanged();
    }

    public void DisableProvider(LyricsProviderItem item)
    {
        if (!_candidates.Contains(item))
        {
            _candidates.Add(item);
        }

        _preferences.SetEnabled(item.Id, false);
        FilterSuggestions(query: null);
        RaiseListDerivedChanged();
    }

    public void CommitReorder(IReadOnlyList<LyricsProviderItem> orderedItems)
    {
        _preferences.Reorder(orderedItems.Select(i => i.Id).ToList());
        RaiseListDerivedChanged();
    }

    private void ResetProviders()
    {
        _preferences.ResetProviders();
        _initializing = true;
        try
        {
            RebuildFromSnapshot();
        }
        finally
        {
            _initializing = false;
        }
    }

    private void RebuildFromSnapshot()
    {
        _mutating = true;
        try
        {
            EnabledProviders.Clear();
            _candidates.Clear();

            foreach (var view in _preferences.GetProviders())
            {
                var item = new LyricsProviderItem(view.Id, view.DisplayName);
                if (view.Enabled)
                {
                    EnabledProviders.Add(item);
                }
                else
                {
                    _candidates.Add(item);
                }
            }
        }
        finally
        {
            _mutating = false;
        }

        FilterSuggestions(query: null);
        RaiseListDerivedChanged();
    }

    // EnabledProviders is mutated by TTB (add/remove) and by the reorder Drop. We only refresh derived UI state
    // here; persistence is committed by the explicit intents (Enable/Disable/CommitReorder), never here.
    private void OnEnabledProvidersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_initializing || _mutating)
        {
            return;
        }

        RaiseListDerivedChanged();
    }

    private void RaiseListDerivedChanged()
    {
        OnPropertyChanged(nameof(CanRemove));
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
