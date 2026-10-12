using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ClipShelfCore;

internal static class CrashReporter
{
    private const string CrashFileName = "程式閃退_把這個傳給ChatGPT.txt";
    private static string _stage = "Program entry";
    private static int _reporting;

    public static void Install()
    {
        TryDeleteOldReport();

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Report("WinForms UI thread", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception ?? new Exception("Unknown non-Exception fatal error: " + e.ExceptionObject);
            Report("AppDomain unhandled exception", ex);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Report("Unobserved task exception", e.Exception);
            e.SetObserved();
        };
    }

    public static void Stage(string stage)
    {
        if (!string.IsNullOrWhiteSpace(stage))
            _stage = stage;
    }

    public static void Report(string source, Exception ex)
    {
        if (Interlocked.Exchange(ref _reporting, 1) != 0) return;

        try
        {
            var text = BuildReport(source, ex);
            var path = TryWriteReport(text);
            TryCopyToClipboard(text);

            if (!string.IsNullOrWhiteSpace(path))
            {
                try
                {
                    Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true });
                }
                catch { }
            }

            try
            {
                MessageBox.Show(
                    "ClipShelf 發生啟動錯誤。\n\n已自動建立：\n" + CrashFileName +
                    "\n\n內容也已嘗試複製到剪貼簿。直接把該檔案傳給 ChatGPT 即可。",
                    "ClipShelf 啟動失敗",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch { }
        }
        catch { }
    }

    private static string BuildReport(string source, Exception ex)
    {
        var sb = new StringBuilder();
        sb.AppendLine("CLIPSHELF RUNTIME CRASH - 把這個檔案傳給 ChatGPT");
        sb.AppendLine("====================================================");
        sb.AppendLine("Version: v2.15.6");
        sb.AppendLine("Component: ClipShelf.Core");
        sb.AppendLine("Source: " + source);
        sb.AppendLine("Startup stage: " + _stage);
        sb.AppendLine("Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
        sb.AppendLine("Base directory: " + AppContext.BaseDirectory);
        sb.AppendLine("OS: " + Environment.OSVersion);
        sb.AppendLine("64-bit process: " + Environment.Is64BitProcess);
        sb.AppendLine(".NET: " + Environment.Version);
        sb.AppendLine();
        sb.AppendLine("---------------- EXCEPTION ----------------");
        sb.AppendLine(ex.ToString());
        sb.AppendLine("-------------- END EXCEPTION --------------");
        return sb.ToString();
    }

    private static string? TryWriteReport(string text)
    {
        string[] candidates =
        {
            Path.Combine(AppContext.BaseDirectory, CrashFileName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), CrashFileName),
            Path.Combine(Path.GetTempPath(), CrashFileName)
        };

        foreach (var path in candidates)
        {
            try
            {
                File.WriteAllText(path, text, new UTF8Encoding(true));
                if (File.Exists(path)) return path;
            }
            catch { }
        }

        return null;
    }

    private static void TryDeleteOldReport()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, CrashFileName);
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    private static void TryCopyToClipboard(string text)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo("clip.exe")
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                CreateNoWindow = true
            };
            process.Start();
            process.StandardInput.Write(text);
            process.StandardInput.Close();
            process.WaitForExit(1000);
        }
        catch { }
    }
}
