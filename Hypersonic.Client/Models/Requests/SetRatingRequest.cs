namespace Hypersonic.Client.Models.Requests;

public class SetRatingRequest : SubsonicRequest
{
    public override string Action => Constants.Action.SetRating;

    public string Id { get; set; }
    public int Rating { get; set; }

    public SetRatingRequest(SubsonicRequest subsonicRequest, string id, int rating)
        : base(subsonicRequest)
    {
        this.Id = id;
        this.Rating = rating;
    }

    public override string ToQueryString()
    {
        string query = base.ToQueryString();
        query += $"&id={this.Id}&rating={this.Rating}";
        return query;
    }
}
