// This file is part of MSIFlux, based on YAMDCC.
// Original Copyright © 2023-2025 Sparronator9999
// Modifications Copyright © 2026 weijuns.
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version.
//
// This program is distributed in the hope that it will be useful, but
// WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY
// or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for
// more details.
//
// You should have received a copy of the GNU General Public License along with
// This program. If not, see <https://www.gnu.org/licenses/>.

using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Windows.Forms;

namespace MSIFlux.Common;

/// <summary>
/// A collection of miscellaneous useful utilities
/// </summary>
public static class Utils
{
    /// <summary>
    /// Shows an information dialog.
    /// </summary>
    /// <param name="message">
    /// The message to show in the info dialog.
    /// </param>
    /// <param name="title">
    /// The text to show in the title bar of the dialog.
    /// </param>
    /// <param name="buttons">
    /// One of the <see cref="MessageBoxButtons"/> values
    /// that specifies which buttons to display in the dialog.
    /// </param>
    /// <returns>
    /// One of the <see cref="DialogResult"/> values.
    /// </returns>
    public static DialogResult ShowInfo(string message, string title,
        MessageBoxButtons buttons = MessageBoxButtons.OK)
    {
        return MessageBox.Show(message, title, buttons, MessageBoxIcon.Asterisk);
    }

    /// <summary>
    /// Shows a warning dialog.
    /// </summary>
    /// <param name="message">
    /// The message to show in the warning dialog.
    /// </param>
    /// <param name="title">
    /// The text to show in the title bar of the dialog.
    /// </param>
    /// <param name="button">
    /// One of the <see cref="MessageBoxDefaultButton"/> values
    /// that specifies the default button for the dialog.
    /// </param>
    /// <returns>
    /// One of the <see cref="DialogResult"/> values.
    /// </returns>
    public static DialogResult ShowWarning(string message, string title,
        MessageBoxDefaultButton button = MessageBoxDefaultButton.Button1)
    {
        return MessageBox.Show(message, title, MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning, button);
    }

    /// <summary>
    /// Shows an error dialog.
    /// </summary>
    /// <param name="message">
    /// The message to show in the error dialog.
    /// </param>
    /// <returns>
    /// One of the <see cref="DialogResult"/> values.
    /// </returns>
    public static DialogResult ShowError(string message)
    {
        return MessageBox.Show(message, "Error",
            MessageBoxButtons.OK, MessageBoxIcon.Stop);
    }

    /// <summary>
    /// Gets a <see cref="Version"/> that can be used to compare
    /// application versions.
    /// </summary>
    /// <returns>
    /// A <see cref="Version"/> object corresponding to the entry application's
    /// version if parsing was successful, otherwise <see langword="null"/>.
    /// </returns>
    public static Version GetCurrentVersion()
    {
        return GetVersion(Application.ProductVersion);
    }

    public static Version GetVersion(string verString)
    {
        // expected version format: X.Y.Z-SUFFIX[.W]+REVISION,
        if (verString.Contains("-"))
        {
            // remove suffix and revision
            verString = verString.Remove(verString.IndexOf('-'));
        }
        else if (verString.Contains("+"))
        {
            // remove revision if suffix doesn't exist for some reason
            verString = verString.Remove(verString.IndexOf('+'));
        }

        return Version.TryParse(verString, out Version ver) ? ver : null;
    }
    public static int GetCurrentSuffixVer(int suffixNum = 0)
    {
        return GetSuffixVer(Application.ProductVersion, suffixNum);
    }

    public static int GetSuffixVer(string verString, int suffixNum = 0)
    {
        // format: X.Y.Z-SUFFIX[.W]+REVISION,
        // where W is a beta/release candidate version if applicable
        if (verString.Contains("-"))
        {
            verString = verString.Remove(0, verString.IndexOf('-') + 1);
        }

        if (verString.Contains("."))
        {
            suffixNum++;
            int index;
            do
            {
                // SUFFIX[.W][-SUFFIX2[.V]...]+REVISION
                index = verString.IndexOf('.');
                if (index == -1)
                {
                    return -1;
                }
                verString = verString.Remove(0, index + 1);
                suffixNum--;
            }
            while (suffixNum > 0);

            if (verString.Contains("-"))
            {
                // remove other suffixes
                verString = verString.Remove(verString.IndexOf('-'));
            }
            else if (verString.Contains("+"))
            {
                // remove Git hash, if it exists (for "dev" detection)
                verString = verString.Remove(verString.IndexOf('+'));
            }
        }
        else
        {
            // suffix with version probably doesn't exist...
            return -1;
        }

        return int.TryParse(verString, out int version) ? version : -1;
    }

