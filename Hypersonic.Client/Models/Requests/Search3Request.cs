using System.Text;

namespace Hypersonic.Client.Models.Requests;

public class Search3Request : SubsonicRequest
{
    public string Query { get; set; }

    public int? ArtistCount { get; set; }

    public int? ArtistOffset { get; set; }

    public int? AlbumCount { get; set; }

    public int? AlbumOffset { get; set; }

    public int? SongCount { get; set; }

    public int? SongOffset { get; set; }

    public string? MusicFolderId { get; set; }

    public override string Action => Constants.Action.Search3;

    public Search3Request(SubsonicRequest subsonicRequest, string query, int? artistCount, int? artistOffset, int? albumCount, int? albumOffset, int? songCount, int? songOffset, string? musicFolderId)
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

        sb.Append($"&query={this.Query}");

        if (this.ArtistCount.HasValue)
        {
            sb.Append($"&artistCount={this.ArtistCount}");
        }

        if (this.ArtistOffset.HasValue)
        {
            sb.Append($"&artistOffset={this.ArtistOffset}");
        }

        if (this.AlbumCount.HasValue)
        {
            sb.Append($"&albumCount={this.AlbumCount}");
        }

        if (this.AlbumOffset.HasValue)
        {
            sb.Append($"&albumOffset={this.AlbumOffset}");
        }

        if (this.SongCount.HasValue)
        {
            sb.Append($"&songCount={this.SongCount}");
        }

        if (this.SongOffset.HasValue)
        {
            sb.Append($"&songOffset={this.SongOffset}");
        }

        if (!string.IsNullOrEmpty(this.MusicFolderId))
        {
            sb.Append($"&musicFolderId={this.MusicFolderId}");
        }

        return sb.ToString();
    }
}
