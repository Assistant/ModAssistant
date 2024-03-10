using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Forms;
using System.Windows.Media.Animation;
using System.Windows.Navigation;
using ModAssistant.Libs;
using static ModAssistant.Http;
using TextBox = System.Windows.Controls.TextBox;

namespace ModAssistant.Pages
{
    /// <summary>
    /// Interaction logic for Mods.xaml
    /// </summary>
    public sealed partial class Mods : Page
    {
        public static Mods Instance = new Mods();

        public List<string> DefaultMods = new List<string> { "SongCore", "WhyIsThereNoLeaderboard", "BeatSaverDownloader", "BeatSaverVoting", "PlaylistManager" };
        public Mod[] ModsList;
        public Mod[] AllModsList;
        public static List<Mod> InstalledMods = new List<Mod>();
        public static List<Mod> ManifestsToMatch = new List<Mod>();
        public List<string> CategoryNames = new List<string>();
        public static List<string> MissingOldMods = new List<string>();
        public CollectionView view;
        public bool PendingChanges;

        private readonly SemaphoreSlim _modsLoadSem = new SemaphoreSlim(1, 1);

        public List<ModListItem> ModList { get; set; }

        public Mods()
        {
            InitializeComponent();
        }

        private void RefreshModsList()
        {
            if (view != null)
            {
                view.Refresh();
            }
        }

        public void RefreshColumns()
        {
            if (MainWindow.Instance.Main.Content != Instance) return;
            double viewWidth = ModsListView.ActualWidth;
            double totalSize = 0;
            GridViewColumn description = null;

            if (ModsListView.View is GridView grid)
            {
                foreach (var column in grid.Columns)
                {
                    if (column.Header?.ToString() == FindResource("Mods:Header:Description").ToString())
                    {
                        description = column;
                    }
                    else
                    {
                        totalSize += column.ActualWidth;
                    }
                    if (double.IsNaN(column.Width))
                    {
                        column.Width = column.ActualWidth;
                        column.Width = double.NaN;
                    }
                }
                double descriptionNewWidth = viewWidth - totalSize - 35;
                description.Width = descriptionNewWidth > 200 ? descriptionNewWidth : 200;
            }
        }

