using Hypersonic.Client.Models.Requests.Base;

namespace Hypersonic.Client.Models.Requests;

public class GetLyricsBySongIdRequest : StringIdRequest
{
    public override string Action => Constants.Action.GetLyricsBySongId;

    protected override string IdName => StringIdRequest.IdNameId;

    public GetLyricsBySongIdRequest(SubsonicRequest subsonicRequest, string id) : base(subsonicRequest, id) { }
}
