using System.Collections.Generic;
using System.Windows.Media;

namespace TWEtaChecker.Models
{
    /// <summary>팝업 표시 규칙. 사용자 설정이 아니라 프로그램에 고정한다 — 바꾸려면 여기를 고친다.</summary>
    public static class EtaRules
    {
        /// <summary>아이디에 들어 있으면 주의를 붙이는 문구 (영문은 대소문자 무시). 길드·클랜 관계자나 운영자를 흉내 내는 아이디에 흔히 쓰인다.</summary>
        public static readonly IReadOnlyList<string> SuspiciousPhrases = new[] { "길드", "클랜", "유저", "운영자", "1-", "2-", "3-", "M-", "S-" };

        /// <summary>닮은 아이디를 몇 개까지 보여줄지.</summary>
        public const int MaxLookalikes = 3;

        /// <summary>에타 레벨 구간 색 (1~20 / 21~40 / 41~60 / 61~80 / 81~). TWChatOverlay 기본값과 같다.</summary>
        public static Color LevelColor(int level) => level switch
        {
            <= 20 => Color.FromRgb(0xC8, 0xCD, 0xD2),
            <= 40 => Color.FromRgb(0x7E, 0xE0, 0x81),
            <= 60 => Color.FromRgb(0x5A, 0xC8, 0xE8),
            <= 80 => Color.FromRgb(0xC0, 0x8B, 0xFF),
            _ => Color.FromRgb(0xFF, 0xD8, 0x4A),
        };
    }
}