        public async Task LoadMods()
        {
            var versionLoadSuccess = await MainWindow.Instance.VersionLoadStatus.Task;
            if (versionLoadSuccess == false) return;

            await _modsLoadSem.WaitAsync();

            try
            {
                MainWindow.Instance.InstallButton.IsEnabled = false;
                MainWindow.Instance.GameVersionsBox.IsEnabled = false;
                MainWindow.Instance.InfoButton.IsEnabled = false;

                if (ModsList != null)
                {
                    Array.Clear(ModsList, 0, ModsList.Length);
                }

                if (AllModsList != null)
                {
                    Array.Clear(AllModsList, 0, AllModsList.Length);
                }

                InstalledMods = new List<Mod>();
                CategoryNames = new List<string>();
                ModList = new List<ModListItem>();

                ModsListView.Visibility = Visibility.Hidden;

                if (App.CheckInstalledMods)
                {
                    MainWindow.Instance.MainText = $"{FindResource("Mods:CheckingInstalledMods")}...";
                    await Task.Run(async () => await CheckInstalledMods());
                    InstalledColumn.Width = double.NaN;
                    UninstallColumn.Width = 70;
                    DescriptionColumn.Width = 750;
                }
                else
                {
                    InstalledColumn.Width = 0;
                    UninstallColumn.Width = 0;
                    DescriptionColumn.Width = 800;
                }

                string lastModdedVersion = CheckPreviousInstallDirs();
                if (lastModdedVersion != null &&
                    !DirectoryContainsMods(Path.Combine(App.BeatSaberInstallDirectory, "Plugins")) &&
                    !DirectoryContainsMods(Path.Combine(App.BeatSaberInstallDirectory, "IPA/Pending/Plugins")))
                {
                    string body = (string)FindResource("Mods:GameUpdatedPrompt:OkCancel");
                    string title = (string)FindResource("Mods:GameUpdatedPrompt:Title");

                    var reinstallMods = System.Windows.Forms.MessageBox.Show(body, title, MessageBoxButtons.OKCancel) == DialogResult.OK;
                    if (reinstallMods) {
                        MainWindow.Instance.MainText = $"{FindResource("Mods:CheckingPreviousMods")}...";
                        await Task.Run(async () => await SelectPreviousMods(lastModdedVersion));
                        InstalledColumn.Width = double.NaN;
                        UninstallColumn.Width = 70;
                        DescriptionColumn.Width = 750;
                        if (MissingOldMods.Count > 0) {
                            string notSelectedTitle = string.Format((string)FindResource("Mods:FailedToSelect:Title"), MissingOldMods.Count);
                            string notSelectedBody1 = (string)FindResource("Mods:FailedToSelect:Body1");
                            string notSelectedBody2 = (string)FindResource("Mods:FailedToSelect:Body2");

                            System.Windows.Forms.MessageBox.Show($"{notSelectedBody1}\n{string.Join(",\n", MissingOldMods)}\n\n{notSelectedBody2}", notSelectedTitle, MessageBoxButtons.OK);
                        }
                    }
                }

                MainWindow.Instance.MainText = $"{FindResource("Mods:LoadingMods")}...";
                await Task.Run(async () => await PopulateModsList());

                ModsListView.ItemsSource = ModList;

                try
                {
                    var manualCategories = new string[] { "Core", "Leaderboards" };

                    ModList.Sort((a, b) =>
                    {
                        foreach (var category in manualCategories)
                        {
                            if (a.Category == category && b.Category == category) return 0;
                            if (a.Category == category) return -1;
                            if (b.Category == category) return 1;
                        }

                        var categoryCompare = a.Category.CompareTo(b.Category);
                        if (categoryCompare != 0) return categoryCompare;

                        var aRequired = !a.IsEnabled;
                        var bRequired = !b.IsEnabled;

                        if (a.ModRequired && !b.ModRequired) return -1;
                        if (b.ModRequired && !a.ModRequired) return 1;

                        return a.ModName.CompareTo(b.ModName);
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }

                view = (CollectionView)CollectionViewSource.GetDefaultView(ModsListView.ItemsSource);
                PropertyGroupDescription groupDescription = new PropertyGroupDescription("Category");
                view.GroupDescriptions.Add(groupDescription);

                this.DataContext = this;

                RefreshModsList();
                ModsListView.Visibility = ModList.Count == 0 ? Visibility.Hidden : Visibility.Visible;
                NoModsGrid.Visibility = ModList.Count == 0 ? Visibility.Visible : Visibility.Hidden;

                MainWindow.Instance.MainText = $"{FindResource("Mods:FinishedLoadingMods")}.";
                MainWindow.Instance.InstallButton.IsEnabled = ModList.Count != 0;
                MainWindow.Instance.GameVersionsBox.IsEnabled = true;
            }
            finally
            {
                _modsLoadSem.Release();
            }
        }

        public async Task CheckInstalledMods()
        {
            await GetAllMods();

            GetBSIPAVersion();
            CheckInstallDir("IPA/Pending/Plugins");
            CheckInstallDir("IPA/Pending/Libs");
            CheckInstallDir("Plugins");
            CheckInstallDir("Libs");
        }

        public async Task SelectPreviousMods(string previousModsDirectory)
        {
            if(AllModsList == null) await GetAllMods();

            CheckInstallDir(previousModsDirectory, true);
            CheckInstallDir("Libs");
            //CheckPreviousInstallDirs();
        }

        public async Task GetAllMods()
        {
            var resp = await HttpClient.GetAsync(Utils.Constants.BeatModsAPIUrl + "mod");
            var body = await resp.Content.ReadAsStringAsync();

            try
            {
                AllModsList = JsonSerializer.Deserialize<Mod[]>(body);
            }
            catch (Exception e)
            {
                System.Windows.MessageBox.Show($"{FindResource("Mods:LoadFailed")}.\n\n" + e);
                AllModsList = new Mod[] { };
            }
        }

        private SemVersion SemverForPluginFolder(string directory)
        {
            string versionString = directory.Substring(directory.LastIndexOf("Old") + 3, directory.Length - directory.LastIndexOf("Plugins")).Trim();
            SemVersion semver;
            if (!SemVersion.TryParse(versionString, out semver)) return null;
            return semver;
        }

        private int CompareInstallDirs(string a, string b)
        {
            SemVersion semverA = SemverForPluginFolder(a);
            SemVersion semverB = SemverForPluginFolder(b);
            if (semverA == null) return 1;
            else if(semverB == null) return 0;

            return semverB.CompareTo(semverA);
        }

        private bool DirectoryContainsMods(string directory)
        {
            if (Directory.Exists(directory))
            {
                foreach (string file in Directory.GetFileSystemEntries(Path.Combine(App.BeatSaberInstallDirectory, directory)))
                {
                    string fileExtension = Path.GetExtension(file);

                    if (File.Exists(file) && (fileExtension == ".dll" || fileExtension == ".manifest")) return true;
                }
            }
            return false;
        }

        private string CheckPreviousInstallDirs()
        {
            if (!Directory.Exists(App.BeatSaberInstallDirectory)) return null;

            string[] directories = Directory.GetDirectories(App.BeatSaberInstallDirectory, "Old*Plugins");
            if (directories.Length == 0) return null;
            Array.Sort(directories, CompareInstallDirs);

            foreach(string directory in directories) if (DirectoryContainsMods(directory)) return directory;

            return null;
        }

        private void CheckInstallDir(string directory, bool setFailedMods = false)
        {
            if (!Directory.Exists(Path.Combine(App.BeatSaberInstallDirectory, directory)))
            {
                return;
            }

            foreach (string file in Directory.GetFileSystemEntries(Path.Combine(App.BeatSaberInstallDirectory, directory)))
            {
                string fileExtension = Path.GetExtension(file);

                if (File.Exists(file) && (fileExtension == ".dll" || fileExtension == ".exe" || fileExtension == ".manifest"))
                {
                    Mod mod = GetModFromHash(Utils.CalculateMD5(file));
                    if (mod != null)
                    {
                        if (fileExtension == ".manifest")
                        {
                            ManifestsToMatch.Add(mod);
                        }
                        else
                        {
                            if (directory.Contains("Libs"))
                            {
                                if (!ManifestsToMatch.Contains(mod))
                                {
                                    continue;
                                }

                                ManifestsToMatch.Remove(mod);
                            }

                            AddDetectedMod(mod);
                        }
                    }
                    else if (setFailedMods)
                    {
                        string fileName = Path.GetFileNameWithoutExtension(file);
                        if (!MissingOldMods.Contains(fileName)) MissingOldMods.Add(fileName);
                        // maybe hook into the manifest to get the actual name. too lazy for now.
                    }
                }
            }
        }

        public void GetBSIPAVersion()
        {
            string InjectorPath = Path.Combine(App.BeatSaberInstallDirectory, "Beat Saber_Data", "Managed", "IPA.Injector.dll");
            if (!File.Exists(InjectorPath)) return;

            string InjectorHash = Utils.CalculateMD5(InjectorPath);
            foreach (Mod mod in AllModsList)
            {
                if (mod.name.ToLowerInvariant() == "bsipa")
                {
                    foreach (Mod.DownloadLink download in mod.downloads)
                    {
                        foreach (Mod.FileHashes fileHash in download.hashMd5)
                        {
                            if (fileHash.hash == InjectorHash)
                            {
                                AddDetectedMod(mod);
                            }
                        }
                    }
                }
            }
        }

        private void AddDetectedMod(Mod mod)
        {
            if (!InstalledMods.Contains(mod))
            {
                InstalledMods.Add(mod);
                if (!DefaultMods.Contains(mod.name))
                {
                    DefaultMods.Add(mod.name);
                }
            }
        }

        private Mod GetModFromHash(string hash)
        {
            foreach (Mod mod in AllModsList)
            {
                if (mod.name.ToLowerInvariant() != "bsipa" && mod.status != "declined")
                {
                    foreach (Mod.DownloadLink download in mod.downloads)
                    {
                        foreach (Mod.FileHashes fileHash in download.hashMd5)
                        {
                            if (fileHash.hash == hash)
                                return mod;
                        }
                    }
                }
            }

            return null;
        }

        public async Task PopulateModsList()
        {
            try
            {
                var resp = await HttpClient.GetAsync(Utils.Constants.BeatModsAPIUrl + Utils.Constants.BeatModsModsOptions + "&gameVersion=" + MainWindow.GameVersion);
                var body = await resp.Content.ReadAsStringAsync();
                ModsList = JsonSerializer.Deserialize<Mod[]>(body);
            }
            catch (Exception e)
            {
                System.Windows.MessageBox.Show($"{FindResource("Mods:LoadFailed")}.\n\n" + e);
                return;
            }

            foreach (Mod mod in ModsList)
            {
                bool preSelected = mod.required;
                if (DefaultMods.Contains(mod.name) || (App.SaveModSelection && App.SavedMods.Contains(mod.name)))
                {
                    preSelected = true;
                    if (!App.SavedMods.Contains(mod.name))
                    {
                        App.SavedMods.Add(mod.name);
                    }
                }

                RegisterDependencies(mod);

                ModListItem ListItem = new ModListItem()
                {
                    IsSelected = preSelected,
                    IsEnabled = !mod.required,
                    ModName = mod.name,
                    ModVersion = mod.version,
                    ModDescription = mod.description.Replace("\r\n", " ").Replace("\n", " "),
                    ModRequired = mod.required,
                    ModInfo = mod,
                    Category = mod.category
                };

                foreach (Promotion promo in Promotions.List)
                {
                    if (promo.Active && mod.name == promo.ModName)
                    {
                        ListItem.PromotionTexts = new string[promo.Links.Count];
                        ListItem.PromotionLinks = new string[promo.Links.Count];
                        ListItem.PromotionTextAfterLinks = new string[promo.Links.Count];

                        for (int i = 0; i < promo.Links.Count; ++i)
                        {
                            PromotionLink link = promo.Links[i];
                            ListItem.PromotionTexts[i] = link.Text;
                            ListItem.PromotionLinks[i] = link.Link;
                            ListItem.PromotionTextAfterLinks[i] = link.TextAfterLink;
                        }
                    }
                }

                foreach (Mod installedMod in InstalledMods)
                {
                    if (mod.name == installedMod.name)
                    {
                        ListItem.InstalledModInfo = installedMod;
                        ListItem.IsInstalled = true;
                        ListItem.InstalledVersion = installedMod.version;
                        break;
                    }
                }

                mod.ListItem = ListItem;

                ModList.Add(ListItem);
            }

            foreach (Mod mod in ModsList)
            {
                ResolveDependencies(mod);
            }
        }

        public async void InstallMods()
        {
            MainWindow.Instance.InstallButton.IsEnabled = false;
            string installDirectory = App.BeatSaberInstallDirectory;

            foreach (Mod mod in ModsList)
            {
                // Ignore mods that are newer than installed version
                if (mod.ListItem.GetVersionComparison > 0) continue;

                // Ignore mods that are on current version if we aren't reinstalling mods
                if (mod.ListItem.GetVersionComparison == 0 && !App.ReinstallInstalledMods) continue;

                if (mod.name.ToLowerInvariant() == "bsipa")
                {
                    MainWindow.Instance.MainText = $"{string.Format((string)FindResource("Mods:InstallingMod"), mod.name)}...";
                    await Task.Run(async () => await InstallMod(mod, installDirectory));
                    MainWindow.Instance.MainText = $"{string.Format((string)FindResource("Mods:InstalledMod"), mod.name)}.";
                    if (!File.Exists(Path.Combine(installDirectory, "winhttp.dll")))
                    {
                        await Task.Run(() =>
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = Path.Combine(installDirectory, "IPA.exe"),
                                WorkingDirectory = installDirectory,
                                Arguments = "-n"
                            }).WaitForExit()
                        );
                    }

                    Options.Instance.YeetBSIPA.IsEnabled = true;
                }
                else if (mod.ListItem.IsSelected)
                {
                    MainWindow.Instance.MainText = $"{string.Format((string)FindResource("Mods:InstallingMod"), mod.name)}...";
                    await Task.Run(async () => await InstallMod(mod, Path.Combine(installDirectory, @"IPA\Pending")));
                    MainWindow.Instance.MainText = $"{string.Format((string)FindResource("Mods:InstalledMod"), mod.name)}.";
                }
            }

            MainWindow.Instance.MainText = $"{FindResource("Mods:FinishedInstallingMods")}.";
            MainWindow.Instance.InstallButton.IsEnabled = true;
            RefreshModsList();
        }

