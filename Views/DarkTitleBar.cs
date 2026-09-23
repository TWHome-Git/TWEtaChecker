using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TWEtaChecker.Views
{
    /// <summary>창 제목 표시줄을 어둡게 (Windows 10 20H1 이상). 지원하지 않으면 아무 일도 하지 않는다.</summary>
    internal static class DarkTitleBar
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        public static void Apply(Window window)
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(window).Handle;
                int enabled = 1;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref enabled, sizeof(int));
            }
            catch
            {
                // 옛 윈도우: 밝은 제목 표시줄 그대로
            }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
