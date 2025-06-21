using System.Text;

namespace Hypersonic.Client.Models.Requests;

public class GetRandomSongsRequest : SubsonicRequest
{
    public int? Size { get; set; }

    public string? Genre { get; set; }

    public int? FromYear { get; set; }

    public int? ToYear { get; set; }    public string? MusicFolderId { get; set; }

    public override string Action => Constants.Action.GetRandomSongs;

    public GetRandomSongsRequest(SubsonicRequest subsonicRequest, int? size = null, string? genre = null, int? fromYear = null, int? toYear = null, string? musicFolderId = null)
        : base(subsonicRequest)
    {
        this.Size = size;
        this.Genre = genre;
        this.FromYear = fromYear;
        this.ToYear = toYear;
        this.MusicFolderId = musicFolderId;
    }

    public override string ToQueryString()
    {
        StringBuilder query = new StringBuilder(base.ToQueryString());

        if (this.Size.HasValue)
        {
            query.Append($"&size={this.Size.Value}");
        }

        if (!string.IsNullOrEmpty(this.Genre))
        {
            query.Append($"&genre={Uri.EscapeDataString(this.Genre)}");
        }

        if (this.FromYear.HasValue)
        {
            query.Append($"&fromYear={this.FromYear.Value}");
        }

        if (this.ToYear.HasValue)
        {
            query.Append($"&toYear={this.ToYear.Value}");
        }

        if (!string.IsNullOrEmpty(this.MusicFolderId))
        {
            query.Append($"&musicFolderId={this.MusicFolderId}");
        }

        return query.ToString();
    }
}
