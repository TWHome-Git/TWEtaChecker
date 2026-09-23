using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using TWEtaChecker.Services;

namespace TWEtaChecker.Views
{
    /// <summary>
    /// 상태(테일즈위버 폴더 / 감시 상태 / 에타 랭킹)를 보여 주는 작은 창.
    /// 최소화하면 숨기고 트레이로 들어간다. 닫기(X)는 프로그램 종료.
    /// </summary>
    public partial class MainWindow : Window
    {
        private static readonly Brush OkBrush = new SolidColorBrush(Color.FromRgb(0x0C, 0xD2, 0x9D));
        private static readonly Brush WaitBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xD8, 0x4A));
        private static readonly Brush OffBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));

        private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        private SettingsWindow? _settingsWindow;

        public MainWindow()
        {
            InitializeComponent();
            SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
            StateChanged += OnStateChanged;
            Closed += (_, _) => Application.Current.Shutdown();

            VersionText.Text = UpdateService.CurrentVersionText;
            App.Updates.AvailableChanged += () => Dispatcher.BeginInvoke(RefreshUpdateBar);
            RefreshUpdateBar();

            _refreshTimer.Tick += (_, _) => RefreshStatus();
            _refreshTimer.Start();
            RefreshStatus();
        }

        /// <summary>트레이에서 다시 불러낼 때.</summary>
        public void ShowFromTray()
        {
            Show();
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();
            RefreshStatus();
        }

        private void OnStateChanged(object? sender, EventArgs e)
        {
            if (WindowState != WindowState.Minimized)
                return;
            Hide();
            _settingsWindow?.Hide();
        }

        public void RefreshStatus()
        {
            if (!IsVisible)
                return;

            GameFolderText.Text = string.IsNullOrWhiteSpace(App.Settings.TalesWeaverFolder) ? "지정 안 됨" : App.Settings.TalesWeaverFolder;
            GameFolderText.ToolTip = GameFolderText.Text;

            (string text, WatchState state) = App.Watcher.GetStatus();
            WatchStatusText.Text = text;
            WatchDot.Fill = state switch
            {
                WatchState.Watching => OkBrush,
                WatchState.WaitingForFolder => WaitBrush,
                _ => OffBrush,
            };

            RankingStatusText.Text = DescribeRanking();
        }

        public static string DescribeRanking()
        {
            int count = App.Ranking.GetRankings().Count;
            if (count == 0)
                return "받는 중…";
            DateTime? date = App.Ranking.GetLastPayloadDate();
            return date.HasValue ? $"{date:yyyy-MM-dd} 기준 · {count:N0}명" : $"{count:N0}명";
        }

        private bool _updating;

        private void RefreshUpdateBar()
        {
            ReleaseInfo? release = App.Updates.Available;
            if (release == null || _updating)
            {
                UpdateBar.Visibility = release == null ? Visibility.Collapsed : UpdateBar.Visibility;
                return;
            }
            UpdateText.Text = $"새 버전 v{release.Version.ToString(3)}이 나왔습니다.";
            UpdateButton.Content = UpdateService.CanSelfUpdate ? "업데이트" : "받으러 가기";
            UpdateButton.IsEnabled = true;
            UpdateBar.Visibility = Visibility.Visible;
        }

        private async void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            ReleaseInfo? release = App.Updates.Available;
            if (release == null)
                return;

            _updating = true;
            UpdateButton.IsEnabled = false;
            UpdateText.Text = "새 버전을 받는 중…";
            var progress = new Progress<double>(p => UpdateText.Text = $"새 버전을 받는 중… {p:P0}");
            bool ok = await App.InstallUpdateAsync(release, progress);
            _updating = false;
            if (ok)
            {
                RefreshUpdateBar(); // 릴리즈 페이지를 연 경우. 설치했으면 곧 프로그램이 다시 시작된다
                return;
            }
            UpdateText.Text = "업데이트하지 못했습니다. 잠시 뒤 다시 시도하세요.";
            UpdateButton.IsEnabled = true;
        }

        private void TestButton_Click(object sender, RoutedEventArgs e) => App.ShowDemoToast();

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_settingsWindow == null || !_settingsWindow.IsLoaded)
            {
                _settingsWindow = new SettingsWindow { Owner = this };
                _settingsWindow.Closed += (_, _) => { _settingsWindow = null; RefreshStatus(); };
            }
            _settingsWindow.Show();
            _settingsWindow.Activate();
        }
    }
}
