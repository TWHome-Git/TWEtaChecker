using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using TWEtaChecker.Models;
using TWEtaChecker.Services;
using TWEtaChecker.Views;

namespace TWEtaChecker
{
    /// <summary>
    /// TW 에타 알림 — 테일즈위버 1:1 대화(메신저) 로그를 지켜보다가 상대의 에타 레벨을 작은 팝업으로 알려 주는 프로그램.
    /// 상태 창(MainWindow)이 기본으로 보이고, 최소화하면 트레이로 들어간다. 게임 메모리는 읽지 않고 게임이 남기는 MsgerLog HTML 파일만 읽는다.
    /// </summary>
    public partial class App : Application
    {
        private const string Caption = "TW 에타 알림";

        private static Mutex? _singleInstance;
        private TrayIconService? _tray;
        private MainWindow? _mainWindow;

        public static AppSettings Settings { get; private set; } = new();
        public static EtaRankingService Ranking { get; } = new();
        public static EtaToastService Toasts { get; } = new();
        public static MessengerLogWatcherService Watcher { get; } = new();
        public static UpdateService Updates { get; } = new();

        private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(6);
        private System.Windows.Threading.DispatcherTimer? _updateTimer;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 업데이트로 다시 시작한 경우: 이전 프로세스가 끝나 단일 실행 잠금을 놓을 때까지 기다린다
            UpdateService.FinishPendingUpdate(e.Args);

