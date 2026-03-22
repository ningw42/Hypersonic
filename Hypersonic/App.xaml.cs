using Hypersonic.Activation;
using Hypersonic.Contracts.Services;
using Hypersonic.Helpers;
using Hypersonic.Models;
using Hypersonic.Services;
using Hypersonic.ViewModels;
using Hypersonic.Views;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;

namespace Hypersonic;

// To learn more about WinUI 3, see https://docs.microsoft.com/windows/apps/winui/winui3/.
public partial class App : Application
{
    // The .NET Generic Host provides dependency injection, configuration, logging, and other services.
    // https://docs.microsoft.com/dotnet/core/extensions/generic-host
    // https://docs.microsoft.com/dotnet/core/extensions/dependency-injection
    // https://docs.microsoft.com/dotnet/core/extensions/configuration
    // https://docs.microsoft.com/dotnet/core/extensions/logging
    public IHost Host
    {
        get;
    }

    public static T GetService<T>()
        where T : class
    {
        if ((App.Current as App)!.Host.Services.GetService(typeof(T)) is not T service)
        {
            throw new ArgumentException($"{typeof(T)} needs to be registered in ConfigureServices within App.xaml.cs.");
        }

        return service;
    }

    public static WindowEx MainWindow { get; } = new MainWindow();

    public static UIElement? AppTitlebar { get; set; }

    public App()
    {
        InitializeComponent();

        Host = Microsoft.Extensions.Hosting.Host.
        CreateDefaultBuilder().
        UseContentRoot(AppContext.BaseDirectory).
        ConfigureServices((context, services) =>
        {
            // Default Activation Handler
            services.AddTransient<ActivationHandler<LaunchActivatedEventArgs>, DefaultActivationHandler>();

            // Other Activation Handlers

            // Services
            services.AddSingleton<ILocalSettingsService, LocalSettingsService>();
            services.AddSingleton<IThemeSelectorService, ThemeSelectorService>();
            services.AddTransient<INavigationViewService, NavigationViewService>();

            services.AddSingleton<IActivationService, ActivationService>();
            services.AddSingleton<IPageService, PageService>();
            services.AddSingleton<INavigationService, NavigationService>();
            services.AddSingleton<IFileService, FileService>();
            services.AddSingleton<ISubsonicClientService, SubsonicClientService>();
            services.AddSingleton<IPlaybackService, PlaybackService>();
            services.AddSingleton<ITaskbarService, TaskbarService>();

            // Views and ViewModels
            services.AddSingleton<PlayerViewModel>();
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<SettingsPage>();
            services.AddTransient<MainViewModel>();
            services.AddTransient<MainPage>();
            services.AddTransient<AlbumsViewModel>();
            services.AddTransient<AlbumsPage>();
            services.AddTransient<AlbumDetailViewModel>();
            services.AddTransient<AlbumDetailPage>();
            services.AddTransient<ArtistsViewModel>();
            services.AddTransient<ArtistsPage>();
            services.AddTransient<ArtistDetailViewModel>();
            services.AddTransient<ArtistDetailPage>();
            services.AddTransient<PlaylistsViewModel>();
            services.AddTransient<PlaylistsPage>();
            services.AddTransient<PlaylistDetailViewModel>();
            services.AddTransient<PlaylistDetailPage>();
            services.AddTransient<ConnectViewModel>();
            services.AddTransient<ConnectPage>();
            services.AddTransient<ShellPage>();
            services.AddTransient<ShellViewModel>();

            // Configuration
            services.Configure<LocalSettingsOptions>(context.Configuration.GetSection(nameof(LocalSettingsOptions)));
        }).
        Build();

        UnhandledException += App_UnhandledException;
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"UNHANDLED EXCEPTION: {e.Exception}");
        System.Diagnostics.Debug.WriteLine($"Message: {e.Message}");
        System.Diagnostics.Debug.WriteLine($"StackTrace: {e.Exception.StackTrace}");
    }

    protected async override void OnLaunched(LaunchActivatedEventArgs args)
    {
        base.OnLaunched(args);

        await App.GetService<IActivationService>().ActivateAsync(args);
    }
}
