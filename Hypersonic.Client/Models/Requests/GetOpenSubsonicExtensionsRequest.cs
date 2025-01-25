namespace Hypersonic.Client.Models.Requests;

public class GetOpenSubsonicExtensionsRequest : SubsonicRequest
{
    public override string Action => Constants.Action.GetOpenSubsonicExtensions;

    public GetOpenSubsonicExtensionsRequest(SubsonicRequest subsonicRequest)
        : base(subsonicRequest)
    {
    }
}
