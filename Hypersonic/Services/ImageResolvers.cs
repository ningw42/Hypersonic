using System.Security.Cryptography;
using System.Text;

using Hypersonic.Client;
using Hypersonic.Contracts.Services;
using Hypersonic.Core.Models;

namespace Hypersonic.Services;

internal interface IImageResolver
{
    bool TryCreateCacheKey(ImageReference image, ImageRequestOptions options, out ImageCacheKey key);

    Task<Stream> FetchAsync(ImageReference image, ImageRequestOptions options, CancellationToken cancellationToken);
}

internal sealed class SubsonicImageResolver : IImageResolver
{
    private readonly ISubsonicClientService _subsonicClientService;

    public SubsonicImageResolver(ISubsonicClientService subsonicClientService)
    {
        _subsonicClientService = subsonicClientService;
    }

    public bool TryCreateCacheKey(ImageReference image, ImageRequestOptions options, out ImageCacheKey key)
    {
        key = default!;

        if (image is not { Provider: ImageReference.SubsonicProvider, Kind: ImageReference.CoverArtKind } ||
            string.IsNullOrWhiteSpace(image.Identity) ||
            _subsonicClientService.Client == null)
        {
            return false;
        }

        key = new ImageCacheKey(
            image.Provider,
            image.Kind,
            GetSubsonicServerHash(_subsonicClientService.Client),
            Hash(image.Identity),
            Hash(image.Version ?? string.Empty),
            options.NormalizedSize);
        return true;
    }

    public Task<Stream> FetchAsync(ImageReference image, ImageRequestOptions options, CancellationToken cancellationToken)
    {
        var client = _subsonicClientService.Client
            ?? throw new InvalidOperationException("No Subsonic client is available.");

        return client.GetCoverArtAsync(image.Identity, options.NormalizedSize);
    }

    private static string GetSubsonicServerHash(ISubsonicClient client)
    {
        if (client is SubsonicClient subsonicClient)
        {
            return Hash(NormalizeUri(subsonicClient.BaseUri, removeLastPathSegment: false));
        }

        try
        {
            var probe = client.GetCoverArtUri("__hypersonic_cache_probe__", "1");
            return Hash(NormalizeUri(probe, removeLastPathSegment: true));
        }
        catch
        {
            return Hash(client.GetType().FullName ?? "unknown-subsonic-client");
        }
    }

    private static string NormalizeUri(string uri, bool removeLastPathSegment)
    {
        return Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
            ? NormalizeUri(parsed, removeLastPathSegment)
            : uri.Trim().TrimEnd('/').ToLowerInvariant();
    }

    private static string NormalizeUri(Uri uri, bool removeLastPathSegment)
    {
        var builder = new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty,
            Fragment = string.Empty,
            Query = string.Empty,
            Scheme = uri.Scheme.ToLowerInvariant(),
            Host = uri.Host.ToLowerInvariant(),
        };

        var path = builder.Path.TrimEnd('/');
        if (removeLastPathSegment)
        {
            var lastSlash = path.LastIndexOf('/');
            path = lastSlash > 0 ? path[..lastSlash] : string.Empty;
        }

        builder.Path = path;
        return builder.Uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.Unescaped).TrimEnd('/');
    }

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

internal sealed class RemoteImageResolver : IImageResolver, IDisposable
{
    private readonly HttpClient _httpClient = CreateHttpClient();

    public bool TryCreateCacheKey(ImageReference image, ImageRequestOptions options, out ImageCacheKey key)
    {
        key = default!;

        if (image is not { Provider: ImageReference.RemoteProvider, Kind: ImageReference.RemoteImageKind } ||
            string.IsNullOrWhiteSpace(image.Identity) ||
            string.IsNullOrWhiteSpace(image.RemoteUrl) ||
            !Uri.TryCreate(image.RemoteUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        key = new ImageCacheKey(
            image.Provider,
            image.Kind,
            string.Empty,
            image.Identity,
            Hash(image.Version ?? string.Empty),
            options.NormalizedSize);
        return true;
    }

    public Task<Stream> FetchAsync(ImageReference image, ImageRequestOptions options, CancellationToken cancellationToken)
    {
        return _httpClient.GetStreamAsync(new Uri(image.RemoteUrl!), cancellationToken);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Hypersonic/1.0");
        return client;
    }

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

internal sealed record ImageCacheKey(
    string Provider,
    string Kind,
    string ServerHash,
    string IdentityHash,
    string VersionHash,
    string Size)
{
    public string ToMaterial()
    {
        return $"{Provider}|{Kind}|{ServerHash}|{IdentityHash}|{VersionHash}|{Size}";
    }
}
