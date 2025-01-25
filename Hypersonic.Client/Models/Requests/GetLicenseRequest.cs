namespace Hypersonic.Client.Models.Requests;

public class GetLicenseRequest : SubsonicRequest
{
    public override string Action => Constants.Action.GetLicense;

    public GetLicenseRequest(SubsonicRequest subsonicRequest)
        : base(subsonicRequest)
    {
    }
}
