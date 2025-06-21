using Hypersonic.Client.Models.Requests.Base;

namespace Hypersonic.Client.Models.Requests;

public class GetPlaylistRequest : StringIdRequest
{
    public override string Action => Constants.Action.GetPlaylist;
    
    protected override string IdName => StringIdRequest.IdNameId;

    public GetPlaylistRequest(SubsonicRequest subsonicRequest, string id) : base(subsonicRequest, id) { }
}
