using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ModAssistant.Pages;
using static ModAssistant.Http;

namespace ModAssistant
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public static MainWindow Instance;
        public static bool ModsOpened = false;
        public static bool ModsLoading = false;
        public static string GameVersion;
        public static string GameVersionDetected;  // the real game version detected from the game
        public static string GameVersionOverride;
        public TaskCompletionSource<bool> VersionLoadStatus = new TaskCompletionSource<bool>();

        public string MainText
        {
            get
            {
                return MainTextBlock.Text;
            }
            set
            {
                Dispatcher.Invoke(new Action(() => { Instance.MainTextBlock.Text = value; }));
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            Instance = this;

            const int ContentWidth = 1280;
            const int ContentHeight = 720;

            double ChromeWidth = SystemParameters.WindowNonClientFrameThickness.Left + SystemParameters.WindowNonClientFrameThickness.Right;
            double ChromeHeight = SystemParameters.WindowNonClientFrameThickness.Top + SystemParameters.WindowNonClientFrameThickness.Bottom;
            double ResizeBorder = SystemParameters.ResizeFrameVerticalBorderWidth;

            Width = ChromeWidth + ContentWidth + 2 * ResizeBorder;
            Height = ChromeHeight + ContentHeight + 2 * ResizeBorder;

            VersionText.Text = App.Version;

            if (Utils.IsVoid())
            {
                Main.Content = Invalid.Instance;
                Instance.ModsButton.IsEnabled = false;
                Instance.OptionsButton.IsEnabled = false;
                Instance.IntroButton.IsEnabled = false;
                Instance.AboutButton.IsEnabled = false;
                Instance.GameVersionsBox.IsEnabled = false;
                return;
            }

            Themes.LoadThemes();
            Themes.FirstLoad(Properties.Settings.Default.SelectedTheme);

            Task.Run(() => LoadVersionsAsync());

            if (!Properties.Settings.Default.Agreed || string.IsNullOrEmpty(Properties.Settings.Default.LastTab))
            {
                Main.Content = Intro.Instance;
            }
            else
            {
                switch (Properties.Settings.Default.LastTab)
                {
                    case "Intro":
                        Main.Content = Intro.Instance;
                        break;
                    case "Mods":
                        _ = ShowModsPage();
                        break;
                    case "About":
                        Main.Content = About.Instance;
                        break;
                    case "Options":
                        Main.Content = Options.Instance;
                        Themes.LoadThemes();
                        break;
                    default:
                        Main.Content = Intro.Instance;
                        break;
                }
            }
        }

        /* Force the app to shutdown when The main window is closed.
         *
         * Explaination:
         * OneClickStatus is initialized as a static object,
         * so the window will exist, even if it is unused.
         * This would cause Mod Assistant to not shutdown,
         * because technically a window was still open.
         */
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            Application.Current.Shutdown();
        }

        private async void LoadVersionsAsync()
        {
            try
            {
                var resp = await HttpClient.GetAsync(Utils.Constants.BeatModsVersions);
                var body = await resp.Content.ReadAsStringAsync();
                List<string> versions = JsonSerializer.Deserialize<string[]>(body).ToList();

                resp = await HttpClient.GetAsync(Utils.Constants.BeatModsAlias);
                body = await resp.Content.ReadAsStringAsync();
                Dictionary<string, string[]> aliases = JsonSerializer.Deserialize<Dictionary<string, string[]>>(body);

                string version = Utils.GetVersion();
                GameVersionDetected = version;
                if (!versions.Contains(version) && CheckAliases(versions, aliases, version) == string.Empty)
                {
                    versions.Insert(0, version);
                }


                Dispatcher.Invoke(() =>
                {
                    GameVersion = GetGameVersion(version, versions, aliases);

                    GameVersionsBox.ItemsSource = versions;
                    GameVersionsBox.SelectedValue = GameVersion;

                    if (!string.IsNullOrEmpty(GameVersionOverride))
                    {
                        GameVersionsBox.Visibility = Visibility.Collapsed;
                        GameVersionsBoxOverride.Visibility = Visibility.Visible;
                        GameVersionsBoxOverride.Text = GameVersionOverride;
                        GameVersionsBoxOverride.IsEnabled = false;
                    }

                    if (!string.IsNullOrEmpty(GameVersion) && Properties.Settings.Default.Agreed)
                    {
                        Instance.ModsButton.IsEnabled = true;
                    }
                });

                VersionLoadStatus.SetResult(true);
            }
            catch (Exception e)
            {
                Dispatcher.Invoke(() =>
                {
                    GameVersionsBox.IsEnabled = false;
                    MessageBox.Show($"{Application.Current.FindResource("MainWindow:GameVersionLoadFailed")}\n{e}");
                });

                VersionLoadStatus.SetResult(false);
            }
        }

        private string GetGameVersion(string version, List<string> versions, Dictionary<string, string[]> aliases)
        {
            if (!string.IsNullOrEmpty(version) && versions.Contains(version))
            {
                return version;
            }

            string aliasOf = CheckAliases(versions, aliases, version);
            if (!string.IsNullOrEmpty(aliasOf))
            {
                return aliasOf;
            }

            string versionsString = string.Join(",", versions.ToArray());
            if (Properties.Settings.Default.AllGameVersions != versionsString)
            {
                Properties.Settings.Default.AllGameVersions = versionsString;
                Properties.Settings.Default.Save();

                string title = (string)Application.Current.FindResource("MainWindow:GameUpdateDialog:Title");
                string line1 = (string)Application.Current.FindResource("MainWindow:GameUpdateDialog:Line1");
                string line2 = (string)Application.Current.FindResource("MainWindow:GameUpdateDialog:Line2");

                Utils.ShowMessageBoxAsync($"{line1}\n\n{line2}", title);
                return versions[0];
            }

            if (!string.IsNullOrEmpty(Properties.Settings.Default.GameVersion) && versions.Contains(Properties.Settings.Default.GameVersion))
                return Properties.Settings.Default.GameVersion;
            return versions[0];
        }

        private string CheckAliases(List<string> versions, Dictionary<string, string[]> aliasesDict, string detectedVersion)
        {
            Dictionary<string, List<string>> aliases = aliasesDict.ToDictionary(x => x.Key, x => x.Value.ToList());
            foreach (string version in versions)
            {
                if (aliases.TryGetValue(version, out var x))
                {
                    if (x.Contains(detectedVersion))
                    {
                        GameVersionOverride = detectedVersion;
                        return version;
                    }
                }
            }

            return string.Empty;
        }

        private async Task ShowModsPage()
        {
            void OpenModsPage()
            {
                Main.Content = Mods.Instance;
                Properties.Settings.Default.LastTab = "Mods";
                Properties.Settings.Default.Save();
                Mods.Instance.RefreshColumns();
            }

            if (ModsOpened == true && Mods.Instance.PendingChanges == false)
            {
                OpenModsPage();
                return;
            }

            Main.Content = Loading.Instance;

            if (ModsLoading) return;
            ModsLoading = true;
            await Mods.Instance.LoadMods();
            ModsLoading = false;

            if (ModsOpened == false) ModsOpened = true;
            if (Mods.Instance.PendingChanges == true) Mods.Instance.PendingChanges = false;

            if (Main.Content == Loading.Instance)
            {
                OpenModsPage();
            }
        }

        private void ModsButton_Click(object sender, RoutedEventArgs e)
        {
            _ = ShowModsPage();
        }

        private void IntroButton_Click(object sender, RoutedEventArgs e)
        {
            Main.Content = Intro.Instance;
            Properties.Settings.Default.LastTab = "Intro";
            Properties.Settings.Default.Save();
        }

        private void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            Main.Content = About.Instance;
            Properties.Settings.Default.LastTab = "About";
            Properties.Settings.Default.Save();
        }

        private void OptionsButton_Click(object sender, RoutedEventArgs e)
        {
            Main.Content = Options.Instance;
            Themes.LoadThemes();
            Properties.Settings.Default.LastTab = "Options";
            Properties.Settings.Default.Save();
        }

        private void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(GameVersionOverride) // game version not listed in aliases at all
                && GameVersion != GameVersionDetected) // and the user manually selected a version
            {
                // show a waring about the version mismatch
                var result = MessageBox.Show(String.Format((string) Application.Current.FindResource("MainWindow:GameVersionMismatch"), GameVersion, GameVersionDetected),
                    (string)Application.Current.FindResource("MainWindow:GameVersionMismatchTitle"), MessageBoxButton.OKCancel);

                if (result == MessageBoxResult.OK)
                {
                    Mods.Instance.InstallMods();
                }
            }
            else
            {
                Mods.Instance.InstallMods();
            }
        }

        private void InfoButton_Click(object sender, RoutedEventArgs e)
        {
            if ((Mods.ModListItem)Mods.Instance.ModsListView.SelectedItem == null)
            {
                MessageBox.Show((string)Application.Current.FindResource("MainWindow:NoModSelected"));
                return;
            }
            Mods.ModListItem mod = ((Mods.ModListItem)Mods.Instance.ModsListView.SelectedItem);
            string infoUrl = mod.ModInfo.link;
            if (string.IsNullOrEmpty(infoUrl))
            {
                MessageBox.Show(string.Format((string)Application.Current.FindResource("MainWindow:NoModInfoPage"), mod.ModName));
            }
            else
            {
                System.Diagnostics.Process.Start(infoUrl);
            }
        }

        private async void GameVersionsBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string oldGameVersion = GameVersion;

            GameVersion = (sender as ComboBox).SelectedItem.ToString();

            if (string.IsNullOrEmpty(oldGameVersion)) return;

            Properties.Settings.Default.GameVersion = GameVersion;
            Properties.Settings.Default.Save();

            if (ModsOpened)
            {
                var prevPage = Main.Content;

                Mods.Instance.PendingChanges = true;
                await ShowModsPage();

                Main.Content = prevPage;
            }
        }

        private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (About.Instance.PatUp.IsOpen)
            {
                About.Instance.PatUp.IsOpen = false;
                About.Instance.PatButton.IsEnabled = true;
            }

            if (About.Instance.HugUp.IsOpen)
            {
                About.Instance.HugUp.IsOpen = false;
                About.Instance.HugButton.IsEnabled = true;
            }
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (Main.Content == Mods.Instance)
            {
                Mods.Instance.RefreshColumns();
            }
        }

        private void BackgroundVideo_MediaEnded(object sender, RoutedEventArgs e)
        {
            BackgroundVideo.Position = TimeSpan.Zero;
            BackgroundVideo.Play();
        }
    }
}
