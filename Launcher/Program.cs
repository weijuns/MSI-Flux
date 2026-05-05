using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

// Minimal launcher: checks for .NET 8 Desktop Runtime, extracts and runs the main app.

static class Launcher
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    private const uint MB_OK = 0x00;
    private const uint MB_ICONINFORMATION = 0x40;

    [STAThread]
    static void Main()
    {
        if (!HasDotNet8DesktopRuntime())
        {
            MessageBoxW(IntPtr.Zero,
                ".NET 8 Desktop Runtime is required to run MSI Flux.\n\n" +
                "Click OK to open the download page.\n" +
                "Install the \"Windows x64\" Desktop Runtime, then try again.",
                "MSI Flux - .NET Runtime Required",
                MB_OK | MB_ICONINFORMATION);
            try
            {
                Process.Start(new ProcessStartInfo("https://dotnet.microsoft.com/download/dotnet/8.0")
                {
                    UseShellExecute = true
                });
            }
            catch { }
            return;
        }

        ExtractAndRun();
    }

    private static bool HasDotNet8DesktopRuntime()
    {
        try
        {
            string dotnetRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet");
            string sharedDir = Path.Combine(dotnetRoot, "shared", "Microsoft.WindowsDesktop.App");
            if (!Directory.Exists(sharedDir)) return false;

            foreach (string dir in Directory.GetDirectories(sharedDir))
            {
                string name = Path.GetFileName(dir);
                if (name.StartsWith("8.") && Directory.GetFiles(dir, "*.dll").Length > 0)
                    return true;
            }
        }
        catch { }
        return false;
    }

    private static void ExtractAndRun()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "MSI_Flux_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);

        string exePath = Path.Combine(tempDir, "MSI Flux.exe");

        try
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MSI_Flux.exe"))
            {
                if (stream is null)
                {
                    MessageBoxW(IntPtr.Zero, "Internal error: embedded resource not found.", "MSI Flux", MB_OK);
                    return;
                }
                using var fs = File.Create(exePath);
                stream.CopyTo(fs);
            }

            var psi = new ProcessStartInfo(exePath)
            {
                UseShellExecute = true,
                WorkingDirectory = tempDir
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            MessageBoxW(IntPtr.Zero, $"Failed to launch MSI Flux:\n{ex.Message}", "MSI Flux", MB_OK);
        }
    }
}
