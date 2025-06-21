using System.Text;

namespace Hypersonic.Client.Models.Requests;

public class GetSongsByGenreRequest : SubsonicRequest
{
    public string Genre { get; set; }

    public int? Count { get; set; }

    public int? Offset { get; set; }    public string? MusicFolderId { get; set; }

    public override string Action => Constants.Action.GetSongsByGenre;

    public GetSongsByGenreRequest(SubsonicRequest subsonicRequest, string genre, int? count = null, int? offset = null, string? musicFolderId = null)
        : base(subsonicRequest)
    {
        this.Genre = genre;
        this.Count = count;
        this.Offset = offset;
        this.MusicFolderId = musicFolderId;
    }

    public override string ToQueryString()
    {
        StringBuilder query = new StringBuilder(base.ToQueryString());

        query.Append($"&genre={Uri.EscapeDataString(this.Genre)}");

        if (this.Count.HasValue)
        {
            query.Append($"&count={this.Count.Value}");
        }

        if (this.Offset.HasValue)
        {
            query.Append($"&offset={this.Offset.Value}");
        }

        if (!string.IsNullOrEmpty(this.MusicFolderId))
        {
            query.Append($"&musicFolderId={this.MusicFolderId}");
        }

        return query.ToString();
    }
}
