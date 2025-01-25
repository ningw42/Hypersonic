namespace Hypersonic.Client.Models.Requests;

public class GetGenresRequest : SubsonicRequest
{
    public override string Action => Constants.Action.GetGenres;

    public GetGenresRequest(SubsonicRequest subsonicRequest)
        : base(subsonicRequest)
    {
    }
}
