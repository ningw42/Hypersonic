using System.Collections.ObjectModel;

using CommunityToolkit.WinUI.Controls;

using Hypersonic.Contracts.Services;
using Hypersonic.Helpers;
using Hypersonic.ViewModels;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppLifecycle;

namespace Hypersonic.Views;

// TODO: Set the URL for your privacy policy by updating SettingsPage_PrivacyTermsLink.NavigateUri in Resources.resw.
public sealed partial class SettingsPage : Page
{
    private readonly ISystemFontCatalog _fontCatalog;

    public SettingsViewModel ViewModel
    {
        get;
    }

    public LyricsSettingsViewModel LyricsViewModel
    {
        get;
    }

    /// <summary>
    /// Filtered list of font families shown in the UI font picker's autosuggest
    /// dropdown. Updated on each <see cref="TokenizingTextBox.TextChanged"/>.
    /// </summary>
    public ObservableCollection<string> UiFontSuggestions { get; } = [];

    /// <summary>
    /// Filtered list of font families shown in the lyrics font picker's
    /// autosuggest dropdown.
    /// </summary>
    public ObservableCollection<string> LyricsFontSuggestions { get; } = [];

    public SettingsPage()
    {
        ViewModel = App.GetService<SettingsViewModel>();
        LyricsViewModel = App.GetService<LyricsSettingsViewModel>();
        _fontCatalog = App.GetService<ISystemFontCatalog>();
        InitializeComponent();

        // Pre-populate the dropdowns with the first batch of suggestions so the
        // picker shows useful entries the moment the user clicks into it.
        SeedSuggestions(UiFontSuggestions);
        SeedSuggestions(LyricsFontSuggestions);

        // Tooltips and automation names for the Reset buttons. Set in
        // code-behind because both targets are attached properties and we want
        // a single localized string shared across all three buttons.
        ApplyResetButtonAccessibility(ResetUiFontsButton);
        ApplyResetButtonAccessibility(ResetLyricsFontsButton);
        ApplyResetButtonAccessibility(ResetUiScaleButton);
        ApplyResetButtonAccessibility(ResetLyricsProvidersButton);
        ApplyResetButtonAccessibility(ResetLyricsAutoLoadButton);
        ApplyResetButtonAccessibility(ResetLyricsManualRefreshButton);

        _ = LyricsViewModel.InitializeAsync();
    }

    private static void ApplyResetButtonAccessibility(Button button)
    {
        var label = "Settings_ResetTooltip".GetLocalized();
        ToolTipService.SetToolTip(button, label);
        AutomationProperties.SetName(button, label);
    }

    private void SeedSuggestions(ObservableCollection<string> target)
    {
        target.Clear();
        foreach (var name in _fontCatalog.Suggest(query: null))
        {
            target.Add(name);
        }
    }

    private void OnUiFontTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        UpdateSuggestions(sender, args, UiFontSuggestions);
    }

    private void OnLyricsFontTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        UpdateSuggestions(sender, args, LyricsFontSuggestions);
    }

    private void UpdateSuggestions(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args, ObservableCollection<string> target)
    {
        // Only refilter when the user typed; ignore programmatic changes (e.g.
        // a token was just added and the inner text was cleared) and selection
        // commits to avoid jittering the dropdown.
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        target.Clear();
        foreach (var name in _fontCatalog.Suggest(sender.Text))
        {
            target.Add(name);
        }
    }

    /// <summary>
    /// Shared handler for both font pickers — rejects any token whose text
    /// does not match an installed font (case-insensitive). This keeps the
    /// chip set authoritative: every chip is guaranteed to be a real font on
    /// the current system (subject to fonts being uninstalled between runs).
    /// </summary>
    private void OnFontTokenAdding(TokenizingTextBox sender, TokenItemAddingEventArgs args)
    {
        if (!_fontCatalog.Contains(args.TokenText))
        {
            args.Cancel = true;
        }
    }

    private async void OnRestartAppClicked(object sender, RoutedEventArgs e)
    {
        await ViewModel.WaitForFontSettingsPersistenceAsync();
        AppInstance.Restart(string.Empty);
    }
}
