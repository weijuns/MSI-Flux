using System;
using System.Management;

Console.WriteLine("=== WMI 事件类检查 ===\n");
string[] classes = { "MSI_Event", "WMIEvent", "MSIEvent" };
foreach (var cls in classes)
{
    try
    {
        using var s = new ManagementObjectSearcher(@"root\wmi", $"SELECT * FROM {cls}");
        int n = 0; foreach (var _ in s.Get()) n++;
        Console.WriteLine($"  {cls}: {n} 实例");
    }
    catch (Exception ex) { Console.WriteLine($"  {cls}: {ex.GetType().Name}: {ex.Message}"); }
}
