using Hypersonic.Contracts.Services;
using Hypersonic.Core.Models;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Hypersonic.Helpers;

public static class CachedImage
{
    private static IImageService? _resolver;

    public static readonly DependencyProperty ReferenceProperty =
        DependencyProperty.RegisterAttached(
            "Reference",
            typeof(ImageReference),
            typeof(CachedImage),
            new PropertyMetadata(null, OnSourcePropertyChanged));

    public static readonly DependencyProperty SizeProperty =
        DependencyProperty.RegisterAttached(
            "Size",
            typeof(string),
            typeof(CachedImage),
            new PropertyMetadata(null, OnSourcePropertyChanged));

    private static readonly DependencyProperty CancellationTokenSourceProperty =
        DependencyProperty.RegisterAttached(
            "CancellationTokenSource",
            typeof(CancellationTokenSource),
            typeof(CachedImage),
            new PropertyMetadata(null));

    private static readonly DependencyProperty AreLifetimeHandlersAttachedProperty =
        DependencyProperty.RegisterAttached(
            "AreLifetimeHandlersAttached",
            typeof(bool),
            typeof(CachedImage),
            new PropertyMetadata(false));

    private static readonly DependencyProperty IsRefreshQueuedProperty =
        DependencyProperty.RegisterAttached(
            "IsRefreshQueued",
            typeof(bool),
            typeof(CachedImage),
            new PropertyMetadata(false));

    public static ImageReference? GetReference(Image image) => (ImageReference?)image.GetValue(ReferenceProperty);

    public static void SetReference(Image image, ImageReference? value) => image.SetValue(ReferenceProperty, value);

    public static string? GetSize(Image image) => (string?)image.GetValue(SizeProperty);

    public static void SetSize(Image image, string? value) => image.SetValue(SizeProperty, value);

    public static IImageService? Resolver
    {
        get => _resolver ??= ResolveDefaultImageService();
        set => _resolver = value;
    }

    public static async Task<BitmapImage?> LoadBitmapAsync(
        IImageService imageService,
        ImageReference? reference,
        ImageRequestOptions options,
        CancellationToken cancellationToken = default)
    {
        var uri = await imageService.GetImageAsync(reference, options, cancellationToken);
        return uri == null ? null : new BitmapImage(uri);
    }

    private static CancellationTokenSource? GetCancellationTokenSource(Image image)
    {
        return (CancellationTokenSource?)image.GetValue(CancellationTokenSourceProperty);
    }

    private static void SetCancellationTokenSource(Image image, CancellationTokenSource? value)
    {
        image.SetValue(CancellationTokenSourceProperty, value);
    }

    private static bool GetAreLifetimeHandlersAttached(Image image)
    {
        return (bool)image.GetValue(AreLifetimeHandlersAttachedProperty);
    }

    private static void SetAreLifetimeHandlersAttached(Image image, bool value)
    {
        image.SetValue(AreLifetimeHandlersAttachedProperty, value);
    }

    private static bool GetIsRefreshQueued(Image image)
    {
        return (bool)image.GetValue(IsRefreshQueuedProperty);
    }

    private static void SetIsRefreshQueued(Image image, bool value)
    {
        image.SetValue(IsRefreshQueuedProperty, value);
    }

    private static IImageService? ResolveDefaultImageService()
    {
        try
        {
            return App.GetService<IImageService>();
        }
        catch
        {
            return null;
        }
    }

    private static void OnSourcePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Image image)
        {
            return;
        }

        if (!GetAreLifetimeHandlersAttached(image))
        {
            image.Loaded += OnImageLoaded;
            image.Unloaded += OnImageUnloaded;
            SetAreLifetimeHandlersAttached(image, true);
        }

        QueueRefreshSource(image);
    }

    private static void OnImageLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Image image ||
            image.Source != null ||
            GetCancellationTokenSource(image) != null ||
            GetReference(image) == null)
        {
            return;
        }

        QueueRefreshSource(image);
    }

    private static void OnImageUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Image image)
        {
            return;
        }

        CancelPendingLoad(image);
    }

    private static async void RefreshSource(Image image)
    {
        CancelPendingLoad(image);

        var reference = GetReference(image);
        var size = GetSize(image);

        if (reference == null)
        {
            image.Source = null;
            return;
        }

        var cts = new CancellationTokenSource();
        SetCancellationTokenSource(image, cts);

        try
        {
            var imageService = Resolver;
            if (imageService == null)
            {
                image.Source = null;
                return;
            }

            var uri = await imageService.GetImageAsync(
                reference,
                ImageRequestOptions.ForSize(size),
                cts.Token);

            if (cts.IsCancellationRequested)
            {
                return;
            }

            image.Source = uri == null ? null : new BitmapImage(uri);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            if (!cts.IsCancellationRequested)
            {
                image.Source = null;
            }
        }
        finally
        {
            if (ReferenceEquals(GetCancellationTokenSource(image), cts))
            {
                SetCancellationTokenSource(image, null);
            }

            cts.Dispose();
        }
    }

    private static void CancelPendingLoad(Image image)
    {
        var cts = GetCancellationTokenSource(image);
        if (cts == null)
        {
            return;
        }

        SetCancellationTokenSource(image, null);
        cts.Cancel();
    }

    private static void QueueRefreshSource(Image image)
    {
        if (GetIsRefreshQueued(image))
        {
            return;
        }

        SetIsRefreshQueued(image, true);

        if (image.DispatcherQueue?.TryEnqueue(() =>
        {
            SetIsRefreshQueued(image, false);
            RefreshSource(image);
        }) == true)
        {
            return;
        }

        SetIsRefreshQueued(image, false);
        RefreshSource(image);
    }
}
