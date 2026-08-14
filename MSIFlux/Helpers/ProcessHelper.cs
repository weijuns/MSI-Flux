using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows.Forms;

namespace MSIFlux.GUI.Helpers
{
    public static class ProcessHelper
    {
        public static bool IsUserAdministrator()
        {
            try
            {
                WindowsIdentity user = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(user);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        public static void RunAsAdmin()
        {
            try
            {
                // 诊断: 记录 RunAsAdmin 调用来源
                try
                {
                    var path = System.IO.Path.Combine(MSIFlux.Common.Paths.Logs, "diag_ensure.txt");
                    var stack = new System.Diagnostics.StackTrace(1, true);
                    var frame = stack.GetFrame(0);
                    System.IO.File.AppendAllText(path,
                        $"[{DateTime.Now:HH:mm:ss.fff}] RunAsAdmin 被调用, 来源: {frame?.GetMethod()?.DeclaringType?.Name}.{frame?.GetMethod()?.Name}\r\n");
                }
                catch { }

                ProcessStartInfo proc = new ProcessStartInfo
                {
                    UseShellExecute = true,
                    WorkingDirectory = Environment.CurrentDirectory,
                    FileName = Application.ExecutablePath,
                    Verb = "runas"
                };
                Process.Start(proc);
                Application.Exit();
            }
            catch
            {
            }
        }
    }
}
