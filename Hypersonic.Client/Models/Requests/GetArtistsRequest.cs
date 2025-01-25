using Hypersonic.Client.Models.Requests.Base;

namespace Hypersonic.Client.Models.Requests;

public class GetArtistsRequest : StringIdRequest
{
    public override string Action => Constants.Action.GetArtists;

    protected override string IdName => StringIdRequest.IdNameMusicFolderId;

    public GetArtistsRequest(SubsonicRequest subsonicRequest, string? musicFolderId) : base(subsonicRequest, musicFolderId) { }
}
