using Hypersonic.Client.Models.Requests.Base;

namespace Hypersonic.Client.Models.Requests;

public class DownloadRequest : StringIdRequest
{
    public override string Action => Constants.Action.Download;

    protected override string IdName => StringIdRequest.IdNameId;

    public DownloadRequest(SubsonicRequest subsonicRequest, string id) : base(subsonicRequest, id) { }
}
