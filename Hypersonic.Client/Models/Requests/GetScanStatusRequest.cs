namespace Hypersonic.Client.Models.Requests;

public class GetScanStatusRequest : SubsonicRequest
{
    public override string Action => Constants.Action.GetScanStatus;

    public GetScanStatusRequest(SubsonicRequest subsonicRequest)
        : base(subsonicRequest)
    {
    }
}
