using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using TWEtaChecker.Models;
using TWEtaChecker.Services;

namespace TWEtaChecker.Views
{
    /// <summary>1:1 대화 에타 팝업. 위치는 <see cref="EtaToastService"/>가 정하고, 제목 줄을 끌어 옮길 수 있다.</summary>
    public partial class EtaToastWindow : Window
    {
        private static readonly SolidColorBrush CautionBrush = new(Color.FromRgb(255, 123, 123));
        private double _fontSize;
        private bool _isPreviewMode;
        private IReadOnlyList<MessengerEtaEntry> _entries = Array.Empty<MessengerEtaEntry>();

        public EtaToastWindow()
        {
            InitializeComponent();
            _fontSize = App.Settings.FontSize;
            CloseButton.Click += (_, _) => Close();
            SourceInitialized += (_, _) => ApplyToolWindowStyle();
        }

        public void SetFontSize(double size)
        {
            _fontSize = size;
            RebuildRows();
        }

        public void SetEntries(IReadOnlyList<MessengerEtaEntry> entries)
        {
            _entries = entries ?? Array.Empty<MessengerEtaEntry>();
            RebuildRows();
        }

        public void SetPreviewMode(bool isPreview)
        {
            _isPreviewMode = isPreview;
            TitleText.Text = isPreview ? "위치 조정 — 끌어서 옮기세요" : "에타 레벨 확인";
        }

        /// <summary>아이디에서 의심 문구가 차지하는 글자 자리와, 들어 있는 문구 목록. 영문은 대소문자를 가리지 않는다.</summary>
        private static (bool[] Marked, List<string> Phrases) FindSuspiciousPhrases(string id)
        {
            var marked = new bool[id.Length];
            var phrases = new List<string>();
            foreach (string phrase in EtaRules.SuspiciousPhrases)
            {
                int at = id.IndexOf(phrase, StringComparison.OrdinalIgnoreCase);
                if (at < 0)
                    continue;
                phrases.Add(phrase);
                for (; at >= 0; at = id.IndexOf(phrase, at + 1, StringComparison.OrdinalIgnoreCase))
                    for (int k = at; k < at + phrase.Length; k++)
                        marked[k] = true;
            }
            return (marked, phrases);
        }

        /// <summary>한글·영문·숫자가 아닌 글자 — 비슷하게 보이는 아이디를 가려내기 위해 빨갛게 칠하는 기준.</summary>
        private static bool IsSpecial(char c)
            => !(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9'
                 || c is >= '가' and <= '힣' || c is >= 'ㄱ' and <= 'ㅣ');