        private async Task InstallMod(Mod mod, string directory)
        {
            int filesCount = 0;
            string downloadLink = null;

            foreach (Mod.DownloadLink link in mod.downloads)
            {
                filesCount = link.hashMd5.Length;

                if (link.type == "universal")
                {
                    downloadLink = link.url;
                    break;
                }
                else if (link.type.ToLowerInvariant() == App.BeatSaberInstallType.ToLowerInvariant())
                {
                    downloadLink = link.url;
                    break;
                }
            }

            if (string.IsNullOrEmpty(downloadLink))
            {
                System.Windows.MessageBox.Show(string.Format((string)FindResource("Mods:ModDownloadLinkMissing"), mod.name));
                return;
            }

            while (true)
            {
                List<ZipArchiveEntry> files = new List<ZipArchiveEntry>(filesCount);

                using (Stream stream = await DownloadMod(Utils.Constants.BeatModsURL + downloadLink))
                using (ZipArchive archive = new ZipArchive(stream))
                {
                    foreach (ZipArchiveEntry file in archive.Entries)
                    {
                        string fileDirectory = Path.GetDirectoryName(Path.Combine(directory, file.FullName));
                        if (!Directory.Exists(fileDirectory))
                        {
                            Directory.CreateDirectory(fileDirectory);
                        }

                        if (!string.IsNullOrEmpty(file.Name))
                        {
                            foreach (Mod.DownloadLink download in mod.downloads)
                            {
                                foreach (Mod.FileHashes fileHash in download.hashMd5)
                                {
                                    using (Stream fileStream = file.Open())
                                    {
                                        if (fileHash.hash == Utils.CalculateMD5FromStream(fileStream))
                                        {
                                            files.Add(file);
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                    }

                    if (files.Count == filesCount)
                    {
                        foreach (ZipArchiveEntry file in files)
                        {
                            await ExtractFile(file, Path.Combine(directory, file.FullName), 3.0, mod.name, 10);
                        }

                        break;
                    }
                }
            }

            if (App.CheckInstalledMods)
            {
                mod.ListItem.IsInstalled = true;
                mod.ListItem.InstalledVersion = mod.version;
                mod.ListItem.InstalledModInfo = mod;
            }
        }

        private async Task ExtractFile(ZipArchiveEntry file, string path, double seconds, string name, int maxTries, int tryNumber = 0)
        {
            if (tryNumber < maxTries)
            {
                try
                {
                    file.ExtractToFile(path, true);
                }
                catch
                {
                    MainWindow.Instance.MainText = $"{string.Format((string)FindResource("Mods:FailedExtract"), name, seconds, tryNumber + 1, maxTries)}";
                    await Task.Delay((int)(seconds * 1000));
                    await ExtractFile(file, path, seconds, name, maxTries, tryNumber + 1);
                }
            }
            else
            {
                System.Windows.MessageBox.Show($"{string.Format((string)FindResource("Mods:FailedExtractMaxReached"), name, maxTries)}.", "Failed to install " + name);
            }
        }

        private async Task<Stream> DownloadMod(string link)
        {
            var resp = await HttpClient.GetAsync(link);
            return await resp.Content.ReadAsStreamAsync();
        }

        private void RegisterDependencies(Mod dependent)
        {
            if (dependent.dependencies.Length == 0)
                return;

            foreach (Mod mod in ModsList)
            {
                foreach (Mod.Dependency dep in dependent.dependencies)
                {

                    if (dep.name == mod.name)
                    {
                        dep.Mod = mod;
                        mod.Dependents.Add(dependent);

                    }
                }
            }
        }

        private void ResolveDependencies(Mod dependent)
        {
            if (dependent.ListItem.IsSelected && dependent.dependencies.Length > 0)
            {
                foreach (Mod.Dependency dependency in dependent.dependencies)
                {
                    if (dependency.Mod.ListItem.IsEnabled)
                    {
                        dependency.Mod.ListItem.PreviousState = dependency.Mod.ListItem.IsSelected;
                        dependency.Mod.ListItem.IsSelected = true;
                        dependency.Mod.ListItem.IsEnabled = false;
                        ResolveDependencies(dependency.Mod);
                    }
                }
            }
        }

        private void UnresolveDependencies(Mod dependent)
        {
            if (!dependent.ListItem.IsSelected && dependent.dependencies.Length > 0)
            {
                foreach (Mod.Dependency dependency in dependent.dependencies)
                {
                    if (!dependency.Mod.ListItem.IsEnabled)
                    {
                        bool needed = false;
                        foreach (Mod dep in dependency.Mod.Dependents)
                        {
                            if (dep.ListItem.IsSelected)
                            {
                                needed = true;
                                break;
                            }
                        }
                        if (!needed && !dependency.Mod.required)
                        {
                            dependency.Mod.ListItem.IsSelected = dependency.Mod.ListItem.PreviousState;
                            dependency.Mod.ListItem.IsEnabled = true;
                            UnresolveDependencies(dependency.Mod);
                        }
                    }
                }
            }
        }

        private void ModCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            Mod mod = (sender as System.Windows.Controls.CheckBox).Tag as Mod;
            mod.ListItem.IsSelected = true;
            ResolveDependencies(mod);
            App.SavedMods.Add(mod.name);
            Properties.Settings.Default.SavedMods = string.Join(",", App.SavedMods.ToArray());
            Properties.Settings.Default.Save();

            RefreshModsList();
        }

        private void ModCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            Mod mod = (sender as System.Windows.Controls.CheckBox).Tag as Mod;
            mod.ListItem.IsSelected = false;
            UnresolveDependencies(mod);
            App.SavedMods.Remove(mod.name);
            Properties.Settings.Default.SavedMods = string.Join(",", App.SavedMods.ToArray());
            Properties.Settings.Default.Save();

            RefreshModsList();
        }

        public class Category
        {
            public string CategoryName { get; set; }
            public List<ModListItem> Mods = new List<ModListItem>();
        }

        public class ModListItem
        {
            public string ModName { get; set; }
            public string ModVersion { get; set; }
            public string ModDescription { get; set; }
            public bool ModRequired { get; set; }
            public bool PreviousState { get; set; }

            public bool IsEnabled { get; set; }
            public bool IsSelected { get; set; }
            public Mod ModInfo { get; set; }
            public string Category { get; set; }

            public Mod InstalledModInfo { get; set; }
            public bool IsInstalled { get; set; }
            private SemVersion _installedVersion { get; set; }
            public string InstalledVersion
            {
                get
                {
                    if (!IsInstalled || _installedVersion == null) return "-";
                    return _installedVersion.ToString();
                }
                set
                {
                    if (SemVersion.TryParse(value, out SemVersion tempInstalledVersion))
                    {
                        _installedVersion = tempInstalledVersion;
                    }
                    else
                    {
                        _installedVersion = null;
                    }
                }
            }

            public string GetVersionColor
            {
                get
                {
                    if (!IsInstalled) return "Black";
                    return _installedVersion >= ModVersion ? "Green" : "Red";
                }
            }

            public string GetVersionDecoration
            {
                get
                {
                    if (!IsInstalled) return "None";
                    return _installedVersion >= ModVersion ? "None" : "Strikethrough";
                }
            }

            public int GetVersionComparison
            {
                get
                {
                    if (!IsInstalled || _installedVersion < ModVersion) return -1;
                    if (_installedVersion > ModVersion) return 1;
                    return 0;
                }
            }

            public bool CanDelete
            {
                get
                {
                    return (!ModInfo.required && IsInstalled);
                }
            }

            public string CanSeeDelete
            {
                get
                {
                    if (!ModInfo.required && IsInstalled)
                        return "Visible";
                    else
                        return "Hidden";
                }
            }

            public string[] PromotionTexts { get; set; }
            public string[] PromotionLinks { get; set; }
            public string[] PromotionTextAfterLinks { get; set; }
            public string PromotionMargin
            {
                get
                {
                    if (PromotionTexts == null || string.IsNullOrEmpty(PromotionTexts[0])) return "-15,0,0,0";
                    return "0,0,5,0";
                }
            }
        }

        private void ModsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if ((Mods.ModListItem)Instance.ModsListView.SelectedItem == null)
            {
                MainWindow.Instance.InfoButton.IsEnabled = false;
            }
            else
            {
                MainWindow.Instance.InfoButton.IsEnabled = true;
            }
        }

        public void UninstallBSIPA(Mod.DownloadLink links)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(App.BeatSaberInstallDirectory, "IPA.exe"),
                WorkingDirectory = App.BeatSaberInstallDirectory,
                Arguments = "--revert -n"
            }).WaitForExit();

