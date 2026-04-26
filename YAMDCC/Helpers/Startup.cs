using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32.TaskScheduler;
using Microsoft.Win32.TaskScheduler.V1Interop;
using Microsoft.Win32.TaskScheduler.V2Interop;

namespace YAMDCC.GUI.Helpers
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
                    return (taskService.RootFolder.AllTasks.Any(t => t.Name == taskName));
            }
            catch (Exception e)
            {
                Logger.WriteLine("Can't check startup task status: " + e.Message);
                return false;
            }
        }

        public static void Schedule()
        {
            using (TaskDefinition td = TaskService.Instance.NewTask())
            {
                td.RegistrationInfo.Description = "YAMDCC Auto Start";
                td.Triggers.Add(new LogonTrigger { Delay = TimeSpan.FromSeconds(1) });
                // 添加 --silent 参数，开机自启时静默运行
                td.Actions.Add(new ExecAction(strExeFilePath, "--silent"));

                td.Principal.LogonType = TaskLogonType.InteractiveToken;
                if (ProcessHelper.IsUserAdministrator())
                    td.Principal.RunLevel = TaskRunLevel.Highest;

                td.Settings.StopIfGoingOnBatteries = false;
                td.Settings.DisallowStartIfOnBatteries = false;
                td.Settings.ExecutionTimeLimit = TimeSpan.Zero;

                try
                {
                    TaskService.Instance.RootFolder.RegisterTaskDefinition(taskName, td);
                }
                catch (Exception e)
                {
                    if (ProcessHelper.IsUserAdministrator())
                        MessageBox.Show("Can't create a start up task. Try running Task Scheduler by hand and manually deleting YAMDCC task if it exists there.", "Scheduler Error", MessageBoxButtons.OK);
                    else
                        ProcessHelper.RunAsAdmin();
                }

                Logger.WriteLine("Startup task scheduled: " + strExeFilePath);
            }
        }

        public static void UnSchedule()
        {
            using (TaskService taskService = new TaskService())
            {
                try
                {
                    taskService.RootFolder.DeleteTask(taskName);
                }
                catch (Exception e)
                {
                    if (ProcessHelper.IsUserAdministrator())
                        MessageBox.Show("Can't remove task. Try running Task Scheduler by hand and manually deleting YAMDCC task if it exists there.", "Scheduler Error", MessageBoxButtons.OK);
                    else
                        ProcessHelper.RunAsAdmin();
                }
            }
        }
    }
}
