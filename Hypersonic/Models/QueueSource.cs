namespace Hypersonic.Models;

public enum QueueSourceKind
{
    None,
    Album,
    Playlist,
    Artist,
    Search,
}

public record QueueSource(QueueSourceKind Kind, string? Id = null, string? Name = null);
