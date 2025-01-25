namespace Hypersonic.Models;

public interface IRecording
{
    public string Title { get; }

    public string Artist { get; }
}

public class Recording : IRecording
{
    public Recording()
    {
    }
}
