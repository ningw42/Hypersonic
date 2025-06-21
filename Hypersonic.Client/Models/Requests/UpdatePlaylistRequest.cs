namespace Hypersonic.Client.Models.Requests;

public class UpdatePlaylistRequest : SubsonicRequest
{
    public override string Action => Constants.Action.UpdatePlaylist;

    public string PlaylistId { get; set; }
    public string? Name { get; set; }
    public string? Comment { get; set; }
    public bool? Public { get; set; }
    public string[]? SongIdToAdd { get; set; }
    public int[]? SongIndexToRemove { get; set; }

    public UpdatePlaylistRequest(SubsonicRequest subsonicRequest, string playlistId, string? name = null, string? comment = null, bool? @public = null, string[]? songIdToAdd = null, int[]? songIndexToRemove = null)
        : base(subsonicRequest)
    {
        this.PlaylistId = playlistId;
        this.Name = name;
        this.Comment = comment;
        this.Public = @public;
        this.SongIdToAdd = songIdToAdd;
        this.SongIndexToRemove = songIndexToRemove;
    }

    public override string ToQueryString()
    {
        string query = base.ToQueryString();
        query += $"&playlistId={this.PlaylistId}";

        if (!string.IsNullOrEmpty(this.Name))
        {
            query += $"&name={Uri.EscapeDataString(this.Name)}";
        }

        if (!string.IsNullOrEmpty(this.Comment))
        {
            query += $"&comment={Uri.EscapeDataString(this.Comment)}";
        }

        if (this.Public.HasValue)
        {
            query += $"&public={this.Public.Value.ToString().ToLower()}";
        }

        if (this.SongIdToAdd != null)
        {
            foreach (string id in this.SongIdToAdd)
            {
                query += $"&songIdToAdd={id}";
            }
        }

        if (this.SongIndexToRemove != null)
        {
            foreach (int index in this.SongIndexToRemove)
            {
                query += $"&songIndexToRemove={index}";
            }
        }

        return query;
    }
}
