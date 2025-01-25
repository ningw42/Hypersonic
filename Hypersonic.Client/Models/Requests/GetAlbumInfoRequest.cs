using Hypersonic.Client.Models.Requests.Base;

namespace Hypersonic.Client.Models.Requests;

public class GetAlbumInfoRequest : StringIdRequest
{
    public override string Action => Constants.Action.GetAlbumInfo;

    protected override string IdName => StringIdRequest.IdNameId;

    public GetAlbumInfoRequest(SubsonicRequest subsonicRequest, string id) : base(subsonicRequest, id) { }
}
