using System.Text;

namespace Hypersonic.Client.Models.Requests;

public class GetAlbumListRequest : SubsonicRequest
{
    public string Type { get; set; }

    public int? Size { get; set; }

    public int? Offset { get; set; }

    public int? FromYear { get; set; }

    public int? ToYear { get; set; }

    public string? Genre { get; set; }

    public string? MusicFolderId { get; set; }

    public override string Action => Constants.Action.GetAlbumList;

    public GetAlbumListRequest(SubsonicRequest subsonicRequest, string type, int? size = null, int? offset = null, int? fromYear = null, int? toYear = null, string? genre = null, string? musicFolderId = null)
        : base(subsonicRequest)
    {
        this.Type = type;
        this.Size = size;
        this.Offset = offset;
        this.FromYear = fromYear;
        this.ToYear = toYear;
        this.Genre = genre;
        this.MusicFolderId = musicFolderId;
    }

    public override string ToQueryString()
    {
        StringBuilder sb = new StringBuilder(base.ToQueryString());

        sb.Append($"&type={this.Type}");

        if (this.Size.HasValue)
        {
            sb.Append($"&size={this.Size}");
        }

        if (this.Offset.HasValue)
        {
            sb.Append($"&offset={this.Offset}");
        }

        if (this.FromYear.HasValue)
        {
            sb.Append($"&fromYear={this.FromYear}");
        }

        if (this.ToYear.HasValue)
        {
            sb.Append($"&toYear={this.ToYear}");
        }

        if (!string.IsNullOrEmpty(this.Genre))
        {
            sb.Append($"&genre={this.Genre}");
        }

        if (!string.IsNullOrEmpty(this.MusicFolderId))
        {
            sb.Append($"&musicFolderId={this.MusicFolderId}");
        }

        return sb.ToString();
    }
}
