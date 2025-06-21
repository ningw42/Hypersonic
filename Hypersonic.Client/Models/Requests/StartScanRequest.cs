namespace Hypersonic.Client.Models.Requests;

public class StartScanRequest : SubsonicRequest
{
    public override string Action => Constants.Action.StartScan;

    public StartScanRequest(SubsonicRequest subsonicRequest)
        : base(subsonicRequest)
    {
    }
}
