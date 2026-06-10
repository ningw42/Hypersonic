using System.Security.Cryptography;
using System.Text;

using Hypersonic.Client;
using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models;
using Hypersonic.Services.Cache;

namespace Hypersonic.Services;

internal interface IImageResolver
{
    bool TryCreateCacheKey(ImageReference image, ImageRequestOptions options, out ImageCacheKey key);

    Task<Stream> FetchAsync(ImageReference image, ImageRequestOptions options, CancellationToken cancellationToken);
}

internal sealed class SubsonicImageResolver : IImageResolver
{
    private readonly ISubsonicClientService _subsonicClientService;
    private readonly ILogger _log;

    public SubsonicImageResolver(ISubsonicClientService subsonicClientService, ILoggingService logService)
    {
        _subsonicClientService = subsonicClientService;
        _log = logService.GetLogger(nameof(SubsonicImageResolver));
    }

    public bool TryCreateCacheKey(ImageReference image, ImageRequestOptions options, out ImageCacheKey key)
    {
        key = default!;

        if (image is not { Provider: ImageReference.SubsonicProvider, Kind: ImageReference.CoverArtKind })
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(image.Identity))
        {
            _log.Debug("Cache key rejected", new { Reason = "empty-identity", image.Provider, image.Kind, options.NormalizedSize });
            return false;
        }

        if (_subsonicClientService.Client == null)
        {
            _log.Debug("Cache key rejected", new { Reason = "no-active-client", image.Provider, image.Kind, options.NormalizedSize });
            return false;
        }

        if (string.IsNullOrEmpty(_subsonicClientService.ActiveUserIdentity))
        {
            _log.Debug("Cache key rejected", new { Reason = "no-active-user-identity", image.Provider, image.Kind, options.NormalizedSize });
            return false;
        }

        key = new ImageCacheKey(
            image.Provider,
            image.Kind,
            SubsonicCacheScope.GetServerHash(_subsonicClientService.Client),
            SubsonicCacheScope.Hash(_subsonicClientService.ActiveUserIdentity),
            SubsonicCacheScope.Hash(image.Identity),
            SubsonicCacheScope.Hash(image.Version ?? string.Empty),
            options.NormalizedSize);

        _log.Debug(
            "Cache key built",
            new
            {
                key.Provider,
                key.Kind,
                ServerHash = Short(key.ServerHash),
                ServerScopeHash = Short(key.ServerScopeHash),
                IdentityHash = Short(key.IdentityHash),
                key.Size,
            });
        return true;
    }

    public Task<Stream> FetchAsync(ImageReference image, ImageRequestOptions options, CancellationToken cancellationToken)
    {
        var client = _subsonicClientService.Client
            ?? throw new InvalidOperationException("No Subsonic client is available.");

        return client.GetCoverArtAsync(image.Identity, options.NormalizedSize);
    }

    private static string Short(string hash) => hash.Length >= 8 ? hash[..8] : hash;
}

internal sealed class RemoteImageResolver : IImageResolver, IDisposable
{
    private readonly HttpClient _httpClient = CreateHttpClient();
    private readonly ILogger _log;

    public RemoteImageResolver(ILoggingService logService)
    {
        _log = logService.GetLogger(nameof(RemoteImageResolver));
    }

    public bool TryCreateCacheKey(ImageReference image, ImageRequestOptions options, out ImageCacheKey key)
    {
        key = default!;

        if (image is not { Provider: ImageReference.RemoteProvider, Kind: ImageReference.RemoteImageKind })
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(image.Identity))
        {
            _log.Debug("Cache key rejected", new { Reason = "empty-identity", image.Provider, image.Kind, options.NormalizedSize });
            return false;
        }

        if (string.IsNullOrWhiteSpace(image.RemoteUrl) ||
            !Uri.TryCreate(image.RemoteUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            _log.Debug("Cache key rejected", new { Reason = "invalid-remote-url", image.Provider, image.Kind, options.NormalizedSize });
            return false;
        }

        key = new ImageCacheKey(
            image.Provider,
            image.Kind,
            string.Empty,
            string.Empty,
            image.Identity,
            Hash(image.Version ?? string.Empty),
            options.NormalizedSize);

        _log.Debug(
            "Cache key built",
            new
            {
                key.Provider,
                key.Kind,
                IdentityHash = Short(key.IdentityHash),
                key.Size,
            });
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

    private static string Short(string hash) => hash.Length >= 8 ? hash[..8] : hash;
}

internal sealed record ImageCacheKey(
    string Provider,
    string Kind,
    string ServerHash,
    string ServerScopeHash,
    string IdentityHash,
    string VersionHash,
    string Size)
{
    public string ToMaterial()
    {
        return $"{Provider}|{Kind}|{ServerHash}|{ServerScopeHash}|{IdentityHash}|{VersionHash}|{Size}";
    }
}
