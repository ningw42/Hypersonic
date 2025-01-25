namespace Hypersonic.Client.Models.Requests;

public class GetSimilarSongs2Request : GetSimilarSongsRequest
{
    public override string Action => Constants.Action.GetSimilarSongs2;

    public GetSimilarSongs2Request(SubsonicRequest subsonicRequest, string id, int? count) : base(subsonicRequest, id, count) {}
}
