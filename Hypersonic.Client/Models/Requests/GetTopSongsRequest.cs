using Hypersonic.Client.Models.Requests.Base;

namespace Hypersonic.Client.Models.Requests;

public class GetTopSongsRequest : StringIdRequest
{
    public int? Count { get; set; }

    public override string Action => Constants.Action.GetTopSongs;

    protected override string IdName => StringIdRequest.IdNameArtist;

    public GetTopSongsRequest(SubsonicRequest subsonicRequest, string artist, int? count)
        : base(subsonicRequest, artist)
    {
        this.Count = count;
    }

    public override string ToQueryString()
    {
        string query = base.ToQueryString();

        if (this.Count.HasValue)
        {
            query += $"&count={this.Count}";
        }

        return query;
    }
}
