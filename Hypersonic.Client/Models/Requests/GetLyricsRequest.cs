using System.Text;

namespace Hypersonic.Client.Models.Requests;

public class GetLyricsRequest : SubsonicRequest
{
    public string? Artist { get; set; }

    public string? Title { get; set; }

    public override string Action => Constants.Action.GetLyrics;

    public GetLyricsRequest(SubsonicRequest subsonicRequest, string? artist = null, string? title = null)
        : base(subsonicRequest)
    {
        this.Artist = artist;
        this.Title = title;
    }

    public override string ToQueryString()
    {
        StringBuilder sb = new StringBuilder(base.ToQueryString());

        if (!string.IsNullOrEmpty(this.Artist))
        {
            sb.Append($"&artist={this.Artist}");
        }

        if (!string.IsNullOrEmpty(this.Title))
        {
            sb.Append($"&title={this.Title}");
        }

        return sb.ToString();
    }
}
