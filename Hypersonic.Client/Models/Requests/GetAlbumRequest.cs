using Hypersonic.Client.Models.Requests.Base;

namespace Hypersonic.Client.Models.Requests;

public class GetAlbumRequest : StringIdRequest
{
    public override string Action => Constants.Action.GetAlbum;

    protected override string IdName => StringIdRequest.IdNameId;

    public GetAlbumRequest(SubsonicRequest subsonicRequest, string id) : base(subsonicRequest, id) { }
}