    public static string GetCurrentVerSuffix()
    {
        return GetVerSuffix(Application.ProductVersion);
    }

    public static string GetVerSuffix(string verString, int suffixNum = 0)
    {
        // format: X.Y.Z-SUFFIX[.W]+REVISION,
        // where W is a beta/release candidate version if applicable
        string verSuffix = verString;

        if (verSuffix.Contains("-"))
        {
            suffixNum++;
            int index;
            do
            {
                // SUFFIX[.W][-SUFFIX2[.V]...]+REVISION
                index = verSuffix.IndexOf('-');
                verSuffix = verSuffix.Remove(0, index + 1);
                suffixNum--;
            }
            while (index != -1 && suffixNum > 0);

            if (verSuffix.Contains("."))
            {
                // remove suffix version number
                verSuffix = verSuffix.Remove(verSuffix.IndexOf('.'));
            }
            else if (verSuffix.Contains("+"))
            {
                // remove Git hash, if it exists (for "dev" detection)
                verSuffix = verSuffix.Remove(verSuffix.IndexOf('+'));
            }
        }
        else
        {
            // suffix probably doesn't exist...
            verSuffix = string.Empty;
        }

        return verSuffix.ToLowerInvariant();
    }

    public static string GetVerString()
    {
        // format: X.Y.Z-SUFFIX[.W]+REVISION,
        // where W is a beta/release candidate version if applicable
        string prodVer = Application.ProductVersion;

        return GetCurrentVerSuffix() switch
        {
            // only show the version number (e.g. X.Y.Z):
            "release" => prodVer.Remove(prodVer.IndexOf('-')),
            "dev" => prodVer.Contains("+")
                // probably a development release (e.g. X.Y.Z-dev+REVISION);
                // show shortened Git commit hash if it exists:
                ? prodVer.Remove(prodVer.IndexOf('+') + 8)
                // Return the product version if not in expected format
                : prodVer,
            // everything else (i.e. beta, RC, etc.)
            _ => prodVer.Contains(".") && prodVer.Contains("+")
                // Beta releases should be in format X.Y.Z-beta.W+REVISION.
                // Remove the revision (i.e. only show X.Y.Z-beta.W):
                ? prodVer.Remove(prodVer.IndexOf('+'))
                // Just return the product version if not in expected format
                : prodVer,
        };
    }

    /// <summary>
    /// Gets the Git revision of this program, if available.
    /// </summary>
    /// <returns>
    /// The Git hash of the program version if available,
    /// otherwise <see cref="string.Empty"/>.
    /// </returns>
    public static string GetRevision()
    {
        string prodVer = Application.ProductVersion;

        return prodVer.Contains("+")
            ? prodVer.Remove(0, prodVer.IndexOf('+') + 1)
            : string.Empty;
    }

    public static Icon GetEntryAssemblyIcon()
    {
        return Icon.ExtractAssociatedIcon(Assembly.GetEntryAssembly().Location);
    }

    public static string GetAppTitle()
    {
        return Assembly.GetEntryAssembly().GetCustomAttribute<AssemblyTitleAttribute>().Title;
    }

