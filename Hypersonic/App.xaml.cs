using Hypersonic.Activation;
using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Lyrics;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models;
using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Core.Services;
using Hypersonic.Core.Services.Lyrics;
using Hypersonic.Core.Services.Logging;
using Hypersonic.Helpers;
using Hypersonic.Models;
using Hypersonic.Services;
using Hypersonic.Services.Lyrics;
using Hypersonic.ViewModels;
using Hypersonic.Views;

using System.Reflection;

using CommunityToolkit.Mvvm.Messaging;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;

using Windows.Storage;

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

    /// <summary>
    /// Set to true when the user chooses Exit from the tray menu.
    /// MainWindow uses this to distinguish a real exit from a close-to-tray.
    /// </summary>
    public static bool IsExiting { get; set; }

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

            // Logging
            services.AddSingleton<ILoggingService>(sp =>
            {
                var service = new LoggingService();
                service.AddSink(new DebugSink(), LogLevel.Debug);

                try
                {
                    var logDir = RuntimeHelper.IsMSIX
                        ? Path.Combine(ApplicationData.Current.LocalFolder.Path, "Logs")
                        : Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "Hypersonic", "Logs");
                    service.AddSink(new FileSink(logDir), LogLevel.Info);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[LoggingService] FileSink creation failed: {ex}");
                }

#if !DEBUG
                service.MinimumLevel = LogLevel.Info;
#endif

                return service;
            });

            // Services
            services.AddSingleton<ILocalSettingsService, LocalSettingsService>();
            services.AddSingleton<IThemeSelectorService, ThemeSelectorService>();
            services.AddSingleton<IBackdropSelectorService, BackdropSelectorService>();
            services.AddTransient<INavigationViewService, NavigationViewService>();

            services.AddSingleton<IActivationService, ActivationService>();
            services.AddSingleton<IPageService, PageService>();
            services.AddSingleton<INavigationService, NavigationService>();
            services.AddSingleton<IFileService, FileService>();
            services.AddSingleton<ISubsonicClientService, SubsonicClientService>();
            services.AddSingleton<IAppFolderLocator, AppFolderLocator>();
            services.AddSingleton<IImageCacheStore, SqliteImageCacheStore>();
            services.AddSingleton<IImageService, CachedImageService>();

            // Lyrics: durable cache store, the shared parser registry, the parsers, the providers (in
            // priority order, so the service receives an ordered IEnumerable), and the orchestrator.
            services.AddSingleton<ILyricsCacheStore, SqliteLyricsCacheStore>();
            services.AddSingleton<ILyricsParserRegistry, LyricsParserRegistry>();
            services.AddSingleton<ILyricsPayloadParser, SubsonicStructuredLyricsParser>();
            services.AddSingleton<ILyricsPayloadParser, LrcSyncedLyricsParser>();
            services.AddSingleton<ILyricsPayloadParser, LrcPlainLyricsParser>();
            services.AddSingleton<ILyricsPayloadParser, TtmlLyricsParser>();
            services.AddSingleton<ILyricsPayloadParser>(sp => new LyricifyWordLyricsParser(
                LyricsFormat.Qrc, Lyricify.Lyrics.Models.LyricsRawTypes.Qrc,
                sp.GetRequiredService<ILoggingService>()));
            services.AddSingleton<ILyricsPayloadParser>(sp => new LyricifyWordLyricsParser(
                LyricsFormat.Krc, Lyricify.Lyrics.Models.LyricsRawTypes.Krc,
                sp.GetRequiredService<ILoggingService>()));
            services.AddSingleton<ILyricsPayloadParser>(sp => new LyricifyWordLyricsParser(
                LyricsFormat.Yrc, Lyricify.Lyrics.Models.LyricsRawTypes.Yrc,
                sp.GetRequiredService<ILoggingService>()));
            services.AddSingleton<ILyricsProvider, SubsonicLyricsProvider>();
            services.AddSingleton<ILyricsProvider, LrcLibLyricsProvider>(); // AFTER Subsonic → Subsonic wins an equal-tier tie
            services.AddSingleton<ILyricsProvider, AmllTtmlLyricsProvider>(); // word-tier, wins only on strictly-higher tier
            services.AddSingleton<ILyricsProvider, QQMusicLyricsProvider>();  // CN word-tier, gated by confidence
            services.AddSingleton<ILyricsProvider, NeteaseLyricsProvider>();
            services.AddSingleton<ILyricsProvider, KugouLyricsProvider>();
            services.AddSingleton<IAmllLyricsIndex, AmllLyricsIndex>();
            services.AddSingleton<ILyricifyLyricsClient, LyricifyLyricsClient>();
            services.Configure<AmllLyricsOptions>(_ => { }); // defaults (Enabled=true, 7-day refresh); seam for the future toggle
            services.Configure<QQLyricsOptions>(_ => { });     // defaults: Enabled=true, MatchThreshold=55
            services.Configure<NeteaseLyricsOptions>(_ => { });
            services.Configure<KugouLyricsOptions>(_ => { });
            // Local override source (NOT a fan-out provider): LyricsService consults it ahead of the cache.
            services.AddSingleton<ILocalLyricsOverrideSource, LocalFileLyricsSource>();
            services.AddSingleton<ILyricsPreferences, LyricsPreferences>();
            services.AddSingleton<ILyricsService, LyricsService>();
            services.AddSingleton<ILyricsSourceCatalog, LyricsSourceCatalog>();

            // Named HTTP client for lrclib (the first outbound HTTP outside SubsonicClient). The
            // per-request bound is the fan-out budget via the linked CTS; Timeout is only a generous
            // backstop that must never pre-empt the largest legitimate budget (a manual refresh).
            var lrclibVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
            services.AddHttpClient("lrclib", client =>
            {
                client.BaseAddress = new Uri("https://lrclib.net");
                client.DefaultRequestHeaders.UserAgent.ParseAdd($"Hypersonic v{lrclibVersion} (https://github.com/ningw42/Hypersonic)");
                client.Timeout = TimeSpan.FromSeconds(30);
            });

            services.AddHttpClient(AmllLyricsConstants.HttpClientName, client =>
            {
                client.BaseAddress = new Uri(AmllLyricsConstants.CdnBase);
                client.DefaultRequestHeaders.UserAgent.ParseAdd($"Hypersonic v{lrclibVersion} (https://github.com/ningw42/Hypersonic)");
                client.Timeout = TimeSpan.FromSeconds(30);
            });

            services.AddSingleton<PlaybackService>();
            services.AddSingleton<IPlaybackService>(sp => sp.GetRequiredService<PlaybackService>());
            services.AddSingleton<IReadOnlyPlaybackState>(sp => sp.GetRequiredService<PlaybackService>());
            services.AddSingleton<ITaskbarService, TaskbarService>();
            services.AddSingleton<ITrayIconService, TrayIconService>();
            services.AddSingleton<ICredentialService, CredentialService>();
            services.AddSingleton<IAppStateService, AppStateService>();
            services.AddSingleton<IRandomPicksCacheService, RandomPicksCacheService>();

            services.AddSingleton<TimeProvider>(TimeProvider.System);
            services.AddSingleton<IScrobblingService, ScrobblingService>();
            services.AddSingleton<ISystemFontCatalog, SystemFontCatalog>();

            // Cross-surface favorite (star) synchronization.
            services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);
            services.AddSingleton<IFavoritesService, FavoritesService>();

            // Views and ViewModels
            services.AddSingleton<PlayerViewModel>();
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<LyricsSettingsViewModel>();
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
            services.AddTransient<TracksViewModel>();
            services.AddTransient<TracksPage>();
            services.AddTransient<PlaylistsViewModel>();
            services.AddTransient<PlaylistsPage>();
            services.AddTransient<PlaylistDetailViewModel>();
            services.AddTransient<PlaylistDetailPage>();
            services.AddTransient<NowPlayingViewModel>();
            services.AddTransient<NowPlayingPage>();
            services.AddTransient<ConnectViewModel>();
            services.AddTransient<ConnectPage>();
            services.AddTransient<ShellPage>();
            services.AddTransient<ShellViewModel>();

            // Configuration
            services.Configure<LocalSettingsOptions>(context.Configuration.GetSection(nameof(LocalSettingsOptions)));
            services.Configure<ImageCacheOptions>(context.Configuration.GetSection(nameof(ImageCacheOptions)));
            services.Configure<LyricsCacheOptions>(context.Configuration.GetSection(nameof(LyricsCacheOptions)));
            services.Configure<LyricsOrchestrationOptions>(context.Configuration.GetSection(nameof(LyricsOrchestrationOptions)));
        }).
        Build();

        UnhandledException += App_UnhandledException;
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        var log = App.GetService<ILoggingService>().GetLogger(nameof(App));
        log.Error("Unhandled exception", new { e.Message }, e.Exception);
    }

    protected async override void OnLaunched(LaunchActivatedEventArgs args)
    {
        base.OnLaunched(args);

        await App.GetService<IActivationService>().ActivateAsync(args);
        _ = ReconcileImageCacheAsync();
        _ = ReconcileLyricsCacheAsync();
    }

    private static async Task ReconcileImageCacheAsync()
    {
        try
        {
            await App.GetService<IImageService>().ReconcileAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            App.GetService<ILoggingService>()
                .GetLogger(nameof(App))
                .Warning("Image cache reconciliation failed", exception: ex);
        }
    }

    private static async Task ReconcileLyricsCacheAsync()
    {
        try
        {
            await App.GetService<ILyricsService>().ReconcileAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            App.GetService<ILoggingService>()
                .GetLogger(nameof(App))
                .Warning("Lyrics cache reconciliation failed", exception: ex);
        }

        try
        {
            await App.GetService<IAmllLyricsIndex>().WarmAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            App.GetService<ILoggingService>()
                .GetLogger(nameof(App))
                .Warning("AMLL index warm failed", exception: ex);
        }
    }
}
