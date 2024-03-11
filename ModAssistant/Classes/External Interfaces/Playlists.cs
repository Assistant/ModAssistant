using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using static ModAssistant.Http;

namespace ModAssistant.API
{
    public class Playlists
    {
        private const string BSaberURLPrefix = "https://bsaber.com/PlaylistAPI/";
        private const string PlaylistsFolder = "Playlists";
        private static readonly string BeatSaberPath = Utils.BeatSaberPath;

        public static void CreatePlaylistsFolder()
        {
            string playlistsPath = Path.Combine(BeatSaberPath, PlaylistsFolder);
            Directory.CreateDirectory(playlistsPath);
        }

        public static async Task DownloadAll(Uri uri)
        {
            switch (uri.Host)
            {
                case "playlist":
                    Uri url = new Uri($"{uri.PathAndQuery.Trim('/')}");
                    string filename = await Get(url);
                    await DownloadFrom(filename);
                    break;
            }
        }

        public static async Task<string> Get(Uri url)
        {
            try
            {
                CreatePlaylistsFolder();
                string filename = await Utils.DownloadAsset(url.ToString(), PlaylistsFolder, preferContentDisposition: true);
                return Path.Combine(BeatSaberPath, PlaylistsFolder, filename);
            }
            catch
            {
                return null;
            }
        }

        public static async Task DownloadFrom(string file)
        {
            CreatePlaylistsFolder();

            if (Path.Combine(BeatSaberPath, PlaylistsFolder) != Path.GetDirectoryName(file))
            {
                string destination = Path.Combine(BeatSaberPath, PlaylistsFolder, Path.GetFileName(file));
                File.Copy(file, destination, true);
            }

            int Errors = 0;
            int Minimum = 0;
            int Value = 0;

            Playlist playlist = JsonSerializer.Deserialize<Playlist>(File.ReadAllText(file));
            int Maximum = playlist.songs.Length;

            foreach (Playlist.Song song in playlist.songs)
            {
                API.BeatSaver.BeatSaverMap response = new BeatSaver.BeatSaverMap();
                if (!string.IsNullOrEmpty(song.hash))
                {
                    response = await BeatSaver.GetFromHash(song.hash, false);
                }
                else if (!string.IsNullOrEmpty(song.key))
                {
                    response = await BeatSaver.GetFromKey(song.key, false);
                }
                Value++;

                if (response.Success)
                {
                    Utils.SetMessage($"{string.Format((string)Application.Current.FindResource("Options:InstallingPlaylist"), TextProgress(Minimum, Maximum, Value))} {response.Name}");
                }
                else
                {
                    Utils.SetMessage($"{string.Format((string)Application.Current.FindResource("Options:FailedPlaylistSong"), song.songName)}");
                    ModAssistant.Utils.Log($"Failed installing BeatSaver map: {song.songName} | {song.key} | {song.hash} | ({response?.response?.ratelimit?.Remaining})");
                    App.CloseWindowOnFinish = false;
                    await Task.Delay(3 * 1000);
                    Errors++;
                }
            }
            Utils.SetMessage($"{string.Format((string)Application.Current.FindResource("Options:FinishedPlaylist"), Errors, playlist.playlistTitle)}");
        }

        private static string TextProgress(int min, int max, int value)
        {
            if (max == value)
            {
                return $" {string.Concat(Enumerable.Repeat("▒", 10))} [{value}/{max}]";
            }
            int interval = (int)Math.Floor((double)value / (((double)max - (double)min) / (double)10));
            return $" {string.Concat(Enumerable.Repeat("▒", interval))}{string.Concat(Enumerable.Repeat("░", 10 - interval))} [{value}/{max}]";
        }

#pragma warning disable IDE1006 // Naming Styles
        class Playlist
        {
            public string playlistTitle { get; set; }
            public string playlistAuthor { get; set; }
            public string image { get; set; }
            public Song[] songs { get; set; }

            public class Song
            {
                public string key { get; set; }
                public string hash { get; set; }
                public string songName { get; set; }
                public string uploader { get; set; }
            }
        }
    }
}
#pragma warning restore IDE1006 // Naming Styles
