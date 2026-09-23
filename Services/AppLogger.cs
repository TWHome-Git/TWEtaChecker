using System;
using System.IO;
using System.Text;

namespace TWEtaChecker.Services
{
    /// <summary>Config\app.log에 한 줄씩 남기는 단순 로거. 1 MB를 넘으면 .old로 밀어낸다.</summary>
    public static class AppLogger
    {
        private static readonly string LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "app.log");
        private static readonly object Sync = new();
        private const long RollAtBytes = 1024 * 1024;

        public static void Info(string message) => Write("INFO", message, null);
        public static void Warn(string message, Exception? ex = null) => Write("WARN", message, ex);
        public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);
        public static void Debug(string message) => Write("DEBUG", message, null);

        private static void Write(string level, string message, Exception? ex)
        {
            try
            {
                lock (Sync)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                    if (File.Exists(LogPath) && new FileInfo(LogPath).Length > RollAtBytes)
                        File.Move(LogPath, LogPath + ".old", overwrite: true);
                    var line = new StringBuilder()
                        .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append(' ')
                        .Append('[').Append(level).Append("] ").Append(message);
                    if (ex != null)
                        line.Append(" | ").Append(ex.GetType().Name).Append(": ").Append(ex.Message);
                    File.AppendAllText(LogPath, line.Append(Environment.NewLine).ToString(), new UTF8Encoding(false));
                }
            }
            catch
            {
                // 로그 실패는 무시한다
            }
        }
    }
}
