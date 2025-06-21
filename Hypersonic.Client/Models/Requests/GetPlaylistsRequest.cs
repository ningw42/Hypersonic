namespace Hypersonic.Client.Models.Requests;

public class GetPlaylistsRequest : SubsonicRequest
{
    public override string Action => Constants.Action.GetPlaylists;

    public string? Username { get; set; }

    public GetPlaylistsRequest(SubsonicRequest subsonicRequest, string? username = null)
        : base(subsonicRequest)
    {
        this.Username = username;
    }

    public override string ToQueryString()
    {
        string query = base.ToQueryString();

        if (!string.IsNullOrEmpty(this.Username))
        {
            query += $"&username={this.Username}";
        }

        return query;
    }
}
