using Hypersonic.Client.Models.Requests.Base;

namespace Hypersonic.Client.Models.Requests;

public class GetAvatarRequest : StringIdRequest
{
    public override string Action => Constants.Action.GetAvatar;

    protected override string IdName => "username";

    public GetAvatarRequest(SubsonicRequest subsonicRequest, string username) : base(subsonicRequest, username) { }
}
