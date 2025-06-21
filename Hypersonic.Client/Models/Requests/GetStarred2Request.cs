using System.Text;

namespace Hypersonic.Client.Models.Requests;

public class GetStarred2Request : SubsonicRequest
{    public string? MusicFolderId { get; set; }

    public override string Action => Constants.Action.GetStarred2;

    public GetStarred2Request(SubsonicRequest subsonicRequest, string? musicFolderId = null)
        : base(subsonicRequest)
    {
        this.MusicFolderId = musicFolderId;
    }

    public override string ToQueryString()
    {
        StringBuilder query = new StringBuilder(base.ToQueryString());

        if (!string.IsNullOrEmpty(this.MusicFolderId))
        {
            query.Append($"&musicFolderId={this.MusicFolderId}");
        }

        return query.ToString();
    }
}
