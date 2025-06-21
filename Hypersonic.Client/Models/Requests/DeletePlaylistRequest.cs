using Hypersonic.Client.Models.Requests.Base;

namespace Hypersonic.Client.Models.Requests;

public class DeletePlaylistRequest : StringIdRequest
{
    public override string Action => Constants.Action.DeletePlaylist;
    
    protected override string IdName => StringIdRequest.IdNameId;

    public DeletePlaylistRequest(SubsonicRequest subsonicRequest, string id) : base(subsonicRequest, id) { }
}
