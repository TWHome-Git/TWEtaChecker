using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TWEtaChecker.Services;

namespace TWEtaChecker.Models
{
    /// <summary>설정 전부. Config\settings.json 한 파일에 그대로 저장된다 — 메모장으로 고쳐도 된다.</summary>
    public sealed class AppSettings
    {
        /// <summary>테일즈위버 설치 폴더 (예: C:\Nexon\TalesWeaver). 메신저 로그는 그 아래 MsgerLog. null이면 첫 실행 때 묻는다.</summary>
        public string? TalesWeaverFolder { get; set; }

        /// <summary>팝업의 아이디 글자 크기. 알약·주의 줄은 이 값에 비례한다.</summary>
        public double FontSize { get; set; } = 20;

        /// <summary>팝업 왼쪽 위 좌표. 둘 다 있어야 쓰고, 없으면 화면 가로 가운데 위쪽.</summary>
        public double? ToastLeft { get; set; }
        public double? ToastTop { get; set; }
    }

    public static class SettingsStore
    {
        public static readonly string ConfigDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config");
        private static readonly string FilePath = Path.Combine(ConfigDirectory, "settings.json");
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 한글을 \uXXXX로 쓰지 않는다
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };
        private static readonly object Sync = new();

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Settings load failed; using defaults.", ex);
            }
            var fresh = new AppSettings();
            Save(fresh); // 처음 실행: 사용자가 열어 볼 수 있게 기본값을 써 둔다
            return fresh;
        }

        public static void Save(AppSettings settings)
        {
            try
            {
                lock (Sync)
                {
                    Directory.CreateDirectory(ConfigDirectory);
                    File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Options), new UTF8Encoding(false));
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Settings save failed.", ex);
            }
        }
    }
}
