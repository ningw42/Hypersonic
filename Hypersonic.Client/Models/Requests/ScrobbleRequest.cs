namespace Hypersonic.Client.Models.Requests;

public class ScrobbleRequest : SubsonicRequest
{
    public override string Action => Constants.Action.Scrobble;

    public string Id { get; set; }
    public ulong? Time { get; set; }
    public bool Submission { get; set; }

    public ScrobbleRequest(SubsonicRequest subsonicRequest, string id, ulong? time = null, bool submission = true)
        : base(subsonicRequest)
    {
        this.Id = id;
        this.Time = time;
        this.Submission = submission;
    }

    public override string ToQueryString()
    {
        string query = base.ToQueryString();
        query += $"&id={this.Id}";

        if (this.Time.HasValue)
        {
            query += $"&time={this.Time.Value}";
        }

        query += $"&submission={this.Submission.ToString().ToLower()}";

        return query;
    }
}
