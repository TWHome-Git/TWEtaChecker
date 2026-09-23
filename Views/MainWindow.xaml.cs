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
