﻿using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Management;
using ModAssistant.Properties;
using System.Net;
using System.Diagnostics;
using System.Security.Principal;

namespace ModAssistant
{
    public class Utils
    {
        public static bool IsAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        public static string ExePath = Process.GetCurrentProcess().MainModule.FileName;

        public class Constants
        {
            public const string BeatSaberAppid = "620980";
            public const string BeatModsApiUrl = "https://beatmods.com/api/v1/";
            public const string BeatModsUrl = "https://beatmods.com";
            public const string BeatModsModsOptions = "mod?status=approved";
            public const string Md5Spacer = "                                 ";
        }

        public static class GameVersions
        {
            public static Dictionary<string, string> SteamVersions = new Dictionary<string, string>
            {
                {"3708884", "0.13.2"},
                {"3844832", "1.0.0" },
                {"3861357", "1.0.1" }
            };
        }

        public static void SendNotify(string message, string title = "Mod Assistant")
        {
            var notification = new System.Windows.Forms.NotifyIcon()
            {
                Visible = true,
                Icon = System.Drawing.SystemIcons.Information,
                BalloonTipTitle = title,
                BalloonTipText = message
            };

            notification.ShowBalloonTip(5000);

            notification.Dispose();
        }

        public static void StartAsAdmin(string arguments, bool close = false)
        {
            Process process = new Process();
            process.StartInfo.FileName = Process.GetCurrentProcess().MainModule.FileName;
            process.StartInfo.Arguments = arguments;
            process.StartInfo.UseShellExecute = true;
            process.StartInfo.Verb = "runas";

            try
            {
                process.Start();
                if (!close)
                    process.WaitForExit();
            }
            catch
            {
                MessageBox.Show("Mod Assistant needs to run this task as Admin. Please try again.");
            }
            if (close)
                App.Current.Shutdown();
        }

        public static string CalculateMd5(string filename)
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

        public static string GetInstallDir()
        {
            string installDir = null;
            
            installDir = Properties.Settings.Default.InstallFolder;
            if (!String.IsNullOrEmpty(installDir) && Directory.Exists(installDir))
            {
                return installDir;
            }

            try
            {
                installDir = GetSteamDir();
            }
            catch { }
            if (!String.IsNullOrEmpty(installDir))
            {
                return installDir;
            }

            try
            {
                installDir = GetOculusDir();
            }
            catch { }
            if (!String.IsNullOrEmpty(installDir))
            {
                return installDir;
            }

            MessageBox.Show("Could not detect your Beat Saber install folder. Please select it manually.");

            installDir = GetManualDir();
            if (!String.IsNullOrEmpty(installDir))
            {
                return installDir;
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

            string steamInstall = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)?.OpenSubKey("SOFTWARE")?.OpenSubKey("WOW6432Node")?.OpenSubKey("Valve")?.OpenSubKey("Steam")?.GetValue("InstallPath").ToString();
            if (String.IsNullOrEmpty(steamInstall))
            {
                steamInstall = Registry.LocalMachine.OpenSubKey("SOFTWARE")?.OpenSubKey("WOW6432Node")?.OpenSubKey("Valve")?.OpenSubKey("Steam")?.GetValue("InstallPath").ToString();
            }
            if (String.IsNullOrEmpty(steamInstall)) return null;

            string vdf = Path.Combine(steamInstall, @"steamapps\libraryfolders.vdf");
            if (!File.Exists(@vdf)) return null;

            Regex regex = new Regex("\\s\"\\d\"\\s+\"(.+)\"");
            List<string> steamPaths = new List<string>();
            steamPaths.Add(Path.Combine(steamInstall, @"steamapps"));

            using (StreamReader reader = new StreamReader(@vdf))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    Match match = regex.Match(line);
                    if (match.Success)
                    {
                        steamPaths.Add(Path.Combine(match.Groups[1].Value.Replace(@"\\", @"\"), @"steamapps"));
                    }
                }
            }

            regex = new Regex("\\s\"installdir\"\\s+\"(.+)\"");
            foreach (string path in steamPaths)
            {
                if (File.Exists(Path.Combine(@path, @"appmanifest_" + Constants.BeatSaberAppid + ".acf")))
                {
                    using (StreamReader reader = new StreamReader(Path.Combine(@path,  @"appmanifest_" + Constants.BeatSaberAppid + ".acf")))
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

        public static string GetSteamVersion()
        {
            string steamInstall = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)?.OpenSubKey("SOFTWARE")?.OpenSubKey("WOW6432Node")?.OpenSubKey("Valve")?.OpenSubKey("Steam")?.GetValue("InstallPath").ToString();
            if (String.IsNullOrEmpty(steamInstall))
            {
                steamInstall = Registry.LocalMachine.OpenSubKey("SOFTWARE")?.OpenSubKey("WOW6432Node")?.OpenSubKey("Valve")?.OpenSubKey("Steam")?.GetValue("InstallPath").ToString();
            }
            if (String.IsNullOrEmpty(steamInstall)) return null;

            string vdf = Path.Combine(steamInstall, @"steamapps\libraryfolders.vdf");
            if (!File.Exists(@vdf)) return null;

            Regex regex = new Regex("\\s\"\\d\"\\s+\"(.+)\"");
            List<string> steamPaths = new List<string>();
            steamPaths.Add(Path.Combine(steamInstall, @"steamapps"));

            using (StreamReader reader = new StreamReader(@vdf))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    Match match = regex.Match(line);
                    if (match.Success)
                    {
                        steamPaths.Add(Path.Combine(match.Groups[1].Value.Replace(@"\\", @"\"), @"steamapps"));
                    }
                }
            }

            regex = new Regex("\\s\"buildid\"\\s+\"(.+)\"");
            foreach (string path in steamPaths)
            {
                if (File.Exists(Path.Combine(@path, @"appmanifest_" + Constants.BeatSaberAppid + ".acf")))
                {
                    using (StreamReader reader = new StreamReader(Path.Combine(@path, @"appmanifest_" + Constants.BeatSaberAppid + ".acf")))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            Match match = regex.Match(line);
                            if (match.Success)
                            {
                                string version;
                                GameVersions.SteamVersions.TryGetValue(match.Groups[1].Value, out version);
                                return version ?? "";
                            }
                        }
                    }
                }
            }
            return null;
        }

        public static string GetOculusDir()
        {
            string oculusInstall = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)?.OpenSubKey("SOFTWARE")?.OpenSubKey("Wow6432Node")?.OpenSubKey("Oculus VR, LLC")?.OpenSubKey("Oculus")?.OpenSubKey("Config")?.GetValue("InitialAppLibrary").ToString();
            if (String.IsNullOrEmpty(oculusInstall)) return null;

            if (!String.IsNullOrEmpty(oculusInstall))
            {
                if (File.Exists(Path.Combine(oculusInstall, "Software", "hyperbolic-magnetism-beat-saber", "Beat Saber.exe")))
                {
                    return SetDir(Path.Combine(oculusInstall, "Software", "hyperbolic-magnetism-beat-saber"), "Oculus");
                }
            }

            // Yoinked this code from Umbranox's Mod Manager. Lot's of thanks and love for Umbra <3
            using (RegistryKey librariesKey = Registry.CurrentUser.OpenSubKey("Software")?.OpenSubKey("Oculus VR, LLC")?.OpenSubKey("Oculus")?.OpenSubKey("Libraries"))
            {
                // Oculus libraries uses GUID volume paths like this "\\?\Volume{0fea75bf-8ad6-457c-9c24-cbe2396f1096}\Games\Oculus Apps", we need to transform these to "D:\Game"\Oculus Apps"
                WqlObjectQuery wqlQuery = new WqlObjectQuery("SELECT * FROM Win32_Volume");
                ManagementObjectSearcher searcher = new ManagementObjectSearcher(wqlQuery);
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
                        string guidLetter = guidLetterVolumes.FirstOrDefault(x => libraryPath.Contains(x.Key)).Value;
                        if (!String.IsNullOrEmpty(guidLetter))
                        {
                            string finalPath = Path.Combine(guidLetter, libraryPath.Substring(49), @"Software\hyperbolic-magnetism-beat-saber");
                            if (File.Exists(Path.Combine(finalPath, "Beat Saber.exe")))
                            {
                                return SetDir(finalPath, "Oculus");
                            }
                        }
                    }
                }
            }
            return null;
        }
        /*
        public static string GetManualDir()
        {

            CommonOpenFileDialog dialog = new CommonOpenFileDialog()
            {
                IsFolderPicker = true,
                Multiselect = false,
                Title = "Select your Beat Saber installation folder"
            };

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                return dialog.FileName;
            }

            return null;
        }*/

