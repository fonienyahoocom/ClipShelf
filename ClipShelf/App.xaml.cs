using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;

namespace ClipShelf;

public partial class App : Application
{
    private System.Threading.Mutex? _instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _instance = new System.Threading.Mutex(true, @"Local\ClipShelf.UI.SingleInstance", out var first);
        if (!first) { Shutdown(); return; }
        EnsureCoreRunning();
        try {
            var window = new MainWindow(); MainWindow = window; window.StartUi(e.Args);
        } catch (Exception ex) {
            MessageBox.Show("ClipShelf 未啟動，原資料保持不變。\n" + ex.Message + "\n請保留 history.json 與 backups 資料夾以便恢復。", "ClipShelf 資料保護", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    // Windows remain initialized until an explicit, checked Core exit request.

    private static void EnsureCoreRunning()
    {
        try
        {
            if (Process.GetProcessesByName("ClipShelf").Any(p =>
            {
                try { return !p.HasExited; }
                finally { p.Dispose(); }
            })) return;

            var uiDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var root = Directory.GetParent(uiDir)?.FullName;
            if (string.IsNullOrWhiteSpace(root)) return;
            var core = Path.Combine(root, "ClipShelf.exe");
            if (File.Exists(core))
                Process.Start(new ProcessStartInfo(core, "--background") { WorkingDirectory = root, UseShellExecute = true });
        }
        catch { }
    }
}
