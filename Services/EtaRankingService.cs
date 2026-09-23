using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace TWEtaChecker.Services
{
    public readonly record struct EtaProfile(int Level, string CharacterName);
    public readonly record struct EtaRankingEntry(int CharacterCode, string CharacterName, string UserId, int Level, int Essence, int OriginalOrder);

    /// <summary>
    /// 에타 랭킹(TWHomeDB의 eta_ranking.json)을 받아 아이디 → 레벨 색인을 만든다 (TWChatOverlay에서 가져옴).
    /// 매일 10시(랭킹 갱신 시각) 이후 한 번 원격을 확인하고, 그 밖에는 로컬 캐시를 쓴다.
    /// </summary>
    public sealed class EtaRankingService
    {
        private const string EtaRankingUrl = "https://raw.githubusercontent.com/TWHome-Git/TWHomeDB/main/eta_ranking.json";
        private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(6);
        private const int RefreshAnchorHourLocal = 10;

        private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(5) };

        private readonly RemoteJsonCacheClient _cacheClient = new("EtaRankingService", EtaRankingUrl, CacheTtl, HttpClient,
            refreshAnchorHourLocal: RefreshAnchorHourLocal, forceRemoteCheckOnFirstCall: true);

        private static readonly Dictionary<int, string> CharacterNameByCode = new()
        {
            [0] = "루시안", [1] = "보리스", [2] = "막시민", [3] = "시벨린", [4] = "조슈아", [5] = "란지에", [6] = "이자크",
            [7] = "밀라", [8] = "티치엘", [9] = "이스핀", [10] = "나야트레이", [11] = "아나이스", [12] = "클로에", [13] = "벤야",
            [14] = "이솔렛", [15] = "로아미니", [16] = "녹턴", [17] = "리체", [18] = "예프넨",
        };

        private Dictionary<string, EtaProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);
        private IReadOnlyList<EtaRankingEntry> _rankings = Array.Empty<EtaRankingEntry>();
        private DateTime? _payloadDateLocal;
        private int _isInitialized;
        private readonly SemaphoreSlim _loadLock = new(1, 1);

        public void InitializeAsync()
        {
            if (Interlocked.Exchange(ref _isInitialized, 1) != 0)
                return;
            _ = EnsureLoadedAsync();
        }

        public async Task EnsureLoadedAsync()
        {
            if (_rankings.Count > 0)
                return;
            await _loadLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_rankings.Count > 0)
                    return;
                string? json = await _cacheClient.GetJsonAsync(forceRefresh: false).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(json))
                    TryApplyRankingJson(json);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("ETA ranking load failed.", ex);
            }
            finally
            {
                _loadLock.Release();
            }
        }

        public async Task<bool> ForceRefreshAsync()
        {
            await _loadLock.WaitAsync().ConfigureAwait(false);
            try
            {
                string? json = await _cacheClient.GetJsonAsync(forceRefresh: true).ConfigureAwait(false);
                return !string.IsNullOrWhiteSpace(json) && TryApplyRankingJson(json);
            }
            finally
            {
                _loadLock.Release();
            }
        }

        public bool TryGetProfile(string userId, out EtaProfile profile)
        {
            profile = default;
            return !string.IsNullOrEmpty(userId) && _profiles.TryGetValue(userId, out profile);
        }

        public IReadOnlyList<EtaRankingEntry> GetRankings() => _rankings;

        public DateTime? GetLastPayloadDate() => _payloadDateLocal;

        private bool TryApplyRankingJson(string json)
        {
            try
            {
                var payload = JsonSerializer.Deserialize<EtaRankingPayload>(json);
                if (payload == null)
                    return false;

                // 신 형식: Servers = { "서버명": [rows] }. 아이디는 서버와 무관하게 공유되므로 전 서버를 병합한다. 구 형식(Rankings 평면 배열)도 지원.
                List<EtaRankingRow> rows = payload.Servers is { Count: > 0 }
                    ? payload.Servers.Values.Where(list => list != null).SelectMany(list => list!).ToList()
                    : payload.Rankings ?? new List<EtaRankingRow>();
                if (rows.Count == 0)
                    return false;

                var next = new Dictionary<string, EtaProfile>(StringComparer.OrdinalIgnoreCase);
                var rankingRows = new List<EtaRankingEntry>(rows.Count);
                int order = 0;
                foreach (var row in rows)
                {
                    if (string.IsNullOrWhiteSpace(row.UserId))
                        continue;
                    string characterName = CharacterNameByCode.TryGetValue(row.CharacterCode, out var name) ? name : $"코드{row.CharacterCode}";
                    rankingRows.Add(new EtaRankingEntry(row.CharacterCode, characterName, row.UserId, row.Level, row.Essence, order++));

                    var candidate = new EtaProfile(row.Level, characterName);
                    if (!next.TryGetValue(row.UserId, out var existing) || candidate.Level > existing.Level)
                        next[row.UserId] = candidate; // 여러 캐릭터면 가장 높은 레벨
                }

                _profiles = next;
                _rankings = new ReadOnlyCollection<EtaRankingEntry>(rankingRows);
                _payloadDateLocal = ResolvePayloadDate(payload);
                AppLogger.Info($"ETA ranking applied. Rows={rankingRows.Count}, Date={_payloadDateLocal:yyyy-MM-dd}");
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Warn("ETA JSON apply failed.", ex);
                return false;
            }
        }

        private static DateTime? ResolvePayloadDate(EtaRankingPayload payload)
        {
            if (payload.Date.HasValue)
                return payload.Date.Value.Date;
            if (!string.IsNullOrWhiteSpace(payload.CollectDate) &&
                (DateTime.TryParse(payload.CollectDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime parsed) || DateTime.TryParse(payload.CollectDate, out parsed)))
                return parsed.Date;
            return null;
        }

        private sealed class EtaRankingPayload
        {
            [JsonPropertyName("Date")] public DateTime? Date { get; set; }
            [JsonPropertyName("CollectDate")] public string? CollectDate { get; set; }
            [JsonPropertyName("Rankings")] public List<EtaRankingRow>? Rankings { get; set; }
            [JsonPropertyName("Servers")] public Dictionary<string, List<EtaRankingRow>>? Servers { get; set; }
        }

        private sealed class EtaRankingRow
        {
            [JsonPropertyName("CharacterCode")] public int CharacterCode { get; set; }
            [JsonPropertyName("UserId")] public string UserId { get; set; } = string.Empty;
            [JsonPropertyName("Level")] public int Level { get; set; }
            [JsonPropertyName("Essence")] public int Essence { get; set; }
        }
    }
}