    /// <summary>
    /// Gets whether the application is running with administrator privileges.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if the application is running as
    /// an administrator, otherwise <see langword="false"/>.
    /// </returns>
    public static bool IsAdmin()
    {
        try
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Installs the specified .NET Framework
    /// service to the local computer.
    /// </summary>
    /// <remarks>
    /// The service is not started automatically. Use
    /// <see cref="StartService(string)"/> to start it if needed.
    /// </remarks>
    /// <param name="svcExe">
    /// The path to the service executable.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the service installation
    /// was successful, otherwise <see langword="false"/>.
    /// </returns>
    public static bool InstallService(string svcExe)
    {
        int exitCode = RunCmd("sc", $"create msifluxsvc binPath= \"{svcExe}.exe\" start= auto DisplayName= \"MSIFlux Service\"");
        if (exitCode == 0)
        {
            RunCmd("sc", "description msifluxsvc \"MSIFlux - Yet Another MSI Dragon Center Clone Service\"");
        }
        return exitCode == 0;
    }

    /// <summary>
    /// Uninstalls the specified service from the local computer.
    /// </summary>
    /// <param name="svcExe">
    /// The path to the service executable (unused, kept for API compatibility).
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the service uninstallation
    /// was successful, otherwise <see langword="false"/>.
    /// </returns>
    public static bool UninstallService(string svcExe)
    {
        RunCmd("net", "stop msifluxsvc");
        int exitCode = RunCmd("sc", "delete msifluxsvc");
        return exitCode == 0;
    }

    /// <summary>
    /// Starts the specified service.
    /// </summary>
    /// <param name="svcName">
    /// The service name, as shown in <c>services.msc</c>
    /// (NOT to be confused with its display name).
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the service started successfully
    /// (or is already running), otherwise <see langword="false"/>.
    /// </returns>
    public static bool StartService(string svcName)
    {
        return RunCmd("net", $"start {svcName}") == 0;
    }

    /// <summary>
    /// Stops the specified service.
    /// </summary>
    /// <param name="svcName">
    /// The service name, as shown in <c>services.msc</c>
    /// (NOT to be confused with its display name).
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the service was stopped successfully
    /// (or is already stopped), otherwise <see langword="false"/>.
    /// </returns>
    public static bool StopService(string svcName)
    {
        try
        {
            using (ServiceController sc = new(svcName))
            {
                if (sc.Status == ServiceControllerStatus.Stopped)
                    return true;

                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
                return sc.Status == ServiceControllerStatus.Stopped;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MSIFlux] StopService({svcName}) failed: {ex.Message}");
            // Fallback to net stop
            return RunCmdAdmin("net", $"stop \"{svcName}\"") == 0;
        }
    }

    /// <summary>
    /// Stops a service and disables it to prevent automatic restart.
    /// Uses ServiceController API first, falls back to sc.exe if needed.
    /// </summary>
    public static bool StopAndDisableService(string svcName)
    {
        bool stopped = false;

        // Step 1: Stop the service using ServiceController
        try
        {
            using (ServiceController sc = new(svcName))
            {
                sc.Refresh();
                if (sc.Status == ServiceControllerStatus.Stopped)
                {
                    stopped = true;
                }
                else
                {
                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
                    stopped = sc.Status == ServiceControllerStatus.Stopped;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MSIFlux] ServiceController.Stop({svcName}) failed: {ex.Message}");
        }

        // Step 2: Fallback — try net stop + taskkill the service process
        if (!stopped)
        {
            RunCmdAdmin("net", $"stop \"{svcName}\"");
            System.Threading.Thread.Sleep(1000);

            // Verify again
            try
            {
                using (ServiceController sc = new(svcName))
                {
                    sc.Refresh();
                    stopped = sc.Status == ServiceControllerStatus.Stopped;
                }
            }
            catch { }
        }

        // Step 3: Disable the service using sc.exe (requires admin)
        int disableResult = RunCmdAdmin("sc", $"config \"{svcName}\" start= disabled");

        // Step 4: Verify final state
        try
        {
            using (ServiceController sc = new(svcName))
            {
                sc.Refresh();
                if (sc.Status == ServiceControllerStatus.Stopped ||
                    sc.Status == ServiceControllerStatus.StopPending)
                {
                    return true;
                }
            }
        }
        catch { }

        return stopped && disableResult == 0;
    }

    /// <summary>
    /// Enables a service (sets to demand start) and starts it.
    /// </summary>
    public static bool EnableAndStartService(string svcName)
    {
        // First, set the service to demand start
        RunCmdAdmin("sc", $"config \"{svcName}\" start= demand");

        try
        {
            using (ServiceController sc = new(svcName))
            {
                sc.Refresh();
                if (sc.Status == ServiceControllerStatus.Running)
                    return true;

                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
                return sc.Status == ServiceControllerStatus.Running;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MSIFlux] ServiceController.Start({svcName}) failed: {ex.Message}");
            // Fallback to net start
            return RunCmdAdmin("net", $"start \"{svcName}\"") == 0;
        }
    }

    /// <summary>
    /// Runs a command with admin privileges. Works correctly regardless of
    /// whether the current process is admin or not.
    /// </summary>
    private static int RunCmdAdmin(string exe, string args)
    {
        try
        {
            if (IsAdmin())
            {
                // Running as admin: use CreateProcess directly (no UAC needed)
                using (Process p = new())
                {
                    p.StartInfo = new ProcessStartInfo(exe, args)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                    };
                    p.Start();
                    p.WaitForExit(15000);
                    return p.ExitCode;
                }
            }
            else
            {
                // Not admin: use ShellExecute + runas to elevate
                using (Process p = new())
                {
                    p.StartInfo = new ProcessStartInfo(exe, args)
                    {
                        UseShellExecute = true,
                        Verb = "runas",
                        CreateNoWindow = true,
                    };
                    p.Start();
                    p.WaitForExit(15000);
                    return p.ExitCode;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MSIFlux] RunCmdAdmin({exe} {args}) failed: {ex.Message}");
            return -1;
        }
    }

    /// <summary>
    /// Checks to see if the specified service
    /// is installed on the computer.
    /// </summary>
    /// <param name="svcName">
    /// The service name, as shown in <c>services.msc</c>
    /// (NOT to be confused with its display name).
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the service was
    /// found, otherwise <see langword="false"/>.
    /// </returns>
    public static bool ServiceExists(string svcName)
    {
        foreach (ServiceController service in ServiceController.GetServices())
        {
            if (service.ServiceName == svcName)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Checks to see if any of MSI Center's services are running.
    /// </summary>
    /// <param name="services">
    /// The names of the MSI Center services that are running.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if any of MSI Center's services
    /// are running, otherwise <see langword="false"/>.
    /// </returns>
    public static bool IsMSIServiceRunning(out string[] services)
    {
        Dictionary<string, string> msiCenterSvcs = new()
        {
            { "Micro Star SCM", "Micro Star SCM (MSIService.exe)" },
            { "MSI_Center_Service", "MSI Center Service (MSI_Central_Service.exe)" },
            // MSI Foundation Service is excluded: GPU mode switching requires it.
        };

        List<string> svcList = [];

        bool msiCenter = false;
        foreach (ServiceController service in ServiceController.GetServices())
        {
            for (int i = 0; i < msiCenterSvcs.Count; i++)
            {
                if (msiCenterSvcs.TryGetValue(service.ServiceName, out string value) &&
                    (service.Status == ServiceControllerStatus.Running ||
                    service.Status == ServiceControllerStatus.StartPending))
                {
                    msiCenter = true;
                    svcList.Add(value);
                }
            }
        }
        services = [.. svcList];
        return msiCenter;
    }

    /// <summary>
    /// Checks to see if the specified service
    /// is running or pending start on the computer.
    /// </summary>
    /// <param name="svcName">
    /// The service name, as shown in <c>services.msc</c>
    /// (NOT to be confused with its display name).
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the service is
    /// running, otherwise <see langword="false"/>.
    /// </returns>
    public static bool ServiceRunning(string svcName)
    {
        try
        {
            using (ServiceController service = new(svcName))
            {
                return service.Status
                    is ServiceControllerStatus.Running
                    or ServiceControllerStatus.StartPending;
            }
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Runs the specified executable as admin,
    /// with the specified arguments.
    /// </summary>
    /// <remarks>
    /// The process will be started with <see cref="ProcessStartInfo.UseShellExecute"/>
    /// set to <see langword="false"/>, except if the calling application is not
    /// running as an administrator, in which case
    /// <see cref="ProcessStartInfo.UseShellExecute"/> is set to
    /// <see langword="true"/> instead.
    /// </remarks>
    /// <param name="exe">
    /// The path to the executable to run.
    /// </param>
    /// <param name="args">
    /// The arguments to pass to the executable.
    /// </param>
    /// <param name="waitExit">
    /// <see langword="true"/> to wait for the executable to exit
    /// before returning, otherwise <see langword="false"/>.
    /// </param>
    /// <returns>
    /// The exit code returned by the executable (unless <paramref name="waitExit"/>
    /// is <see langword="true"/>, in which case 0 will always be returned).
    /// </returns>
    /// <exception cref="Win32Exception"/>
    public static int RunCmd(string exe, string args, bool waitExit = true)
    {
        try
        {
            if (IsAdmin())
            {
                // Running as admin: use CreateProcess directly (no UAC needed)
                using (Process p = new()
                {
                    StartInfo = new ProcessStartInfo(exe, args)
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                    },
                })
                {
                    p.Start();
                    if (waitExit)
                    {
                        p.WaitForExit();
                        return p.ExitCode;
                    }
                }
            }
            else
            {
                // Not admin: must use ShellExecute + runas to elevate
                using (Process p = new()
                {
                    StartInfo = new ProcessStartInfo(exe, args)
                    {
                        CreateNoWindow = true,
                        UseShellExecute = true,
                        Verb = "runas",
                    },
                })
                {
                    p.Start();
                    if (waitExit)
                    {
                        p.WaitForExit();
                        return p.ExitCode;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MSIFlux] RunCmd({exe} {args}) failed: {ex.Message}");
        }
        return 0;
    }

    /// <summary>
    /// Gets the computer model name from registry.
    /// </summary>
    /// <returns>
    /// The computer model if the function succeeds,
    /// otherwise <see cref="string.Empty"/>.'
    /// </returns>
    public static string GetPCModel()
    {
        return GetBIOSRegValue("SystemProductName");
    }

    /// <summary>
    /// Gets the computer manufacturer from registry.
    /// </summary>
    /// <returns>
    /// The computer manufacturer if the function succeeds,
    /// otherwise <see cref="string.Empty"/>.
    /// </returns>
    public static string GetPCManufacturer()
    {
        return GetBIOSRegValue("SystemManufacturer");
    }

    private static string GetBIOSRegValue(string name)
    {
        using (RegistryKey biosKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS"))
        {
            return ((string)biosKey?.GetValue(name, string.Empty)).Trim();
        }
    }

    /// <summary>
    /// Gets the PID of a running process by its image name.
    /// </summary>
    /// <param name="exeName">The image name of the process (e.g. "Feature_Manager.exe").</param>
    /// <returns>The PID string if found, otherwise <see langword="null"/>.</returns>
    public static string GetProcessPid(string exeName)
    {
        try
        {
            using (Process process = new Process())
            {
                process.StartInfo.FileName = "tasklist";
                process.StartInfo.Arguments = $"/fi \"IMAGENAME eq {exeName}\" /fo list";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.CreateNoWindow = true;
                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                foreach (string line in output.Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string trimmedLine = line.Trim();
                    if (trimmedLine.StartsWith("PID:"))
                    {
                        return trimmedLine.Split(':')[1].Trim();
                    }
                }
                return null;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MSIFlux] Error getting process PID for {exeName}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Checks whether a process with the specified image name is running.
    /// </summary>
    /// <param name="exeName">The image name of the process.</param>
    /// <returns><see langword="true"/> if the process is running.</returns>
    public static bool IsProcessRunning(string exeName)
    {
        return !string.IsNullOrEmpty(GetProcessPid(exeName));
    }

    /// <summary>
    /// Stops a process by its image name using taskkill.
    /// </summary>
    /// <param name="displayName">A human-readable name for logging.</param>
    /// <param name="exeName">The image name of the process.</param>
    /// <returns><see langword="true"/> if the process was stopped or was not running.</returns>
    public static bool StopProcessByName(string displayName, string exeName)
    {
        try
        {
            string pid = GetProcessPid(exeName);
            if (string.IsNullOrEmpty(pid))
            {
                Debug.WriteLine($"[MSIFlux] {displayName} is not running");
                return true;
            }

            Debug.WriteLine($"[MSIFlux] Stopping {displayName} (PID: {pid})...");
            using (Process process = new Process())
            {
                process.StartInfo.FileName = "taskkill";
                process.StartInfo.Arguments = $"/f /pid {pid}";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.Start();
                process.WaitForExit();
            }

            System.Threading.Thread.Sleep(800);
            if (string.IsNullOrEmpty(GetProcessPid(exeName)))
            {
                Debug.WriteLine($"[MSIFlux] {displayName} stopped successfully");
                return true;
            }
            else
            {
                Debug.WriteLine($"[MSIFlux] Failed to stop {displayName}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MSIFlux] Error stopping process {displayName}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Checks the status of a Windows service by name.
    /// </summary>
    /// <param name="displayName">A human-readable name for logging.</param>
    /// <param name="serviceName">The service name.</param>
    /// <returns>Status string: "运行中", "已停止", "其他状态", or "查询失败".</returns>
    public static string CheckServiceStatus(string displayName, string serviceName)
    {
        try
        {
            using (Process process = new Process())
            {
                process.StartInfo.FileName = "sc";
                process.StartInfo.Arguments = $"query \"{serviceName}\"";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.CreateNoWindow = true;
                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                if (output.Contains("RUNNING"))
                    return "运行中";
                else if (output.Contains("STOPPED"))
                    return "已停止";
                else
                    return "其他状态";
            }
        }
        catch
        {
            return "查询失败";
        }
    }
}
