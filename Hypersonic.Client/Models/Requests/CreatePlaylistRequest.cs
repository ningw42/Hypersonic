namespace Hypersonic.Client.Models.Requests;

public class CreatePlaylistRequest : SubsonicRequest
{
    public override string Action => Constants.Action.CreatePlaylist;

    public string? PlaylistId { get; set; }
    public string? Name { get; set; }
    public string[]? SongId { get; set; }

    public CreatePlaylistRequest(SubsonicRequest subsonicRequest, string? playlistId = null, string? name = null, string[]? songId = null)
        : base(subsonicRequest)
    {
        this.PlaylistId = playlistId;
        this.Name = name;
        this.SongId = songId;
    }

    public override string ToQueryString()
    {
        string query = base.ToQueryString();

        if (!string.IsNullOrEmpty(this.PlaylistId))
        {
            query += $"&playlistId={this.PlaylistId}";
        }

        if (!string.IsNullOrEmpty(this.Name))
        {
            query += $"&name={Uri.EscapeDataString(this.Name)}";
        }

        if (this.SongId != null)
        {
            foreach (string id in this.SongId)
            {
                query += $"&songId={id}";
            }
        }

        return query;
    }
}
