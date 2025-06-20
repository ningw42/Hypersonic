namespace Hypersonic.Client.Models.Requests;

public class GetAlbumList2Request : GetAlbumListRequest
{
    public override string Action => Constants.Action.GetAlbumList2;

    public GetAlbumList2Request(SubsonicRequest subsonicRequest, string type, int? size = null, int? offset = null, int? fromYear = null, int? toYear = null, string? genre = null, string? musicFolderId = null)
        : base(subsonicRequest, type, size, offset, fromYear, toYear, genre, musicFolderId)
    {
    }
}
