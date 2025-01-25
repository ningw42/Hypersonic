namespace Hypersonic.Models;

public interface IDisc
{
    string Id { get; }

    string Title { get; }

    IList<IRecording> Recordings { get; }
}

public class Disc
{
}
