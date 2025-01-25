namespace Hypersonic.Client.Models.Requests;

public class PingRequest : SubsonicRequest
{
    public override string Action => Constants.Action.Ping;

    public PingRequest(SubsonicRequest subsonicRequest) 
        : base(subsonicRequest)
    {
    }
}
