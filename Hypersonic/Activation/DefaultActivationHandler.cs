using Hypersonic.Contracts.Services;
using Hypersonic.ViewModels;

using Microsoft.UI.Xaml;

namespace Hypersonic.Activation;

public class DefaultActivationHandler : ActivationHandler<LaunchActivatedEventArgs>
{
    private readonly INavigationService _navigationService;
    private readonly IAppStateService _appStateService;

    private static readonly HashSet<string> TopLevelPages = new()
    {
        typeof(MainViewModel).FullName!,
        typeof(AlbumsViewModel).FullName!,
        typeof(ArtistsViewModel).FullName!,
        typeof(PlaylistsViewModel).FullName!,
        typeof(SettingsViewModel).FullName!,
    };

    public DefaultActivationHandler(INavigationService navigationService, IAppStateService appStateService)
    {
        _navigationService = navigationService;
        _appStateService = appStateService;
    }

    protected override bool CanHandleInternal(LaunchActivatedEventArgs args)
    {
        // None of the ActivationHandlers has handled the activation.
        return _navigationService.Frame?.Content == null;
    }

    protected async override Task HandleInternalAsync(LaunchActivatedEventArgs args)
    {
        var lastPage = _appStateService.State.Navigation.LastPage;
        var pageKey = lastPage != null && TopLevelPages.Contains(lastPage)
            ? lastPage
            : typeof(MainViewModel).FullName!;

        _navigationService.NavigateTo(pageKey, args.Arguments);

        await Task.CompletedTask;
    }
}
