using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TWEtaChecker.Models;

namespace TWEtaChecker.Services
{
    /// <summary>1:1 대화 상대 하나. 에타 랭킹에 없으면 Level이 null. Lookalikes는 랭킹에 있는 닮은 아이디(없으면 빈 목록).</summary>
    public readonly record struct MessengerEtaEntry(string UserId, int? Level, IReadOnlyList<EtaLookalike> Lookalikes);

    public enum WatchState { Off, WaitingForFolder, Watching }

    /// <summary>
    /// 게임의 메신저 로그 폴더(MsgerLog)를 지켜보다가 1:1 대화 HTML이 생기거나 바뀌면 상대 아이디를 뽑아 팝업을 띄운다.
    /// 상대 줄은 흰색(#ffffff) 글자 색으로 찍히므로 그 줄의 "이름 :" 앞부분이 아이디다.
    /// </summary>
    public sealed class MessengerLogWatcherService : IDisposable
    {
        public const string DefaultGameFolder = @"C:\Nexon\TalesWeaver";
        private const string MessengerLogFolderName = "MsgerLog";
        private static readonly Encoding KoreanEncoding;
        private static readonly Regex TargetLineRegex = new(
            @"<font[^>]*color\s*=\s*[""']#ffffff[""'][^>]*>\s*(?<name>[^:<]+)\s*:",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static MessengerLogWatcherService()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); // CP949
            KoreanEncoding = Encoding.GetEncoding(949);
        }

        private FileSystemWatcher? _watcher;
        private FileSystemWatcher? _folderCreationWatcher;
        private bool _disposed;
        private readonly ConcurrentDictionary<string, byte> _processing = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>설정된 테일즈위버 폴더 아래 MsgerLog. 폴더가 아직 설정되지 않았으면 null.</summary>
        public static string? ResolveMessengerLogDirectory()
        {
            string? gameFolder = App.Settings.TalesWeaverFolder;
            return string.IsNullOrWhiteSpace(gameFolder) ? null : Path.Combine(gameFolder, MessengerLogFolderName);
        }

        /// <summary>
        /// 사용자가 고른 폴더를 테일즈위버 설치 폴더로 바꾼다 — MsgerLog나 ChatLog 폴더 자체를 골랐으면 그 부모.
        /// </summary>
        public static string NormalizeGameFolder(string selected)
        {
            string trimmed = selected.TrimEnd('\\', '/');
            string name = Path.GetFileName(trimmed);
            if (name.Equals(MessengerLogFolderName, StringComparison.OrdinalIgnoreCase) || name.Equals("ChatLog", StringComparison.OrdinalIgnoreCase))
                return Path.GetDirectoryName(trimmed) ?? trimmed;
            return trimmed;
        }

        /// <summary>메인 창에 보여 줄 감시 상태.</summary>
        public (string Text, WatchState State) GetStatus()
        {
            if (ResolveMessengerLogDirectory() == null)
                return ("테일즈위버 폴더를 지정하세요", WatchState.Off);
            if (_watcher != null)
                return ("감시 중", WatchState.Watching);
            if (_folderCreationWatcher != null)
                return ("MsgerLog 폴더 생성 대기 (1:1 대화를 하면 생깁니다)", WatchState.WaitingForFolder);
            return ("테일즈위버 폴더를 찾을 수 없습니다", WatchState.Off);
        }

        /// <summary>테일즈위버 설치 폴더처럼 보이는지 (MsgerLog나 ChatLog가 있는지).</summary>
        public static bool LooksLikeGameFolder(string gameFolder) =>
            Directory.Exists(Path.Combine(gameFolder, MessengerLogFolderName)) || Directory.Exists(Path.Combine(gameFolder, "ChatLog"));

        public void Start()
        {
            if (_watcher != null || _folderCreationWatcher != null)
                return;

            string? directory = ResolveMessengerLogDirectory();
            if (directory == null)
            {
                AppLogger.Warn("TalesWeaver folder is not set; messenger log watcher not started.");
                return;
            }
            if (!Directory.Exists(directory))
            {
                WaitForMessengerLogFolder(directory);
                return;
            }

            _watcher = new FileSystemWatcher(directory)
            {
                Filter = "*.*",
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite,
                EnableRaisingEvents = true,
            };
            _watcher.Created += OnFileEvent;
            _watcher.Changed += OnFileEvent;
            _watcher.Renamed += (_, e) => QueueProcess(e.FullPath);
            AppLogger.Info($"Messenger log watcher started. Path={directory}");
        }

