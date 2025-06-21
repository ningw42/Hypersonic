namespace Hypersonic.Client.Models.Requests;

public class UnstarRequest : SubsonicRequest
{
    public override string Action => Constants.Action.Unstar;

    public string? Id { get; set; }
    public string? AlbumId { get; set; }
    public string? ArtistId { get; set; }

    public UnstarRequest(SubsonicRequest subsonicRequest, string? id = null, string? albumId = null, string? artistId = null)
        : base(subsonicRequest)
    {
        this.Id = id;
        this.AlbumId = albumId;
        this.ArtistId = artistId;
    }

    public override string ToQueryString()
    {
        string query = base.ToQueryString();

        if (!string.IsNullOrEmpty(this.Id))
        {
            query += $"&id={this.Id}";
        }

        if (!string.IsNullOrEmpty(this.AlbumId))
        {
            query += $"&albumId={this.AlbumId}";
        }

        if (!string.IsNullOrEmpty(this.ArtistId))
        {
            query += $"&artistId={this.ArtistId}";
        }

        return query;
    }
}
