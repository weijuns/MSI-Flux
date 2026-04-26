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

using System;
using System.ServiceProcess;
using System.Windows.Forms;
using MSIFlux.Common;
using MSIFlux.Common.Configs;
using MSIFlux.Common.Dialogs;
using MSIFlux.Common.Logs;

namespace MSIFlux.Service;

internal static class Program
{
    /// <summary>
    /// The <see cref="Logger"/> instance to write logs to.
    /// </summary>
    private static readonly Logger Log = new()
    {
        LogDir = Paths.Logs,
        ConsoleLevel = LogLevel.None,
        FileLevel = LogLevel.Debug,
    };

    /// <summary>
    /// The main entry point for the application.
    /// </summary>
    private static void Main()
    {
        AppDomain.CurrentDomain.UnhandledException +=
            new UnhandledExceptionEventHandler(UnhandledException);

        if (Environment.UserInteractive)
        {
            Utils.ShowError(Strings.GetString("errDirectRun"));
        }
        else
        {
            Log.Info(
                $"OS version: {Environment.OSVersion}\n" +
                $"Service version: {Application.ProductVersion}");

            Log.FileLevel = CommonConfig.GetLogLevel();
            Log.Debug("Log level is set to debug mode.");
            ServiceBase.Run(new FanControlService(Log));
        }
    }

    private static void UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        new CrashDialog((Exception)e.ExceptionObject).ShowDialog();
    }
}
