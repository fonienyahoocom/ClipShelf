using System;
using System.Threading;
using System.Windows.Forms;

namespace ClipShelfCore;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        CrashReporter.Install();

        try
        {
            Run(args);
        }
        catch (Exception ex)
        {
            CrashReporter.Report("Top-level startup", ex);
        }
    }

    private static void Run(string[] args)
    {
        CrashReporter.Stage("Parse startup arguments");
        var setNonInterference = Array.Exists(args, a => string.Equals(a, "--noninterference", StringComparison.OrdinalIgnoreCase));
        var setNormal = Array.Exists(args, a => string.Equals(a, "--normal-mode", StringComparison.OrdinalIgnoreCase));

        CrashReporter.Stage("Acquire single-instance mutex");
        using var mutex = new Mutex(true, @"Local\ClipShelf.Core.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            CrashReporter.Stage("Forward request to existing Core");
            try
            {
                var eventName = setNonInterference
                    ? EventNames.SetNonInterferenceModeRequest
                    : setNormal
                        ? EventNames.SetNormalModeRequest
                        : EventNames.OpenUiRequest;
                using var request = new EventWaitHandle(false, EventResetMode.AutoReset, eventName);
                request.Set();
            }
            catch (Exception ex)
            {
                CrashReporter.Report("Could not contact existing Core", ex);
            }
            return;
        }

        if (setNonInterference || setNormal)
        {
            CrashReporter.Stage("Persist requested window mode");
            try
            {
                var store = new CoreStateStore();
                store.SetSwitchNonInterferenceMode(setNonInterference);
            }
            catch (Exception ex)
            {
                CrashReporter.Report("Persist startup mode", ex);
                return;
            }
        }

        var dataDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipShelf");
        var history = System.IO.Path.Combine(dataDir, "history.json");
        try { ClipShelfShared.SafeState.Read(history); }
        catch (Exception ex) {
            var previous = history + ".previous";
            bool canRestore = false;
            try { if (System.IO.File.Exists(previous)) { ClipShelfShared.SafeState.Read(previous); canRestore = true; } } catch { }
            if (!canRestore) { MessageBox.Show("資料讀取失敗，已停止啟動並保留原檔。\n" + ex.Message + "\n\n請先保留資料夾，再從備份恢復或提供錯誤訊息協助處理。\n資料位置：" + dataDir, "ClipShelf 無法讀取資料", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            if (MessageBox.Show("資料讀取失敗，原檔不會被覆蓋。\n" + ex.Message + "\n\n是否恢復上一份有效資料？損壞原檔也會另外保留。", "ClipShelf 資料恢復", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            System.IO.File.Copy(history, history + ".unreadable-" + DateTime.Now.ToString("yyyyMMddHHmmss"), false);
            System.IO.File.Copy(previous, history, true);
        }
        CrashReporter.Stage("Initialize WinForms");
        ApplicationConfiguration.Initialize();

        CrashReporter.Stage("Construct CoreContext");
        using var context = new CoreContext(Array.Exists(args, a => a == "--background"));

        CrashReporter.Stage("Enter WinForms message loop");
        Application.Run(context);
    }
}
