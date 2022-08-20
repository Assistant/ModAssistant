using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using static ModAssistant.Http;

namespace ModAssistant
{
    public class Utils
    {
        public static bool IsAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        public static string ExePath = Process.GetCurrentProcess().MainModule.FileName;

        public class Constants
        {
            public const string BeatSaberAPPID = "620980";
            public const string BeatModsAPIUrl = "https://beatmods.com/api/v1/";
            public const string TeknikAPIUrl = "https://api.teknik.io/v1/";
            public const string BeatModsURL = "https://beatmods.com";
            public const string BeatModsVersions = "https://versions.beatmods.com/versions.json";
            public const string BeatModsAlias = "https://alias.beatmods.com/aliases.json";
            public const string WeebCDNAPIURL = "https://pat.assistant.moe/api/v1.0/";
            public const string BeatModsModsOptions = "mod?status=approved";
            public const string MD5Spacer = "                                 ";
            public static readonly char[] IllegalCharacters = new char[]
            {
                '<', '>', ':', '/', '\\', '|', '?', '*', '"',
                '\u0000', '\u0001', '\u0002', '\u0003', '\u0004', '\u0005', '\u0006', '\u0007',
                '\u0008', '\u0009', '\u000a', '\u000b', '\u000c', '\u000d', '\u000e', '\u000d',
                '\u000f', '\u0010', '\u0011', '\u0012', '\u0013', '\u0014', '\u0015', '\u0016',
                '\u0017', '\u0018', '\u0019', '\u001a', '\u001b', '\u001c', '\u001d', '\u001f',
            };
        }

        public class TeknikPasteResponse
        {
            public Result result;
            public class Result
            {
                public string id;
                public string url;
                public string title;
                public string syntax;
                public DateTime? expiration;
                public string password;
            }
        }

        public class WeebCDNRandomResponse
        {
            public int index;
            public string url;
            public string ext;
        }

        public static void SendNotify(string message, string title = null)
        {
            string defaultTitle = (string)Application.Current.FindResource("Utils:NotificationTitle");

            var notification = new System.Windows.Forms.NotifyIcon()
            {
                Visible = true,
                Icon = System.Drawing.SystemIcons.Information,
                BalloonTipTitle = title ?? defaultTitle,
                BalloonTipText = message
            };

            notification.ShowBalloonTip(5000);

            notification.Dispose();
        }

        public static void StartAsAdmin(string Arguments, bool Close = false)
        {
            using (Process process = new Process())
            {
                process.StartInfo.FileName = Process.GetCurrentProcess().MainModule.FileName;
                process.StartInfo.Arguments = Arguments;
                process.StartInfo.UseShellExecute = true;
                process.StartInfo.Verb = "runas";

                try
                {
                    process.Start();

                    if (!Close)
                    {
                        process.WaitForExit();
                    }
                }
                catch
                {
                    MessageBox.Show((string)Application.Current.FindResource("Utils:RunAsAdmin"));
                }

                if (Close) Application.Current.Shutdown();
            }
        }

