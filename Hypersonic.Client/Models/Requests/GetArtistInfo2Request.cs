namespace Hypersonic.Client.Models.Requests;

public class GetArtistInfo2Request : GetArtistInfoRequest
{
    public override string Action => Constants.Action.GetArtistInfo2;

    public GetArtistInfo2Request(SubsonicRequest subsonicRequest, string id, int? count, bool includeNotPresent)
        : base(subsonicRequest, id, count, includeNotPresent)
    { }
}
