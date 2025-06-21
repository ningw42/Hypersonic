namespace Hypersonic.Client.Models.Requests;

public class GetUsersRequest : SubsonicRequest
{
    public override string Action => Constants.Action.GetUsers;

    public GetUsersRequest(SubsonicRequest subsonicRequest)
        : base(subsonicRequest)
    {
    }
}
