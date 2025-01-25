using System.Text;
using Hypersonic.Client.Models.Requests.Base;

namespace Hypersonic.Client.Models.Requests;

public class GetIndexesRequest : StringIdRequest
{
    public ulong? IfModifiedSince { get; set; }

    public override string Action => Constants.Action.GetIndexes;

    protected override string IdName => StringIdRequest.IdNameMusicFolderId;

    public GetIndexesRequest(SubsonicRequest subsonicRequest, string? musicFolderId, ulong? ifModifiedSince)
        : base(subsonicRequest, musicFolderId)
    {
        this.IfModifiedSince = ifModifiedSince;
    }

    public override string ToQueryString()
    {
        StringBuilder sb = new StringBuilder(base.ToQueryString());

        if (this.IfModifiedSince.HasValue)
        {
            sb.Append($"&ifModifiedSince={this.IfModifiedSince}");
        }

        return sb.ToString();
    }
}