        public static string CalculateMD5(string filename)
        {
            using (var md5 = MD5.Create())
            {
                using (var stream = File.OpenRead(filename))
                {
                    var hash = md5.ComputeHash(stream);
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
        }

        public static string CalculateMD5FromStream(Stream stream)
        {
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        public static string GetInstallDir()
        {
            string InstallDir = Properties.Settings.Default.InstallFolder;

            if (!string.IsNullOrEmpty(InstallDir)
                && Directory.Exists(InstallDir)
                && Directory.Exists(Path.Combine(InstallDir, "Beat Saber_Data", "Plugins"))
                && File.Exists(Path.Combine(InstallDir, "Beat Saber.exe")))
            {
                return InstallDir;
            }

            try
            {
                InstallDir = GetSteamDir();
            }
            catch { }
            if (!string.IsNullOrEmpty(InstallDir))
            {
                return InstallDir;
            }

            try
            {
                InstallDir = GetOculusDir();
            }
            catch { }
            if (!string.IsNullOrEmpty(InstallDir))
            {
                return InstallDir;
            }

            MessageBox.Show((string)Application.Current.FindResource("Utils:NoInstallFolder"));

            InstallDir = GetManualDir();
            if (!string.IsNullOrEmpty(InstallDir))
            {
                return InstallDir;
            }

            return null;
        }

        public static string SetDir(string directory, string store)
        {
            App.BeatSaberInstallDirectory = directory;
            App.BeatSaberInstallType = store;
            Pages.Options.Instance.InstallDirectory = directory;
            Pages.Options.Instance.InstallType = store;
            Properties.Settings.Default.InstallFolder = directory;
            Properties.Settings.Default.StoreType = store;
            Properties.Settings.Default.Save();
            return directory;
        }

        public static string GetSteamDir()
        {

            string SteamInstall = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)?.OpenSubKey("SOFTWARE")?.OpenSubKey("WOW6432Node")?.OpenSubKey("Valve")?.OpenSubKey("Steam")?.GetValue("InstallPath").ToString();
            if (string.IsNullOrEmpty(SteamInstall))
            {
                SteamInstall = Registry.LocalMachine.OpenSubKey("SOFTWARE")?.OpenSubKey("WOW6432Node")?.OpenSubKey("Valve")?.OpenSubKey("Steam")?.GetValue("InstallPath").ToString();
            }

            if (string.IsNullOrEmpty(SteamInstall)) return null;

            string vdf = Path.Combine(SteamInstall, @"steamapps\libraryfolders.vdf");
            if (!File.Exists(@vdf)) return null;

            Regex regex = new Regex("\\s\"(?:\\d|path)\"\\s+\"(.+)\"");
            List<string> SteamPaths = new List<string>
            {
                Path.Combine(SteamInstall, @"steamapps")
            };

            using (StreamReader reader = new StreamReader(@vdf))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    Match match = regex.Match(line);
                    if (match.Success)
                    {
                        SteamPaths.Add(Path.Combine(match.Groups[1].Value.Replace(@"\\", @"\"), @"steamapps"));
                    }
                }
            }

            regex = new Regex("\\s\"installdir\"\\s+\"(.+)\"");
            foreach (string path in SteamPaths)
            {
                if (File.Exists(Path.Combine(@path, @"appmanifest_" + Constants.BeatSaberAPPID + ".acf")))
                {
                    using (StreamReader reader = new StreamReader(Path.Combine(@path, @"appmanifest_" + Constants.BeatSaberAPPID + ".acf")))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            Match match = regex.Match(line);
                            if (match.Success)
                            {
                                if (File.Exists(Path.Combine(@path, @"common", match.Groups[1].Value, "Beat Saber.exe")))
                                {
                                    return SetDir(Path.Combine(@path, @"common", match.Groups[1].Value), "Steam");
                                }
                            }
                        }
                    }
                }
            }
            return null;
        }

        public static string GetVersion()
        {
            string filename = Path.Combine(App.BeatSaberInstallDirectory, "Beat Saber_Data", "globalgamemanagers");
            using (var stream = File.OpenRead(filename))
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                const string key = "public.app-category.games";
                int pos = 0;

                while (stream.Position < stream.Length && pos < key.Length)
                {
                    if (reader.ReadByte() == key[pos]) pos++;
                    else pos = 0;
                }

                if (stream.Position == stream.Length) // we went through the entire stream without finding the key
                    return null;

                while (stream.Position < stream.Length)
                {
                    var current = (char)reader.ReadByte();
                    if (char.IsDigit(current))
                        break;
                }

                var rewind = -sizeof(int) - sizeof(byte);
                stream.Seek(rewind, SeekOrigin.Current); // rewind to the string length

                var strlen = reader.ReadInt32();
                var strbytes = reader.ReadBytes(strlen);

                return Encoding.UTF8.GetString(strbytes);
            }
        }

        public static string GetOculusDir()
        {
            string OculusInstall = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)?.OpenSubKey("SOFTWARE")?.OpenSubKey("Wow6432Node")?.OpenSubKey("Oculus VR, LLC")?.OpenSubKey("Oculus")?.OpenSubKey("Config")?.GetValue("InitialAppLibrary").ToString();
            if (string.IsNullOrEmpty(OculusInstall)) return null;

