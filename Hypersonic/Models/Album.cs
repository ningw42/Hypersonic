namespace Hypersonic.Models;

public interface IAlbum
{
    public string Id { get; }

    public string Title { get; }

    public string CoverArtUri { get; }

    public int Runtime { get; }

    public string Genre { get; }

    public IList<IDisc> Discs { get; }
}

public class Album : IAlbum
{
}
