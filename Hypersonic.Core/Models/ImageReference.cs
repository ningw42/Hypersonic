using System.Security.Cryptography;
using System.Text;

namespace Hypersonic.Core.Models;

public sealed record ImageReference(
    string Provider,
    string Kind,
    string Identity,
    string? Version = null,
    string? RemoteUrl = null)
{
    public const string SubsonicProvider = "subsonic";
    public const string RemoteProvider = "remote";
    public const string CoverArtKind = "cover-art";
    public const string RemoteImageKind = "remote-image";

    public static ImageReference? SubsonicCoverArt(string? coverArtId, string? version = null)
    {
        var identity = NormalizeNullable(coverArtId);
        if (identity == null)
        {
            return null;
        }

        return new ImageReference(
            SubsonicProvider,
            CoverArtKind,
            identity,
            NormalizeNullable(version));
    }

    public static ImageReference? Remote(string? remoteUrl, string? version = null)
    {
        if (string.IsNullOrWhiteSpace(remoteUrl) ||
            !Uri.TryCreate(remoteUrl.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        return new ImageReference(
            RemoteProvider,
            RemoteImageKind,
            Hash(NormalizeRemoteKey(uri)),
            NormalizeNullable(version),
            uri.ToString());
    }

    private static string? NormalizeNullable(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string NormalizeRemoteKey(Uri uri)
    {
        var builder = new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty,
            Fragment = string.Empty,
            Scheme = uri.Scheme.ToLowerInvariant(),
            Host = uri.Host.ToLowerInvariant(),
        };

        builder.Path = builder.Path.TrimEnd('/');
        return builder.Uri.GetComponents(
            UriComponents.SchemeAndServer | UriComponents.Path | UriComponents.Query,
            UriFormat.Unescaped).TrimEnd('/');
    }

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