            if (!string.IsNullOrEmpty(OculusInstall))
            {
                if (File.Exists(Path.Combine(OculusInstall, "Software", "hyperbolic-magnetism-beat-saber", "Beat Saber.exe")))
                {
                    return SetDir(Path.Combine(OculusInstall, "Software", "hyperbolic-magnetism-beat-saber"), "Oculus");
                }
            }

            // Yoinked this code from Umbranox's Mod Manager. Lot's of thanks and love for Umbra <3
            using (RegistryKey librariesKey = Registry.CurrentUser.OpenSubKey("Software")?.OpenSubKey("Oculus VR, LLC")?.OpenSubKey("Oculus")?.OpenSubKey("Libraries"))
            {
                // Oculus libraries uses GUID volume paths like this "\\?\Volume{0fea75bf-8ad6-457c-9c24-cbe2396f1096}\Games\Oculus Apps", we need to transform these to "D:\Game"\Oculus Apps"
                WqlObjectQuery wqlQuery = new WqlObjectQuery("SELECT * FROM Win32_Volume");
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(wqlQuery))
                {
                    Dictionary<string, string> guidLetterVolumes = new Dictionary<string, string>();

                    foreach (ManagementBaseObject disk in searcher.Get())
                    {
                        var diskId = ((string)disk.GetPropertyValue("DeviceID")).Substring(11, 36);
                        var diskLetter = ((string)disk.GetPropertyValue("DriveLetter")) + @"\";

                        if (!string.IsNullOrWhiteSpace(diskLetter))
                        {
                            guidLetterVolumes.Add(diskId, diskLetter);
                        }
                    }

                    // Search among the library folders
                    foreach (string libraryKeyName in librariesKey.GetSubKeyNames())
                    {
                        using (RegistryKey libraryKey = librariesKey.OpenSubKey(libraryKeyName))
                        {
                            string libraryPath = (string)libraryKey.GetValue("Path");
                            // Yoinked this code from Megalon's fix. <3
                            string GUIDLetter = guidLetterVolumes.FirstOrDefault(x => libraryPath.Contains(x.Key)).Value;
                            if (!string.IsNullOrEmpty(GUIDLetter))
                            {
                                string finalPath = Path.Combine(GUIDLetter, libraryPath.Substring(49), @"Software\hyperbolic-magnetism-beat-saber");
                                if (File.Exists(Path.Combine(finalPath, "Beat Saber.exe")))
                                {
                                    return SetDir(finalPath, "Oculus");
                                }
                            }
                        }
                    }
                }
            }

