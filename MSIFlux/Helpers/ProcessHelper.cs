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
