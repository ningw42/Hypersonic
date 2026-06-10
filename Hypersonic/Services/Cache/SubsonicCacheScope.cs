using System.Security.Cryptography;
using System.Text;

using Hypersonic.Client;
using Hypersonic.Contracts.Services;

namespace Hypersonic.Services.Cache;

/// <summary>
/// Shared derivation of the Subsonic cache scope — the normalized-base-URI <em>server hash</em> and
/// the SHA-256 <em>identity hash</em> — used by both the cover-art cache (<c>SubsonicImageResolver</c>)
/// and the lyrics cache (<c>LyricsService</c>). Subsonic ids are unique only within one server <em>and</em>
/// one user account, so every cache key folds in the server + active-user identity. Factored here so the
/// two caches cannot drift in how they scope keys.
/// </summary>
internal static class SubsonicCacheScope
{
    /// <summary>SHA-256 of <paramref name="value"/> as a lower-case hex string.</summary>
    public static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Hash of the client's normalized base URI. Uses <see cref="SubsonicClient.BaseUri"/> directly when
    /// available; otherwise falls back to probing <c>getCoverArt</c> and stripping the last path segment.
    /// </summary>
    public static string GetServerHash(ISubsonicClient client)
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

    /// <summary>
    /// Builds the scoped cache key hash for a song's lyrics: <c>server-hash + active-user-identity-hash
    /// + song-id hash</c>. Returns <c>false</c> (and an empty hash) when there is no active client, no
    /// active user identity, or a blank song id — the cases where the lookup/fetch must be skipped.
    /// </summary>
    public static bool TryBuildSongKeyHash(ISubsonicClientService clientService, string songId, out string keyHash)
    {
        keyHash = string.Empty;

        var client = clientService.Client;
        if (client is null
            || string.IsNullOrEmpty(clientService.ActiveUserIdentity)
            || string.IsNullOrWhiteSpace(songId))
        {
            return false;
        }

        var serverHash = GetServerHash(client);
        var userScopeHash = Hash(clientService.ActiveUserIdentity);
        var idHash = Hash(songId);
        keyHash = Hash($"lyrics|{serverHash}|{userScopeHash}|{idHash}");
        return true;
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
}
