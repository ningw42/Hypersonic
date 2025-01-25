using Hypersonic.Client.Models.Requests.Base;

namespace Hypersonic.Client.Models.Requests;

public class GetArtistInfoRequest : StringIdRequest
{
    public int? Count { get; set; }

    public bool IncludeNotPresent { get; set; }

    public override string Action => Constants.Action.GetArtistInfo;

    protected override string IdName => StringIdRequest.IdNameId;

    public GetArtistInfoRequest(SubsonicRequest subsonicRequest, string id, int? count, bool includeNotPresent) 
        : base(subsonicRequest, id)
    {
        this.Count = count;
        this.IncludeNotPresent = includeNotPresent;
    }
}
