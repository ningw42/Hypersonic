using Hypersonic.Client.Models.Requests.Base;

namespace Hypersonic.Client.Models.Requests;

public class GetSongRequest : StringIdRequest
{
    public override string Action => Constants.Action.GetSong;

    protected override string IdName => StringIdRequest.IdNameId;

    public GetSongRequest(SubsonicRequest subsonicRequest, string id) : base(subsonicRequest, id) { }
}