            foreach (Mod.FileHashes files in links.hashMd5)
            {
                string file = files.file.Replace("IPA/", "").Replace("Data", "Beat Saber_Data");
                if (File.Exists(Path.Combine(App.BeatSaberInstallDirectory, file)))
                    File.Delete(Path.Combine(App.BeatSaberInstallDirectory, file));
            }
            Options.Instance.YeetBSIPA.IsEnabled = false;
        }

        private void Uninstall_Click(object sender, RoutedEventArgs e)
        {
            Mod mod = ((sender as System.Windows.Controls.Button).Tag as Mod);

            string title = string.Format((string)FindResource("Mods:UninstallBox:Title"), mod.name);
            string body1 = string.Format((string)FindResource("Mods:UninstallBox:Body1"), mod.name);
            string body2 = string.Format((string)FindResource("Mods:UninstallBox:Body2"), mod.name);
            var result = System.Windows.Forms.MessageBox.Show($"{body1}\n{body2}", title, MessageBoxButtons.YesNo);

            if (result == DialogResult.Yes)
            {
                UninstallModFromList(mod);
            }
        }

        private void UninstallModFromList(Mod mod)
        {
            UninstallMod(mod.ListItem.InstalledModInfo);
            mod.ListItem.IsInstalled = false;
            mod.ListItem.InstalledVersion = null;
            if (App.SelectInstalledMods)
            {
                mod.ListItem.IsSelected = false;
                UnresolveDependencies(mod);
                App.SavedMods.Remove(mod.name);
                Properties.Settings.Default.SavedMods = string.Join(",", App.SavedMods.ToArray());
                Properties.Settings.Default.Save();
                RefreshModsList();
            }
            view.Refresh();
        }