            return null;
        }

        public static string GetManualDir()
        {
            var dialog = new SaveFileDialog()
            {
                Title = (string)Application.Current.FindResource("Utils:InstallDir:DialogTitle"),
                Filter = "Directory|*.this.directory",
                FileName = "select"
            };

            if (dialog.ShowDialog() == true)
            {
                string path = dialog.FileName;
                path = path.Replace("\\select.this.directory", "");
                path = path.Replace(".this.directory", "");
                path = path.Replace("\\select.directory", "");
                if (File.Exists(Path.Combine(path, "Beat Saber.exe")))
                {
                    string store;
                    if (File.Exists(Path.Combine(path, "Beat Saber_Data", "Plugins", "steam_api64.dll"))
                       || File.Exists(Path.Combine(path, "Beat Saber_Data", "Plugins", "x86_64", "steam_api64.dll")))
                    {
                        store = "Steam";
                    }
                    else
                    {
                        store = "Oculus";
                    }
                    return SetDir(path, store);
                }
            }
            return null;
        }

        public static string GetManualFile(string filter = "", string title = "Open File")
        {
            var dialog = new OpenFileDialog()
            {
                Title = title,
                Filter = filter,
                Multiselect = false,
            };

            if (dialog.ShowDialog() == true)
            {
                return dialog.FileName;
            }
            return null;
        }

        public static bool IsVoid()
        {
            string directory = App.BeatSaberInstallDirectory;
            string pluginsDirectory = Path.Combine(directory, "Beat Saber_Data", "Plugins", "x86_64"); // newer version of bs use x86_64 for plugins folder

            if (File.Exists(Path.Combine(directory, "IGG-GAMES.COM.url")) ||
                File.Exists(Path.Combine(directory, "SmartSteamEmu.ini")) ||
                File.Exists(Path.Combine(directory, "GAMESTORRENT.CO.url")) ||
                File.Exists(Path.Combine(pluginsDirectory, "BSteam crack.dll")) ||
                File.Exists(Path.Combine(pluginsDirectory, "HUHUVR_steam_api64.dll")) ||
                Directory.GetFiles(pluginsDirectory, "*.ini", SearchOption.TopDirectoryOnly).Where(x => Path.GetFileName(x).ToLower() != "desktop.ini").Any())
                return true;
            return false;
        }

        public static byte[] StreamToArray(Stream input)
        {
            byte[] buffer = new byte[16 * 1024];
            using (MemoryStream ms = new MemoryStream())
            {
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ms.Write(buffer, 0, read);
                }
                return ms.ToArray();
            }
        }

        public static void OpenFolder(string location)
        {
            if (!location.EndsWith(Path.DirectorySeparatorChar.ToString())) location += Path.DirectorySeparatorChar;
            if (Directory.Exists(location))
            {
                try
                {
                    Process.Start(new System.Diagnostics.ProcessStartInfo()
                    {
                        FileName = location,
                        UseShellExecute = true,
                        Verb = "open"
                    });
                    return;
                }
                catch { }
            }
            MessageBox.Show($"{string.Format((string)Application.Current.FindResource("Utils:CannotOpenFolder"), location)}.");
        }

        public static void Log(string message, string severity = "LOG")
        {
            string path = Path.GetDirectoryName(ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.PerUserRoamingAndLocal).FilePath);
            string logFile = $"{path}{Path.DirectorySeparatorChar}log.log";
            File.AppendAllText(logFile, $"[{DateTime.UtcNow:yyyy-mm-dd HH:mm:ss.ffffff}][{severity.ToUpperInvariant()}] {message}\n");
        }

        public static async Task<string> Download(string link, string folder, string output, bool preferContentDisposition = false)
        {
            var resp = await HttpClient.GetAsync(link);
            var cdFilename = resp.Content.Headers.ContentDisposition?.FileName?.Trim('"');
            // Prevent path traversal
            if (cdFilename?.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                cdFilename = null;
            }

            var filename = WebUtility.UrlDecode(Path.Combine(
                folder,
                (preferContentDisposition ? cdFilename : null) ?? output
            ));

            using (var stream = await resp.Content.ReadAsStreamAsync())
            using (var fs = new FileStream(filename, FileMode.OpenOrCreate, FileAccess.Write))
            {
                await stream.CopyToAsync(fs);
            }

            return filename;
        }

        private delegate void ShowMessageBoxDelegate(string Message, string Caption);

        private static void ShowMessageBox(string Message, string Caption)
        {
            MessageBox.Show(Message, Caption);
        }

        public static void ShowMessageBoxAsync(string Message, string Caption)
        {
            ShowMessageBoxDelegate caller = new ShowMessageBoxDelegate(ShowMessageBox);
            caller.BeginInvoke(Message, Caption, null, null);
        }

        public static void ShowMessageBoxAsync(string Message)
        {
            ShowMessageBoxDelegate caller = new ShowMessageBoxDelegate(ShowMessageBox);
            caller.BeginInvoke(Message, null, null, null);
        }

        /// <summary>
        /// Attempts to write the specified string to the <see cref="System.Windows.Clipboard"/>.
        /// </summary>
        /// <param name="text">The string to be written</param>
        public static void SetClipboard(string text)
        {
            bool success = false;
            try
            {
                Clipboard.SetText(text);
                success = true;
            }
            catch (Exception)
            {
                // Swallow exceptions relating to writing data to clipboard.
            }

            // This could be placed in the try/catch block but we don't
            // want to suppress exceptions for non-clipboard operations
            if (success)
            {
                SendNotify($"Copied text to clipboard");
            }
        }
    }
}
