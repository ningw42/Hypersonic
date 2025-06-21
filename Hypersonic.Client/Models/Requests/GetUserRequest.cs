namespace Hypersonic.Client.Models.Requests;

public class GetUserRequest : SubsonicRequest
{
    public override string Action => Constants.Action.GetUser;

    public string Username { get; set; }

    public GetUserRequest(SubsonicRequest subsonicRequest, string username)
        : base(subsonicRequest)
    {
        this.Username = username;
    }

    public override string ToQueryString()
    {
        string query = base.ToQueryString();
        query += $"&username={Uri.EscapeDataString(this.Username)}";
        return query;
    }
}
