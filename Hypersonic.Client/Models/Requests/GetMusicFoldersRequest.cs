namespace Hypersonic.Client.Models.Requests;

public class GetMusicFoldersRequest : SubsonicRequest
{
    public override string Action => Constants.Action.GetMusicFolders;

    public GetMusicFoldersRequest(SubsonicRequest subsonicRequest)
        : base(subsonicRequest)
    {
    }
}