        public void UninstallMod(Mod mod)
        {
            Mod.DownloadLink links = null;
            foreach (Mod.DownloadLink link in mod.downloads)
            {
                if (link.type.ToLowerInvariant() == "universal" || link.type.ToLowerInvariant() == App.BeatSaberInstallType.ToLowerInvariant())
                {
                    links = link;
                    break;
                }
            }
            if (mod.name.ToLowerInvariant() == "bsipa")
            {
                var hasIPAExe = File.Exists(Path.Combine(App.BeatSaberInstallDirectory, "IPA.exe"));
                var hasIPADir = Directory.Exists(Path.Combine(App.BeatSaberInstallDirectory, "IPA"));

                if (hasIPADir && hasIPAExe)
                {
                    UninstallBSIPA(links);
                }
                else
                {
                    var title = (string)FindResource("Mods:UninstallBSIPANotFound:Title");
                    var body = (string)FindResource("Mods:UninstallBSIPANotFound:Body");

                    System.Windows.Forms.MessageBox.Show(body, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            foreach (Mod.FileHashes files in links.hashMd5)
            {
                if (File.Exists(Path.Combine(App.BeatSaberInstallDirectory, files.file)))
                    File.Delete(Path.Combine(App.BeatSaberInstallDirectory, files.file));
                if (File.Exists(Path.Combine(App.BeatSaberInstallDirectory, "IPA", "Pending", files.file)))
                    File.Delete(Path.Combine(App.BeatSaberInstallDirectory, "IPA", "Pending", files.file));
            }
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri));
            e.Handled = true;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshColumns();
        }

        private void CopyText(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!(sender is TextBlock textBlock)) return;
            var text = textBlock.Text;

            // Ensure there's text to be copied
            if (string.IsNullOrWhiteSpace(text)) return;

            Utils.SetClipboard(text);
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            if (SearchBar.Height == 0)
            {
                SearchBar.Focus();
                Animate(SearchBar, 0, 16, new TimeSpan(0, 0, 0, 0, 300));
                Animate(SearchText, 0, 16, new TimeSpan(0, 0, 0, 0, 300));
                ModsListView.Items.Filter = new Predicate<object>(SearchFilter);
            }
            else
            {
                Animate(SearchBar, 16, 0, new TimeSpan(0, 0, 0, 0, 300));
                Animate(SearchText, 16, 0, new TimeSpan(0, 0, 0, 0, 300));
                ModsListView.Items.Filter = null;
            }
        }