        /// <summary>
        /// 상대마다 한 줄: 왼쪽에 아이디, 오른쪽에 레벨 알약.
        /// 아이디에 한글·영문·숫자 아닌 글자·의심 문구가 있으면 그 글자를 빨갛게 칠하고 아래에 "주의" 줄을 붙인다.
        /// 랭킹에 닮은 아이디가 있으면 그 아이디와 레벨을 아래에 빨갛게 적고, 길이가 같은 닮은꼴이면 다른 글자를 아이디 안에서 빨갛게 칠한다.
        /// 주의 표시는 랭킹에 없는 아이디에만 붙인다 — 랭킹에 있으면 실제 캐릭터가 확인된 것이다.
        /// 알약은 에타 레벨 구간 색을 글자·테두리에, 같은 색의 옅은 물결을 배경에 쓴다. 랭킹에 없으면 흐린 "정보 없음".
        /// </summary>
        private void RebuildRows()
        {
            EntryList.Children.Clear();
            double scale = _fontSize / 20.0;
            for (int i = 0; i < _entries.Count; i++)
            {
                MessengerEtaEntry entry = _entries[i];

                var idText = new TextBlock { FontSize = _fontSize, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
                idText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
                bool hasSpecial = false;
                // 랭킹에 있는 아이디는 실제 캐릭터가 확인된 것이므로 주의 표시(빨간 글자·주의 줄)를 붙이지 않는다
                bool caution = !entry.Level.HasValue;
                string? twin = entry.Lookalikes.FirstOrDefault(l => l.Kind == EtaLookalikeKind.Confusable && l.UserId.Length == entry.UserId.Length).UserId;
                var (phraseMarked, phrases) = FindSuspiciousPhrases(entry.UserId);
                for (int k = 0; k < entry.UserId.Length; k++)
                {
                    char c = entry.UserId[k];
                    var run = new Run(c.ToString());
                    bool special = IsSpecial(c);
                    hasSpecial |= special;
                    if (caution && (special || phraseMarked[k] || (twin != null && twin[k] != c)))
                        run.Foreground = CautionBrush;
                    idText.Inlines.Add(run);
                }

                var cautions = new StackPanel();
                double cautionSize = Math.Max(10, Math.Round(_fontSize * 0.6));
                if (caution && hasSpecial)
                    cautions.Children.Add(Caution("주의 - 특수 문자 포함", cautionSize));
                if (caution && phrases.Count > 0)
                    cautions.Children.Add(Caution($"주의 - 의심 문구 포함 ({string.Join(", ", phrases)})", cautionSize));
                foreach (EtaLookalike lookalike in caution ? entry.Lookalikes : Array.Empty<EtaLookalike>())
                {
                    string kind = lookalike.Kind == EtaLookalikeKind.Confusable ? "닮은꼴" : "비슷한";
                    cautions.Children.Add(Caution($"주의 - {kind} 아이디 {lookalike.UserId} (Lv {lookalike.Level})", cautionSize));
                }

                var pillText = new TextBlock
                {
                    Text = entry.Level.HasValue ? $"Lv {entry.Level.Value}" : "정보 없음",
                    FontSize = Math.Max(10, Math.Round(_fontSize * 0.7)),
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var pill = new Border
                {
                    Child = pillText,
                    CornerRadius = new CornerRadius(20),
                    Padding = new Thickness(Math.Round(10 * scale) + 2, 1, Math.Round(10 * scale) + 2, 2),
                    BorderThickness = new Thickness(1),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(10, 0, 0, 0),
                };
                if (entry.Level.HasValue)
                {
                    Color color = EtaRules.LevelColor(entry.Level.Value);
                    pillText.Foreground = new SolidColorBrush(color);
                    pill.BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, color.R, color.G, color.B));
                    pill.Background = new SolidColorBrush(Color.FromArgb(0x22, color.R, color.G, color.B));
                }
                else
                {
                    pillText.SetResourceReference(TextBlock.ForegroundProperty, "OverlayMutedTextBrush");
                    pill.SetResourceReference(Border.BorderBrushProperty, "ControlBorderBrush");
                    pill.Background = Brushes.Transparent;
                }

                var row = new Grid { Margin = new Thickness(6, 4, 6, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetColumn(pill, 1);
                Grid.SetRow(cautions, 1);
                Grid.SetColumnSpan(cautions, 2);
                row.Children.Add(idText);
                row.Children.Add(pill);
                row.Children.Add(cautions);
                EntryList.Children.Add(row);

                if (i < _entries.Count - 1)
                {
                    var rule = new Border { Height = 1, Margin = new Thickness(4, 0, 4, 0), Opacity = 0.7 };
                    rule.SetResourceReference(Border.BackgroundProperty, "OverlayCardBorderBrush");
                    EntryList.Children.Add(rule);
                }
            }
        }

        private static TextBlock Caution(string text, double size)
            => new() { Text = text, FontSize = size, Foreground = CautionBrush, Margin = new Thickness(0, 1, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };


        public void ShowAt(double left, double top)
        {
            Left = left;
            Top = top;
            if (!IsVisible)
                Show();
            Opacity = 1.0;
            Topmost = true;
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();
                NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER);
            }
            catch { }
        }

        /// <summary>
        /// 알트탭 목록에 안 뜨고, 절대 활성화되지 않는 툴윈도우. 뜨거나 눌리거나 끌려도 게임이 키보드 포커스를 잃지 않는다.
        /// 닫기 버튼·끌기를 위해 클릭은 받는다.
        /// </summary>
        private void ApplyToolWindowStyle()
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero)
                    return;
                int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
                int next = (exStyle | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE) & ~NativeMethods.WS_EX_TRANSPARENT;
                if (next != exStyle)
                    NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, next);
                HwndSource.FromHwnd(hwnd)?.AddHook(PreventActivationHook);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to set tool window style.", ex);
            }
        }

        /// <summary>클릭해도 활성화하지 않는다 (WS_EX_NOACTIVATE를 무시하는 경우 대비).</summary>
        private static IntPtr PreventActivationHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != NativeMethods.WM_MOUSEACTIVATE)
                return IntPtr.Zero;
            handled = true;
            return (IntPtr)NativeMethods.MA_NOACTIVATE;
        }

        /// <summary>실제 팝업을 제목 줄로 끌어 옮긴 뒤. 서비스가 이 자리를 새 팝업 위치로 저장한다.</summary>
        public event Action<EtaToastWindow>? DragMoved;

        /// <summary>
        /// 제목 줄을 끌면 옮긴다. 미리보기는 창 어디를 잡아도 된다.
        /// 닫기 버튼은 제 클릭을 먼저 처리하므로 여기까지 오지 않는다.
        /// </summary>
        private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || !(_isPreviewMode || HeaderGrid.IsMouseOver))
                return;
            double left = Left, top = Top;
            try { DragMove(); }
            catch (InvalidOperationException) { return; }
            if (!_isPreviewMode && (Math.Abs(Left - left) > 0.5 || Math.Abs(Top - top) > 0.5))
                DragMoved?.Invoke(this);
        }
    }
}