        public static string GetManualDir()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog()
            {
                Title = "Select your Beat Saber install folder",
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
                    if (File.Exists(Path.Combine(path, "Beat Saber_Data", "Plugins", "steam_api64.dll")))
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

        public static bool IsVoid()
        {
            string directory = App.BeatSaberInstallDirectory;

            if (File.Exists(Path.Combine(directory, "IGG-GAMES.COM.url")) ||
                File.Exists(Path.Combine(directory, "SmartSteamEmu.ini")) ||
                File.Exists(Path.Combine(directory, "GAMESTORRENT.CO.url")) ||
                File.Exists(Path.Combine(directory, "Beat Saber_Data", "Plugins", "BSteam crack.dll")) ||
                File.Exists(Path.Combine(directory, "Beat Saber_Data", "Plugins", "HUHUVR_steam_api64.dll")) ||
                Directory.GetFiles(Path.Combine(directory, "Beat Saber_Data", "Plugins"), "*.ini", SearchOption.TopDirectoryOnly).Length > 0)
                return true;
            return false;
        }

        public static void Download(string link, string output)
        {
            WebClient webClient = new WebClient();
            webClient.Headers.Add("user-agent", "ModAssistant/" + App.Version);

            byte[] file = webClient.DownloadData(link);
            File.WriteAllBytes(output, file);
        }

        private delegate void ShowMessageBoxDelegate(string message, string caption);

        private static void ShowMessageBox(string message, string caption)
        {
            MessageBox.Show(message, caption);
        }

        public static void ShowMessageBoxAsync(string message, string caption)
        {
            ShowMessageBoxDelegate caller = new ShowMessageBoxDelegate(ShowMessageBox);
            caller.BeginInvoke(message, caption, null, null);
        }

        public static void ShowMessageBoxAsync(string message)
        {
            ShowMessageBoxDelegate caller = new ShowMessageBoxDelegate(ShowMessageBox);
            caller.BeginInvoke(message, null, null, null);
        }
    }
}
