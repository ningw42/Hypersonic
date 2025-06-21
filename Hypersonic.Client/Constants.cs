namespace Hypersonic.Client;
public class Constants
{
    public const int SaltLength = 8;

    public class Action
    {
        #region System

        public const string Ping = "ping.view";
        public const string GetLicense = "getLicense.view";
        public const string GetOpenSubsonicExtensions = "getOpenSubsonicExtensions.view";

        #endregion System

        #region Browsing

        public const string GetMusicFolders = "getMusicFolders.view";
        public const string GetIndexes = "getIndexes.view";
        public const string GetMusicDirectory = "getMusicDirectory.view";
        public const string GetGenres = "getGenres.view";
        public const string GetArtists = "getArtists.view";
        public const string GetArtist = "getArtist.view";
        public const string GetAlbum = "getAlbum.view";
        public const string GetSong = "getSong.view";
        public const string GetVideos = "getVideos.view";
        public const string GetVideoInfo = "getVideoInfo.view";
        public const string GetArtistInfo = "getArtistInfo.view";
        public const string GetArtistInfo2 = "getArtistInfo2.view";
        public const string GetAlbumInfo = "getAlbumInfo.view";
        public const string GetAlbumInfo2 = "getAlbumInfo2.view";
        public const string GetSimilarSongs = "getSimilarSongs.view";
        public const string GetSimilarSongs2 = "getSimilarSongs2.view";
        public const string GetTopSongs = "getTopSongs.view";

        #endregion Browsing

        #region Album/Song Lists

        public const string GetAlbumList = "getAlbumList.view";
        public const string GetAlbumList2 = "getAlbumList2.view";
        public const string GetRandomSongs = "getRandomSongs.view";
        public const string GetSongsByGenre = "getSongsByGenre.view";
        public const string GetNowPlaying = "getNowPlaying.view";
        public const string GetStarred = "getStarred.view";
        public const string GetStarred2 = "getStarred2.view";

        #endregion Album/Song Lists

        #region Searching
        
        public const string Search2 = "search2.view";
        public const string Search3 = "search3.view";

        #endregion Searching

        #region Playlists

        public const string GetPlaylists = "getPlaylists.view";
        public const string GetPlaylist = "getPlaylist.view";
        public const string CreatePlaylist = "createPlaylist.view";
        public const string UpdatePlaylist = "updatePlaylist.view";
        public const string DeletePlaylist = "deletePlaylist.view";

        #endregion Playlists

        #region Media Retrieval

        public const string Stream = "stream.view";
        public const string Download = "download.view";
        public const string HLS = "hls.m3u8";
        public const string GetCaptions = "getCaptions.view";
        public const string GetCoverArt = "getCoverArt.view";
        public const string GetLyrics = "getLyrics.view";
        public const string GetLyricsBySongId = "getLyricsBySongId.view";
        public const string GetAvatar = "getAvatar.view";

        #endregion Media Retrieval

        #region Media Annotation

        public const string Star = "star.view";
        public const string Unstar = "unstar.view";
        public const string SetRating = "setRating.view";
        public const string Scrobble = "scrobble.view";

        #endregion Media Annotation

        #region Sharing

        public const string GetShares = "getShares.view";
        public const string CreateShare = "createShare.view";
        public const string UpdateShare = "updateShare.view";
        public const string DeleteShare = "deleteShare.view";

        #endregion Sharing

        #region Podcast

        public const string GetPodcasts = "getPodcasts.view";
        public const string GetNewestPodcasts = "getNewestPodcasts.view";
        public const string RefreshPodcasts = "refreshPodcasts.view";
        public const string CreatePodcastChannel = "createPodcastChannel.view";
        public const string DeletePodcastChannel = "deletePodcastChannel.view";
        public const string DeletePodcastEpisode = "deletePodcastEpisode.view";
        public const string DownloadPodcastEpisode = "downloadPodcastEpisode.view";

        #endregion Podcast

        #region Jukebox

        public const string JukeboxControl = "jukeboxControl.view";

        #endregion Jukebox

        #region Internet Radio
        
        public const string GetInternetRadioStations = "getInternetRadioStations.view";
        public const string CreateInternetRadioStation = "createInternetRadioStation.view";
        public const string UpdateInternetRadioStation = "updateInternetRadioStation.view";
        public const string DeleteInternetRadioStation = "deleteInternetRadioStation.view";

        #endregion Internet Radio

        #region Chat

        public const string GetChatMessages = "getChatMessages.view";
        public const string AddChatMessage = "addChatMessage.view";

        #endregion Chat

        #region User Management

        public const string GetUser = "getUser.view";
        public const string GetUsers = "getUsers.view";
        public const string CreateUser = "createUser.view";
        public const string UpdateUser = "updateUser.view";
        public const string DeleteUser = "deleteUser.view";
        public const string ChangePassword = "changePassword.view";

        #endregion User Management

        #region Bookmarks

        public const string GetBookmarks = "getBookmarks.view";
        public const string CreateBookmark = "createBookmark.view";
        public const string DeleteBookmark = "deleteBookmark.view";
        public const string GetPlayQueue = "getPlayQueue.view";
        public const string SavePlayQueue = "savePlayQueue.view";

        #endregion Bookmarks

        #region Media Library Scanning

        public const string GetScanStatus = "getScanStatus.view";
        public const string StartScan = "startScan.view";

        #endregion Media Library Scanning
    }
}