            _singleInstance = new Mutex(true, "TWEtaChecker.SingleInstance", out bool createdNew);
            if (!createdNew)
            {
                MessageBox.Show("TW 에타 알림이 이미 실행 중입니다. 트레이 아이콘을 두 번 눌러 창을 여세요.", Caption, MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            DispatcherUnhandledException += (_, args) =>
            {
                AppLogger.Error("Unhandled UI exception.", args.Exception);
                args.Handled = true;
            };

            Settings = SettingsStore.Load();
            Ranking.InitializeAsync();

            // 처음 실행: 테일즈위버 폴더부터 정한다. 취소하면 감시 없이 창만 뜨고 다음 실행 때 다시 묻는다.
            if (string.IsNullOrWhiteSpace(Settings.TalesWeaverFolder) && !AskGameFolder(owner: null, firstRun: true))
                MessageBox.Show(
                    "테일즈위버 폴더를 정하지 않아 1:1 대화를 감시하지 않습니다.\n[설정] → 테일즈위버 폴더 [변경…]에서 언제든 정할 수 있습니다.",
                    Caption, MessageBoxButton.OK, MessageBoxImage.Information);

            Watcher.Start();

            _tray = new TrayIconService("TW 에타 알림 — 1:1 대화 에타 확인", new List<TrayIconService.MenuItem>
            {
                new("종료", () => Shutdown()),
            });
            _tray.DoubleClick += () => _mainWindow?.ShowFromTray();

            _mainWindow = new MainWindow();
            _mainWindow.Show();

            // 새 버전 확인: 지금 한 번, 이후 6시간마다
            _ = Updates.CheckAsync();
            _updateTimer = new System.Windows.Threading.DispatcherTimer { Interval = UpdateCheckInterval };
            _updateTimer.Tick += (_, _) => _ = Updates.CheckAsync();
            _updateTimer.Start();

            // 명령줄 --demo: 실행하자마자 테스트 팝업을 띄운다 (모양 확인용)
            foreach (string arg in e.Args)
                if (string.Equals(arg, "--demo", StringComparison.OrdinalIgnoreCase))
                    ShowDemoToast();

            AppLogger.Info($"Started {UpdateService.CurrentVersionText}. MsgerLog={MessengerLogWatcherService.ResolveMessengerLogDirectory() ?? "(not set)"}");
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try { Watcher.Dispose(); } catch { }
            try { _tray?.Dispose(); } catch { }
            try { _singleInstance?.ReleaseMutex(); } catch { }
            base.OnExit(e);
        }

        // ===== 메인·설정 창에서 부르는 동작 =====

        public static void TogglePositionPreview()
        {
            if (Toasts.IsPreviewVisible)
                Toasts.ClosePositionPreview();
            else
                Toasts.ShowPositionPreview();
        }

        /// <summary>테스트 팝업. 실제 1:1 대화처럼 에타 랭킹에서 레벨·닮은 아이디를 찾아 보여 준다.</summary>
        public static void ShowDemoToast()
        {
            string[] demoIds = { "드드해", "드드헤", "뜨뜨해", "드드해1" };
            _ = Task.Run(async () =>
            {
                try
                {
                    await Ranking.EnsureLoadedAsync().ConfigureAwait(false);
                    Toasts.ShowForFile("__demo__", demoIds.Select(MessengerLogWatcherService.BuildEntry).ToList());
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("Demo toast failed.", ex);
                }
            });
        }

        public static void ChangeFontSize(double delta)
        {
            Settings.FontSize = Math.Max(12, Math.Min(40, Settings.FontSize + delta));
            SettingsStore.Save(Settings);
            Toasts.ApplyFontSize(Settings.FontSize);
        }

        /// <summary>설정 창의 테일즈위버 폴더 [변경…]. 바뀌면 감시를 새 폴더로 다시 시작한다.</summary>
        public static void ChooseGameFolder(Window owner)
        {
            if (AskGameFolder(owner, firstRun: false))
                Watcher.Restart();
        }

        /// <summary>
        /// 테일즈위버 설치 폴더를 고르게 하고 설정에 저장한다. 메신저 로그는 그 아래 MsgerLog로 자동 연결된다.
        /// 고른 폴더에 MsgerLog·ChatLog가 없으면 한 번 더 확인한다. 저장했으면 true, 취소했으면 false.
        /// </summary>
        private static bool AskGameFolder(Window? owner, bool firstRun)
        {
            if (firstRun)
                ShowMessage(owner,
                    "처음 실행입니다. 테일즈위버가 설치된 폴더를 선택해 주세요.\n(보통 C:\\Nexon\\TalesWeaver — 그 안의 MsgerLog 폴더에서 1:1 대화를 읽습니다.)",
                    MessageBoxButton.OK, MessageBoxImage.Information);

            string? current = Settings.TalesWeaverFolder;
            string initial = !string.IsNullOrWhiteSpace(current) && Directory.Exists(current) ? current
                : Directory.Exists(MessengerLogWatcherService.DefaultGameFolder) ? MessengerLogWatcherService.DefaultGameFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.MyComputer);

            while (true)
            {
                var dialog = new OpenFolderDialog
                {
                    Title = "테일즈위버 설치 폴더를 선택하세요 (예: C:\\Nexon\\TalesWeaver)",
                    InitialDirectory = initial,
                };
                bool? picked = owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
                if (picked != true)
                    return false;

                string gameFolder = MessengerLogWatcherService.NormalizeGameFolder(dialog.FolderName);
                if (!MessengerLogWatcherService.LooksLikeGameFolder(gameFolder))
                {
                    MessageBoxResult answer = ShowMessage(owner,
                        $"{gameFolder}\n\n이 폴더에 MsgerLog나 ChatLog 폴더가 없습니다. 테일즈위버 설치 폴더가 맞나요?\n\n" +
                        "예: 이 폴더로 정합니다 (1:1 대화를 처음 하면 게임이 MsgerLog를 만듭니다)\n아니요: 다시 고릅니다",
                        MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                    if (answer == MessageBoxResult.Cancel)
                        return false;
                    if (answer == MessageBoxResult.No)
                    {
                        initial = gameFolder;
                        continue;
                    }
                }

                Settings.TalesWeaverFolder = gameFolder;
                SettingsStore.Save(Settings);
                AppLogger.Info($"TalesWeaver folder set: {gameFolder}");
                return true;
            }
        }

        private static MessageBoxResult ShowMessage(Window? owner, string text, MessageBoxButton buttons, MessageBoxImage image) =>
            owner == null
                ? MessageBox.Show(text, Caption, buttons, image)
                : MessageBox.Show(owner, text, Caption, buttons, image);

        /// <summary>
        /// 새 버전을 받아 설치하고 다시 시작한다. 개발 빌드처럼 제자리 교체가 안 되면 릴리즈 페이지를 연다.
        /// 실패하면 false (프로그램은 그대로 계속 돈다).
        /// </summary>
        public static async Task<bool> InstallUpdateAsync(ReleaseInfo release, IProgress<double>? progress)
        {
            if (!UpdateService.CanSelfUpdate)
            {
                UpdateService.OpenReleasesPage(release.PageUrl);
                return true;
            }
            if (!await Updates.DownloadAndInstallAsync(release, progress))
                return false;
            Current.Shutdown();
            return true;
        }

        public static void OpenConfigFolder()
        {
            try
            {
                Directory.CreateDirectory(SettingsStore.ConfigDirectory);
                Process.Start(new ProcessStartInfo(SettingsStore.ConfigDirectory) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to open config folder.", ex);
            }
        }
    }
}