        private void SearchBar_TextChanged(object sender, TextChangedEventArgs e)
        {
            ModsListView.Items.Filter = new Predicate<object>(SearchFilter);
            if (SearchBar.Text.Length > 0)
            {
                SearchText.Text = null;
            }
            else
            {
                SearchText.Text = (string)FindResource("Mods:SearchLabel");
            }
        }

        private bool SearchFilter(object mod)
        {
            ModListItem item = mod as ModListItem;
            if (item.ModName.ToLowerInvariant().Contains(SearchBar.Text.ToLowerInvariant())) return true;
            if (item.ModDescription.ToLowerInvariant().Contains(SearchBar.Text.ToLowerInvariant())) return true;
            if (item.ModName.ToLowerInvariant().Replace(" ", string.Empty).Contains(SearchBar.Text.ToLowerInvariant().Replace(" ", string.Empty))) return true;
            if (item.ModDescription.ToLowerInvariant().Replace(" ", string.Empty).Contains(SearchBar.Text.ToLowerInvariant().Replace(" ", string.Empty))) return true;
            return false;
        }

        private void Animate(TextBlock target, double oldHeight, double newHeight, TimeSpan duration)
        {
            target.Height = oldHeight;
            DoubleAnimation animation = new DoubleAnimation(newHeight, duration);
            target.BeginAnimation(HeightProperty, animation);
        }

        private void Animate(TextBox target, double oldHeight, double newHeight, TimeSpan duration)
        {
            target.Height = oldHeight;
            DoubleAnimation animation = new DoubleAnimation(newHeight, duration);
            target.BeginAnimation(HeightProperty, animation);
        }
    }
}
