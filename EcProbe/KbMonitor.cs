// 键盘自动监测器
//
// 用法:
//   KbMonitor.exe <durationSeconds> [outputFile]
//
// 它会每隔 2 秒采样一次键盘状态，把结果写入日志。
// 你只需要在这段时间内正常使用笔记本键盘。
//
// 判定逻辑:
//   累计采样到 0 个按键 = 键盘失效（这条日志会明确写出来）

using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

internal static class KbMonitor
{
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        int seconds = args.Length > 0 ? int.Parse(args[0]) : 60;
        string logPath = args.Length > 1
            ? args[1]
            : Path.Combine(Path.GetTempPath(), "kb-monitor.log");

        using var w = new StreamWriter(logPath, append: false, Encoding.UTF8) { AutoFlush = true };

        void W(string s)
        {
            Console.WriteLine(s);
            w.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {s}");
        }

        W($"=== 键盘监测开始，持续 {seconds} 秒 ===");
        W("请正常使用笔记本键盘（打字、按方向键等）");

        var allKeys = new HashSet<int>();
        long totalSamples = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int nextTick = 2;

        while (sw.Elapsed.TotalSeconds < seconds)
        {
            int before = (int)totalSamples;

            // 采样 2 秒
            var tickEnd = DateTime.Now.AddSeconds(2);
            while (DateTime.Now < tickEnd && sw.Elapsed.TotalSeconds < seconds)
            {
                for (int vk = 8; vk <= 254; vk++)
                {
                    if ((GetAsyncKeyState(vk) & 0x8000) != 0)
                    {
                        totalSamples++;
                        allKeys.Add(vk);
                    }
                }
                Thread.Sleep(8);
            }

            int delta = (int)(totalSamples - before);
            var names = new StringBuilder();
            foreach (var k in allKeys) names.Append("0x").Append(k.ToString("X2")).Append(' ');

            if (delta == 0)
                W($"[{nextTick,3}s] 本段无按键  | 累计 {totalSamples} | 已捕获 VK: {names}");
            else
                W($"[{nextTick,3}s] 本段 {delta} 个样本 | 累计 {totalSamples} | 已捕获 VK: {names}");

            nextTick += 2;
        }

        W("=== 结果 ===");
        if (totalSamples == 0)
        {
            W("❌ 全程 0 个按键样本 —— 键盘失效");
            Console.WriteLine();
            Console.WriteLine("请把这段日志发给我。");
            return 2;
        }

        W($"✅ 键盘正常，共 {totalSamples} 个按键样本，涉及 {allKeys.Count} 个不同按键");
        return 0;
    }
}
