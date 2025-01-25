using Hypersonic.Client.Models.Requests.Base;

namespace Hypersonic.Client.Models.Requests;

public class GetMusicDirectoryRequest : StringIdRequest
{
    public override string Action => Constants.Action.GetMusicDirectory;

    protected override string IdName => StringIdRequest.IdNameId;

    public GetMusicDirectoryRequest(SubsonicRequest subsonicRequest, string id) : base(subsonicRequest, id) { }
}
