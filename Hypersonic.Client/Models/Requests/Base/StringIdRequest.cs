using Microsoft.VisualBasic;

namespace Hypersonic.Client.Models.Requests.Base;

public abstract class StringIdRequest : SubsonicRequest
{
    public const string IdNameId = "id";

    public const string IdNameMusicFolderId = "musicFolderId";

    public const string IdNameArtist = "artist";

    public string? Id { get; set; }

    protected abstract string IdName { get; }

    public StringIdRequest(SubsonicRequest subsonicRequest, string? id)
        : base(subsonicRequest)
    {
        this.Id = id;
    }

    public override string ToQueryString()
    {
        string query = base.ToQueryString();

        if (!string.IsNullOrEmpty(this.Id))
        {
            query += $"&{this.IdName}={this.Id}";
        }

        return query;
    }
}
