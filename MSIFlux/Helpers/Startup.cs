using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32.TaskScheduler;
using Microsoft.Win32.TaskScheduler.V1Interop;
using Microsoft.Win32.TaskScheduler.V2Interop;

namespace MSIFlux.GUI.Helpers
{
    public class Startup
    {
        static string taskName = "MSI Flux";
        static string strExeFilePath = Application.ExecutablePath.Trim();

        public static bool IsScheduled()
        {
            try
            {
                using (TaskService taskService = new TaskService())
                {
                    // 计划任务 + 服务启动类型 两者都算开机自启. 以计划任务为准,
                    // 服务启动类型作为辅助判断 (若服务也是 auto 也视为已开启).
                    bool taskExists = taskService.RootFolder.AllTasks.Any(t => t.Name == taskName);
                    return taskExists || ServiceManager.IsAutoStart();
                }
            }
            catch (Exception e)
            {
                Logger.WriteLine("Can't check startup task status: " + e.Message);
                return false;
            }
        }

        public static bool Schedule()
        {
            // 计划任务写入系统 RootFolder 需要管理员权限, 服务启动类型切换也需要提权.
            // 通过提权子进程一次性完成, 不退出当前程序.
            if (!ProcessHelper.IsUserAdministrator())
            {
                int code = ServiceManager.RelaunchElevated("--enable-autostart");
                return code == 0;
            }

            DoSchedule();
            TrySetServiceAutoStart(true);
            return true;
        }

        public static bool UnSchedule()
        {
            if (!ProcessHelper.IsUserAdministrator())
            {
                int code = ServiceManager.RelaunchElevated("--disable-autostart");
                return code == 0;
            }

            DoUnSchedule();
            TrySetServiceAutoStart(false);
            return true;
        }

        /// <summary>实际创建计划任务 (调用方必须已提权).</summary>
        internal static void DoSchedule()
        {
            using (TaskDefinition td = TaskService.Instance.NewTask())
            {
                td.RegistrationInfo.Description = "MSIFlux Auto Start";
                td.Triggers.Add(new LogonTrigger { Delay = TimeSpan.FromSeconds(1) });
                // 添加 --silent 参数，开机自启时静默运行
                td.Actions.Add(new ExecAction(strExeFilePath, "--silent"));

                // GUI 以普通用户身份运行 (asInvoker), 计划任务也应以普通用户身份运行.
                td.Principal.LogonType = TaskLogonType.InteractiveToken;
                td.Principal.RunLevel = TaskRunLevel.LUA;

                td.Settings.StopIfGoingOnBatteries = false;
                td.Settings.DisallowStartIfOnBatteries = false;
                td.Settings.ExecutionTimeLimit = TimeSpan.Zero;

                try
                {
                    TaskService.Instance.RootFolder.RegisterTaskDefinition(taskName, td);
                    Logger.WriteLine("Startup task scheduled: " + strExeFilePath);
                }
                catch (Exception e)
                {
                    Logger.WriteLine("Can't create startup task: " + e.Message);
                }
            }
        }

        /// <summary>实际删除计划任务 (调用方必须已提权).</summary>
        internal static void DoUnSchedule()
        {
            using (TaskService taskService = new TaskService())
            {
                try
                {
                    taskService.RootFolder.DeleteTask(taskName);
                    Logger.WriteLine("Startup task removed.");
                }
                catch (Exception e)
                {
                    Logger.WriteLine("Can't remove startup task: " + e.Message);
                }
            }
        }

        /// <summary>
        /// 设置 Windows 服务的启动类型. 当前进程若未提权, 则通过 UAC 提权子进程执行.
        /// </summary>
        private static void TrySetServiceAutoStart(bool auto)
        {
            try
            {
                if (ServiceManager.IsCurrentProcessElevated())
                {
                    ServiceManager.SetStartType(auto);
                }
                else
                {
                    ServiceManager.RelaunchElevated(auto ? "--service-autostart" : "--service-manual");
                }
            }
            catch (Exception e)
            {
                Logger.WriteLine("Failed to set service auto start: " + e.Message);
            }
        }
    }
}
