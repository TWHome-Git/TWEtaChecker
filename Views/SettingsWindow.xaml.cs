using System;
using System.Windows;
using TWEtaChecker.Services;

namespace TWEtaChecker.Views
{
    /// <summary>글자 크기, 팝업 위치, 테일즈위버 폴더, 에타 랭킹 새로 고침. 바꾸는 즉시 저장된다.</summary>
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
            Closed += (_, _) =>
            {
                // 위치 조정 중에 닫으면 그 자리로 저장하고 미리보기를 닫는다
                if (App.Toasts.IsPreviewVisible)
                    App.Toasts.ClosePositionPreview();
            };
            Refresh();
        }

        private void Refresh()
        {
            FontSizeText.Text = App.Settings.FontSize.ToString("0");
            PositionButton.Content = App.Toasts.IsPreviewVisible ? "위치 저장" : "위치 조정";

            string? folder = App.Settings.TalesWeaverFolder;
            GameFolderText.Text = string.IsNullOrWhiteSpace(folder) ? "지정 안 됨" : folder;
            GameFolderText.ToolTip = GameFolderText.Text;
            string? msgerLog = MessengerLogWatcherService.ResolveMessengerLogDirectory();
            MessengerLogText.Text = msgerLog == null ? "폴더를 지정해야 1:1 대화를 감시합니다." : $"메신저 로그: {msgerLog}";

            RankingText.Text = MainWindow.DescribeRanking();
            VersionText.Text = $"현재 {UpdateService.CurrentVersionText}";
        }

        private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            CheckUpdateButton.IsEnabled = false;
            UpdateStatusText.Text = "확인하는 중…";
            ReleaseInfo? newer = await App.Updates.CheckAsync();
            UpdateStatusText.Text = newer != null
                ? $"새 버전 v{newer.Version.ToString(3)}이 있습니다. 상태 창의 [업데이트]를 누르세요."
                : App.Updates.LastCheckSucceeded ? "최신 버전입니다." : "확인하지 못했습니다. 인터넷 연결을 확인하세요.";
            CheckUpdateButton.IsEnabled = true;
        }

        private void FontSmaller_Click(object sender, RoutedEventArgs e) { App.ChangeFontSize(-2); Refresh(); }
        private void FontLarger_Click(object sender, RoutedEventArgs e) { App.ChangeFontSize(+2); Refresh(); }

        private void Position_Click(object sender, RoutedEventArgs e)
        {
            // 미리보기 표시·닫기는 디스패처에 걸려 있으므로 끝난 뒤에 버튼 글자를 바꾼다
            App.TogglePositionPreview();
            Dispatcher.BeginInvoke(Refresh, System.Windows.Threading.DispatcherPriority.Background);
        }

        private void Test_Click(object sender, RoutedEventArgs e) => App.ShowDemoToast();

        private void ChangeFolder_Click(object sender, RoutedEventArgs e)
        {
            App.ChooseGameFolder(this);
            Refresh();
        }

        private async void RefreshRanking_Click(object sender, RoutedEventArgs e)
        {
            RefreshRankingButton.IsEnabled = false;
            RankingText.Text = "받는 중…";
            bool ok;
            try { ok = await App.Ranking.ForceRefreshAsync(); }
            catch (Exception ex) { AppLogger.Warn("Ranking refresh failed.", ex); ok = false; }
            RefreshRankingButton.IsEnabled = true;
            Refresh();
            if (!ok)
                RankingText.Text = "받지 못했습니다. 잠시 뒤 다시 시도하세요.";
        }

        private void OpenConfig_Click(object sender, RoutedEventArgs e) => App.OpenConfigFolder();

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
