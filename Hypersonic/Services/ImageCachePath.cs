using Hypersonic.Helpers;
using Hypersonic.Models;

using Windows.Storage;

namespace Hypersonic.Services;

internal static class ImageCachePath
{
    public static string Resolve(ImageCacheOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.RootPath))
        {
            return options.RootPath;
        }

        if (RuntimeHelper.IsMSIX)
        {
            return Path.Combine(ApplicationData.Current.LocalCacheFolder.Path, "ImageCache");
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Hypersonic",
            "Cache",
            "Images");
    }
}
