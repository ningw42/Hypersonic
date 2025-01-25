using Hypersonic.Client.Models.Requests.Base;

namespace Hypersonic.Client.Models.Requests;

public class GetArtistRequest : StringIdRequest
{
    public override string Action => Constants.Action.GetArtist;
    
    protected override string IdName => StringIdRequest.IdNameId;

    public GetArtistRequest(SubsonicRequest subsonicRequest, string id) : base(subsonicRequest, id) { }
}
