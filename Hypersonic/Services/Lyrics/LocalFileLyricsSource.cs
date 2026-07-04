using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Lyrics;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models.Lyrics;
using Hypersonic.Core.Services.Lyrics;

namespace Hypersonic.Services.Lyrics;

/// <summary>
/// Reads a user-sideloaded TTML override (<c>&lt;songId&gt;.ttml</c> under the override folder) and
/// parses it through the shared <c>ttml</c> registry parser. This is an <b>override source, not a
/// fan-out provider</b>: <see cref="LyricsService"/> consults it ahead of the cache read so a present
/// file wins outright (bypassing cache + fan-out + write) and deleting it reverts cleanly. Absent or
/// unparseable → <c>null</c>, and the caller falls through to the normal path. See the Phase 2 design
/// doc §3.8.
/// </summary>
public sealed class LocalFileLyricsSource : ILocalLyricsOverrideSource
{
    private readonly IAppFolderLocator _folders;
    private readonly ILyricsParserRegistry _registry;
    private readonly ILogger _log;

    public LocalFileLyricsSource(IAppFolderLocator folders, ILyricsParserRegistry registry, ILoggingService logService)
    {
        _folders = folders;
        _registry = registry;
        _log = logService.GetLogger(nameof(LocalFileLyricsSource));
    }

    public async Task<LyricsDocument?> TryGetOverrideAsync(string songId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(songId) || !IsSafeFileStem(songId))
        {
            return null;
        }

        string path;
        try
        {
            var folder = _folders.GetLyricsOverrideFolder();
            var candidate = Path.GetFullPath(Path.Combine(folder, songId + "." + TtmlLyricsConstants.Format));

            // Defence in depth on top of the file-stem check: the resolved path must stay inside the folder.
            var fullFolder = Path.GetFullPath(folder);
            if (!candidate.StartsWith(EnsureTrailingSeparator(fullFolder), StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            path = candidate;
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return null;
        }

        if (!File.Exists(path))
        {
            return null; // the common case: nothing sideloaded — a cheap existence check, no allocation
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            var document = _registry.Parse(LyricsFormat.Ttml, bytes);
            if (document is null)
            {
                _log.Warning("Lyrics override file failed to parse; ignoring", new { SongId = songId });
            }

            return document;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warning("Failed to read lyrics override file", new { SongId = songId }, ex);
            return null;
        }
    }

    private static bool IsSafeFileStem(string songId)
        => songId.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
           && songId.IndexOf('/') < 0
           && songId.IndexOf('\\') < 0
           && songId != "."
           && songId != "..";

    private static string EnsureTrailingSeparator(string path)
        => path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
}
