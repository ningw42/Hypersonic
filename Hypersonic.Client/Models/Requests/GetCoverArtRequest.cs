using System.Text;
using Hypersonic.Client.Models.Requests.Base;

namespace Hypersonic.Client.Models.Requests;

public class GetCoverArtRequest : StringIdRequest
{
    public string? Size { get; set; }

    public override string Action => Constants.Action.GetCoverArt;

    protected override string IdName => StringIdRequest.IdNameId;

    public GetCoverArtRequest(SubsonicRequest subsonicRequest, string id, string? size = null)
        : base(subsonicRequest, id)
    {
        this.Size = size;
    }

    public override string ToQueryString()
    {
        StringBuilder sb = new StringBuilder(base.ToQueryString());

        if (!string.IsNullOrEmpty(this.Size))
        {
            sb.Append($"&size={this.Size}");
        }

        return sb.ToString();
    }
}
