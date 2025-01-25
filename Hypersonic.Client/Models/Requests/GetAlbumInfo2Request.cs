namespace Hypersonic.Client.Models.Requests;

public class GetAlbumInfo2Request : GetAlbumInfoRequest
{
    public override string Action => Constants.Action.GetAlbumInfo2;

    public GetAlbumInfo2Request(SubsonicRequest subsonicRequest, string id) : base(subsonicRequest, id) { }
}
