using Hypersonic.Client.Models.Requests.Base;

namespace Hypersonic.Client.Models.Requests;

public class GetSimilarSongsRequest : StringIdRequest
{
    public int? Count { get; set; }

    public override string Action => Constants.Action.GetSimilarSongs;

    protected override string IdName => StringIdRequest.IdNameId;

    public GetSimilarSongsRequest(SubsonicRequest subsonicRequest, string id, int? count)
        : base(subsonicRequest, id)
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