        /// <summary>
        /// 1:1 대화를 한 번도 안 했으면 MsgerLog 폴더가 아직 없다. 게임 폴더를 지켜보다가 생기면 그때 감시를 시작한다.
        /// </summary>
        private void WaitForMessengerLogFolder(string directory)
        {
            string? gameFolder = Path.GetDirectoryName(directory);
            if (gameFolder == null || !Directory.Exists(gameFolder))
            {
                AppLogger.Warn($"TalesWeaver folder not found: {gameFolder}");
                return;
            }

            _folderCreationWatcher = new FileSystemWatcher(gameFolder)
            {
                Filter = MessengerLogFolderName,
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.DirectoryName,
                EnableRaisingEvents = true,
            };
            FileSystemEventHandler onCreated = (_, _) => App.Current.Dispatcher.BeginInvoke(Restart);
            _folderCreationWatcher.Created += onCreated;
            _folderCreationWatcher.Renamed += (_, e) => onCreated(null!, e);
            AppLogger.Info($"Messenger log directory not found yet; waiting for it. Path={directory}");
        }

        public void Stop()
        {
            if (_folderCreationWatcher != null)
            {
                _folderCreationWatcher.EnableRaisingEvents = false;
                _folderCreationWatcher.Dispose();
                _folderCreationWatcher = null;
            }
            if (_watcher == null)
                return;
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
            AppLogger.Info("Messenger log watcher stopped.");
        }

        /// <summary>폴더 설정이 바뀌었을 때.</summary>
        public void Restart()
        {
            Stop();
            Start();
        }

        private void OnFileEvent(object sender, FileSystemEventArgs e) => QueueProcess(e.FullPath);

        private void QueueProcess(string fullPath)
        {
            string ext = Path.GetExtension(fullPath);
            if (!ext.Equals(".html", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".htm", StringComparison.OrdinalIgnoreCase))
                return;
            if (!_processing.TryAdd(fullPath, 0))
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    await App.Ranking.EnsureLoadedAsync().ConfigureAwait(false);
                    IReadOnlyList<string> targetIds = await TryExtractPartnerIdsAsync(fullPath).ConfigureAwait(false);
                    if (targetIds.Count == 0)
                    {
                        AppLogger.Debug($"Messenger parse result empty: {fullPath}");
                        return;
                    }

                    var entries = targetIds.Select(BuildEntry).ToList();

                    AppLogger.Info($"Messenger toast. File={Path.GetFileName(fullPath)}, Targets={entries.Count}");
                    App.Toasts.ShowForFile(fullPath, entries);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("Failed to process messenger log event.", ex);
                }
                finally
                {
                    _processing.TryRemove(fullPath, out _);
                }
            });
        }

        /// <summary>아이디 하나를 랭킹에서 찾아 레벨과 닮은 아이디를 붙인다. 테스트 팝업도 이것을 쓴다.</summary>
        public static MessengerEtaEntry BuildEntry(string userId)
        {
            IReadOnlyList<EtaLookalike> lookalikes = EtaLookalikeFinder.Find(App.Ranking, userId, EtaRules.MaxLookalikes);
            return App.Ranking.TryGetProfile(userId, out EtaProfile profile)
                ? new MessengerEtaEntry(userId, profile.Level, lookalikes)
                : new MessengerEtaEntry(userId, null, lookalikes);
        }

        /// <summary>게임이 파일을 잡고 있는 동안은 잠깐 기다렸다가 다시 읽는다 (최대 30회 × 120 ms).</summary>
        private static async Task<IReadOnlyList<string>> TryExtractPartnerIdsAsync(string path)
        {
            for (int attempt = 0; attempt < 30; attempt++)
            {
                try
                {
                    if (!File.Exists(path))
                        return Array.Empty<string>();
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(stream, KoreanEncoding, detectEncodingFromByteOrderMarks: true);
                    string html = await reader.ReadToEndAsync().ConfigureAwait(false);
                    var result = new List<string>();
                    foreach (Match match in TargetLineRegex.Matches(html))
                    {
                        string id = match.Groups["name"].Value.Trim();
                        if (id.Length > 0 && !result.Contains(id, StringComparer.Ordinal))
                            result.Add(id);
                    }
                    return result;
                }
                catch (IOException) { await Task.Delay(120).ConfigureAwait(false); }
                catch (UnauthorizedAccessException) { await Task.Delay(120).ConfigureAwait(false); }
                catch { return Array.Empty<string>(); }
            }
            return Array.Empty<string>();
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            Stop();
        }
    }
}
