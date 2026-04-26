using System;
using System.IO;
using YAMDCC.Common;

namespace YAMDCC.GUI.Helpers
{
    public static class Logger
    {
        public static void WriteLine(string message)
        {
            try
            {
                string logPath = Path.Combine(
                    Paths.Logs,
                    $"GUI_{DateTime.Now:yyyyMMdd}.log");

                Directory.CreateDirectory(Paths.Logs);
                File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] {message}\n");
            }
            catch
            {
            }
        }
    }
}
