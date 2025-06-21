namespace Hypersonic.Client.Models.Requests;

public class GetNowPlayingRequest : SubsonicRequest
{
    public override string Action => Constants.Action.GetNowPlaying;

    public GetNowPlayingRequest(SubsonicRequest subsonicRequest)
        : base(subsonicRequest)
    {
    }
}
