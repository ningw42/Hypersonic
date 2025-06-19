namespace Hypersonic.Models;

public interface IDisc
{
    string Id { get; set; }
    string Title { get; set; }
    IList<IRecording> Recordings { get; set; }
}

public class Disc : IDisc
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public IList<IRecording> Recordings { get; set; } = new List<IRecording>();
    
    public Disc()
    {
    }
    
    public Disc(string id, string title, IList<IRecording>? recordings = null)
    {
        Id = id;
        Title = title;
        Recordings = recordings ?? new List<IRecording>();
    }
    
    public void AddRecording(IRecording recording)
    {
        if (recording != null)
        {
            Recordings.Add(recording);
        }
    }
}
