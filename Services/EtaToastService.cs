using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using TWEtaChecker.Models;
using TWEtaChecker.Views;

namespace TWEtaChecker.Services
{
    /// <summary>
    /// 1:1 대화 파일마다 팝업 하나. 저장된 위치(없으면 화면 위 가운데)에서 아래로 쌓는다.
    /// 설정 창의 "위치 조정"은 끌 수 있는 미리보기 팝업을 띄우고, 끝내면 그 자리를 저장한다. 미리보기 중에는 실제 팝업을 숨긴다.
    /// 실제 팝업도 제목 줄을 끌어 옮기면 그 자리가 저장된다.
    /// </summary>
    public sealed class EtaToastService
    {
        private readonly Dictionary<string, EtaToastWindow> _windows = new(StringComparer.OrdinalIgnoreCase);
        private EtaToastWindow? _preview;
        private const double ToastWidth = 320;
        private const double DefaultTop = 42;
        private const double Gap = 8;

        public bool IsPreviewVisible => _preview?.IsVisible == true;

        public void ShowForFile(string filePath, IReadOnlyList<MessengerEtaEntry> entries)
        {
            if (string.IsNullOrWhiteSpace(filePath) || entries.Count == 0)
                return;

            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    if (!_windows.TryGetValue(filePath, out EtaToastWindow? window) || !window.IsLoaded)
                    {
                        window = new EtaToastWindow();
                        string key = filePath;
                        window.Closed += (_, _) =>
                        {
                            if (_windows.TryGetValue(key, out var current) && ReferenceEquals(current, window))
                                _windows.Remove(key);
                            Rearrange();
                        };
                        window.DragMoved += OnToastDragged;
                        _windows[filePath] = window;
                    }
                    window.SetEntries(entries);
                    Rearrange();
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Toast render failed.", ex);
                }
            });
        }

        public void ShowPositionPreview()
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                if (_preview == null || !_preview.IsLoaded)
                {
                    _preview = new EtaToastWindow();
                    _preview.Closed += (_, _) => { _preview = null; Rearrange(); };
                }
                _preview.SetEntries(new[]
                {
                    new MessengerEtaEntry("아이디1", 41, Array.Empty<EtaLookalike>()),
                    new MessengerEtaEntry("아이디2", 10, Array.Empty<EtaLookalike>()),
                });
                _preview.SetPreviewMode(true);
                var (left, top) = ResolveBasePosition();
                _preview.ShowAt(left, top);
                Rearrange();
            });
        }

        /// <summary>미리보기를 닫으면서 그 자리를 저장한다.</summary>
        public void ClosePositionPreview()
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                if (_preview == null)
                    return;
                App.Settings.ToastLeft = _preview.Left;
                App.Settings.ToastTop = _preview.Top;
                SettingsStore.Save(App.Settings);
                _preview.Close();
                _preview = null;
                Rearrange();
            });
        }

        /// <summary>
        /// 실제 팝업을 끌어 옮기면 그 팝업이 쌓인 순서만큼 위로 되돌린 자리를 새 기준 위치로 저장하고, 나머지 팝업도 그 아래로 다시 쌓는다.
        /// </summary>
        private void OnToastDragged(EtaToastWindow dragged)
        {
            double offset = 0;
            foreach (EtaToastWindow window in _windows.Values)
            {
                if (ReferenceEquals(window, dragged))
                    break;
                offset += window.ActualHeight + Gap;
            }
            App.Settings.ToastLeft = dragged.Left;
            App.Settings.ToastTop = dragged.Top - offset;
            SettingsStore.Save(App.Settings);
            Rearrange();
        }

        public void ApplyFontSize(double size)
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                foreach (EtaToastWindow window in _windows.Values)
                    window.SetFontSize(size);
                _preview?.SetFontSize(size);
                Rearrange();
            });
        }

        private void Rearrange()
        {
            var alive = _windows.Values.ToList();

            // 미리보기가 떠 있는 동안은 실제 팝업을 숨긴다 — 둘이 나란히 보이면 같은 창이 둘 뜬 것처럼 헷갈린다
            if (_preview?.IsVisible == true)
            {
                foreach (EtaToastWindow window in alive)
                    if (window.IsVisible) window.Hide();
                return;
            }

            var (left, baseTop) = ResolveBasePosition();
            var area = SystemParameters.WorkArea;
            double top = baseTop;
            foreach (EtaToastWindow window in alive)
            {
                window.UpdateLayout();
                double clampedLeft = Math.Max(area.Left, Math.Min(left, area.Right - ToastWidth));
                double clampedTop = Math.Max(area.Top, Math.Min(top, area.Bottom - window.ActualHeight));
                window.SetPreviewMode(false);
                window.ShowAt(clampedLeft, clampedTop);
                top += window.ActualHeight + Gap; // 팝업마다 높이가 다르므로 앞 팝업 높이를 쌓아 간다
            }
        }

        private static (double Left, double Top) ResolveBasePosition()
        {
            AppSettings s = App.Settings;
            if (s.ToastLeft.HasValue && s.ToastTop.HasValue)
                return (s.ToastLeft.Value, s.ToastTop.Value);
            var area = SystemParameters.WorkArea;
            return (area.Left + (area.Width - ToastWidth) / 2, DefaultTop);
        }
    }
}
