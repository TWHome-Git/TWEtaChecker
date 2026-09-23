using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace TWEtaChecker.Services
{
    /// <summary>GitHub 최신 릴리즈 하나. Version은 태그(예: 1.0.1, v1.0.1)에서 읽는다.</summary>
    public sealed record ReleaseInfo(Version Version, string Tag, string PageUrl, string ExeUrl, long ExeSize);

    /// <summary>
    /// GitHub 릴리즈로 자동 업데이트.
    /// 최신 릴리즈(프리릴리즈 제외)의 태그가 지금 버전보다 높고 TWEtaChecker.exe가 첨부돼 있으면 새 버전으로 본다.
    /// 교체는 실행 중인 exe를 .old로 이름만 바꾸고(윈도우는 실행 중인 파일의 이름 변경을 허용한다) 새 exe를 그 자리에 둔 뒤 다시 시작한다.
    /// </summary>
    public sealed class UpdateService
    {
        public const string ReleasesPageUrl = "https://github.com/TWHome-Git/TWEtaChecker/releases/latest";
        private const string LatestReleaseApi = "https://api.github.com/repos/TWHome-Git/TWEtaChecker/releases/latest";
        private const string AssetName = "TWEtaChecker.exe";
        public const string WaitPidArgument = "--wait-pid";

        /// <summary>지금 실행 중인 버전 (csproj의 Version).</summary>
        public static Version CurrentVersion { get; } = Normalize(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0));

        private static readonly HttpClient Http = CreateHttpClient(); // CurrentVersion을 쓰므로 그 뒤에 둔다

        public static string CurrentVersionText => $"v{CurrentVersion.ToString(3)}";

        /// <summary>
        /// 제자리 교체가 가능한 배포본인지. dotnet build 출력(옆에 TWEtaChecker.dll이 있음)은 교체하면 깨지므로 릴리즈 페이지만 연다.
        /// </summary>
        public static bool CanSelfUpdate
        {
            get
            {
                string? exe = Environment.ProcessPath;
                return !string.IsNullOrEmpty(exe)
                    && Path.GetFileName(exe).Equals(AssetName, StringComparison.OrdinalIgnoreCase)
                    && !File.Exists(Path.Combine(Path.GetDirectoryName(exe)!, "TWEtaChecker.dll"));
            }
        }

        /// <summary>마지막 확인에서 찾은 새 버전. 없으면 null.</summary>
        public ReleaseInfo? Available { get; private set; }

        public event Action? AvailableChanged;

        /// <summary>마지막 확인이 GitHub에 닿았는지. false면 null 결과는 "최신"이 아니라 "모름"이다.</summary>
        public bool LastCheckSucceeded { get; private set; }

        /// <summary>새 버전이 있으면 그 정보를, 없거나 확인하지 못하면 null.</summary>
        public async Task<ReleaseInfo?> CheckAsync()
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
                request.Headers.Accept.ParseAdd("application/vnd.github+json");
                using HttpResponseMessage response = await Http.SendAsync(request).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                ReleaseInfo? latest = Parse(json);
                ReleaseInfo? newer = latest != null && latest.Version > CurrentVersion ? latest : null;
                LastCheckSucceeded = true;
                AppLogger.Info($"Update check. Current={CurrentVersionText}, Latest={latest?.Tag ?? "(none)"}");
                if (!Equals(newer, Available))
                {
                    Available = newer;
                    AvailableChanged?.Invoke();
                }
                return newer;
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Update check failed.", ex);
                LastCheckSucceeded = false;
                return null;
            }
        }

        private static ReleaseInfo? Parse(string json)
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            string tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out Version? version))
                return null;
            string page = root.TryGetProperty("html_url", out JsonElement h) ? h.GetString() ?? ReleasesPageUrl : ReleasesPageUrl;
            foreach (JsonElement asset in root.GetProperty("assets").EnumerateArray())
            {
                if (!string.Equals(asset.GetProperty("name").GetString(), AssetName, StringComparison.OrdinalIgnoreCase))
                    continue;
                return new ReleaseInfo(Normalize(version), tag, page,
                    asset.GetProperty("browser_download_url").GetString() ?? string.Empty,
                    asset.GetProperty("size").GetInt64());
            }
            AppLogger.Warn($"Release {tag} has no {AssetName} asset.");
            return null;
        }

        /// <summary>
        /// 새 exe를 받아 지금 exe와 바꾸고 새 exe를 띄운다. 성공하면 true — 부른 쪽이 곧바로 프로그램을 끝내야 한다.
        /// 새 exe는 <see cref="WaitPidArgument"/>로 이 프로세스가 끝나기를 기다린 뒤 시작한다.
        /// </summary>
        public async Task<bool> DownloadAndInstallAsync(ReleaseInfo release, IProgress<double>? progress)
        {
            string exe = Environment.ProcessPath!;
            string newPath = exe + ".new";
            string oldPath = exe + ".old";
            try
            {
                using (HttpResponseMessage response = await Http.GetAsync(release.ExeUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    long total = response.Content.Headers.ContentLength ?? release.ExeSize;
                    await using Stream source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                    await using FileStream target = new(newPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    byte[] buffer = new byte[81920];
                    long copied = 0;
                    int read;
                    while ((read = await source.ReadAsync(buffer).ConfigureAwait(false)) > 0)
                    {
                        await target.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
                        copied += read;
                        if (total > 0)
                            progress?.Report((double)copied / total);
                    }
                }

                // 받다 끊긴 파일로 바꾸지 않는다
                if (release.ExeSize > 0 && new FileInfo(newPath).Length != release.ExeSize)
                    throw new IOException($"Downloaded size mismatch: {new FileInfo(newPath).Length} != {release.ExeSize}");

                File.Delete(oldPath);
                File.Move(exe, oldPath);
                try
                {
                    File.Move(newPath, exe);
                }
                catch
                {
                    File.Move(oldPath, exe); // 되돌린다
                    throw;
                }

                Process.Start(new ProcessStartInfo(exe, $"{WaitPidArgument} {Environment.ProcessId}") { UseShellExecute = false });
                AppLogger.Info($"Updated to {release.Tag}; restarting.");
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Update to {release.Tag} failed.", ex);
                try { File.Delete(newPath); } catch { }
                return false;
            }
        }

        /// <summary>업데이트 뒤 첫 실행: 이전 프로세스가 끝나기를 기다리고, 남은 .old를 지운다.</summary>
        public static void FinishPendingUpdate(string[] args)
        {
            int at = Array.FindIndex(args, a => a.Equals(WaitPidArgument, StringComparison.OrdinalIgnoreCase));
            if (at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], out int pid))
            {
                try { Process.GetProcessById(pid).WaitForExit(15000); }
                catch (ArgumentException) { } // 이미 끝남
            }

            string? exe = Environment.ProcessPath;
            if (exe == null)
                return;
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    if (File.Exists(exe + ".old"))
                        File.Delete(exe + ".old");
                    return;
                }
                catch (IOException) { System.Threading.Thread.Sleep(300); }
                catch (UnauthorizedAccessException) { System.Threading.Thread.Sleep(300); }
            }
        }

        public static void OpenReleasesPage(string? url = null)
        {
            try { Process.Start(new ProcessStartInfo(url ?? ReleasesPageUrl) { UseShellExecute = true }); }
            catch (Exception ex) { AppLogger.Warn("Failed to open releases page.", ex); }
        }

        private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"TWEtaChecker/{CurrentVersion.ToString(3)}"); // GitHub API는 User-Agent가 필요하다
            return client;
        }
    }
}
