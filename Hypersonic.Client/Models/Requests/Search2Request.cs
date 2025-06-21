using System.Text;

namespace Hypersonic.Client.Models.Requests;

public class Search2Request : SubsonicRequest
{
    public string Query { get; set; }

    public int? ArtistCount { get; set; }

    public int? ArtistOffset { get; set; }

    public int? AlbumCount { get; set; }

    public int? AlbumOffset { get; set; }

    public int? SongCount { get; set; }

    public int? SongOffset { get; set; }

    public string? MusicFolderId { get; set; }

    public override string Action => Constants.Action.Search2;

    public Search2Request(SubsonicRequest subsonicRequest, string query, int? artistCount = null, int? artistOffset = null, int? albumCount = null, int? albumOffset = null, int? songCount = null, int? songOffset = null, string? musicFolderId = null)
        : base(subsonicRequest)
    {
        this.Query = query;
        this.ArtistCount = artistCount;
        this.ArtistOffset = artistOffset;
        this.AlbumCount = albumCount;
        this.AlbumOffset = albumOffset;
        this.SongCount = songCount;
        this.SongOffset = songOffset;
        this.MusicFolderId = musicFolderId;
    }

    public override string ToQueryString()
    {
        StringBuilder sb = new StringBuilder(base.ToQueryString());

        sb.Append($"&query={Uri.EscapeDataString(this.Query)}");

        if (this.ArtistCount.HasValue)
        {
            sb.Append($"&artistCount={this.ArtistCount.Value}");
        }

        if (this.ArtistOffset.HasValue)
        {
            sb.Append($"&artistOffset={this.ArtistOffset.Value}");
        }

        if (this.AlbumCount.HasValue)
        {
            sb.Append($"&albumCount={this.AlbumCount.Value}");
        }

        if (this.AlbumOffset.HasValue)
        {
            sb.Append($"&albumOffset={this.AlbumOffset.Value}");
        }

        if (this.SongCount.HasValue)
        {
            sb.Append($"&songCount={this.SongCount.Value}");
        }

        if (this.SongOffset.HasValue)
        {
            sb.Append($"&songOffset={this.SongOffset.Value}");
        }

        if (!string.IsNullOrEmpty(this.MusicFolderId))
        {
            sb.Append($"&musicFolderId={this.MusicFolderId}");
        }

        return sb.ToString();
    }
}
