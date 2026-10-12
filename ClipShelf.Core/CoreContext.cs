using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Threading.Tasks;
using ClipShelfShared;

namespace ClipShelfCore;

internal sealed class CoreContext : ApplicationContext
{
    private readonly CoreStateStore _store;
    private readonly NativeMessageWindow _messageWindow;
    private readonly BubbleForm _bubble;
    private readonly NotifyIcon _trayIcon;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _quickLaunchItem;
    private readonly ToolStripMenuItem _manageLaunchItem;
    private readonly ToolStripMenuItem _normalModeItem;
    private readonly ToolStripMenuItem _nonInterferenceModeItem;
    private readonly Control _dispatcher;

    private readonly EventWaitHandle _toggleUiEvent;
    private readonly EventWaitHandle _quickToggleUiEvent;
    private readonly EventWaitHandle _showUiEvent;
    private readonly EventWaitHandle _showBubbleEvent;
    private readonly EventWaitHandle _uiClosedEvent;
    private readonly EventWaitHandle _openUiRequestEvent;
    private readonly EventWaitHandle _toggleCaptureEvent;
    private readonly EventWaitHandle _stateChangedEvent;
    private readonly EventWaitHandle _setNonInterferenceModeEvent;
    private readonly EventWaitHandle _setNormalModeEvent;
    private readonly EventWaitHandle _uiApplyNonInterferenceModeEvent;
    private readonly EventWaitHandle _uiApplyNormalModeEvent;
    private readonly RegisteredWaitHandle _showBubbleWait;
    private readonly RegisteredWaitHandle _uiClosedWait;
    private readonly RegisteredWaitHandle _openUiWait;
    private readonly RegisteredWaitHandle _stateChangedWait;
    private readonly RegisteredWaitHandle _setNonInterferenceModeWait;
    private readonly RegisteredWaitHandle _setNormalModeWait;

    private readonly System.Threading.SemaphoreSlim _requestLock = new(1, 1);
    private IntPtr _lastExternalWindow;
    private long _lastQuickRequest;
    private string _hotkeyStatus = "";
    private Process? _startedUiProcess;
    private bool _uiStarting;
    private bool _lastUiRunning;
    private bool _shuttingDown;
    private DateTime _lastSaveError = DateTime.MinValue;
    private bool _bubblePendingAfterUiClose;
    private readonly ToolStripMenuItem _bubbleVisibleItem;

    public CoreContext(bool background = false)
    {
        _dispatcher = new Control();
        _dispatcher.CreateControl();

        _store = new CoreStateStore();
        _messageWindow = new NativeMessageWindow();
        _messageWindow.HotkeyPressed += ToggleUiFromHotkey;
        _messageWindow.ClipboardChanged += CaptureClipboard;

        _toggleUiEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventNames.ToggleUi);
        _quickToggleUiEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventNames.QuickToggleUi);
        _showUiEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventNames.ShowUi);
        _showBubbleEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventNames.ShowBubble);
        _uiClosedEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventNames.UiClosed);
        _openUiRequestEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventNames.OpenUiRequest);
        _toggleCaptureEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventNames.ToggleCapture);
        _stateChangedEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventNames.StateChanged);
        _setNonInterferenceModeEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventNames.SetNonInterferenceModeRequest);
        _setNormalModeEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventNames.SetNormalModeRequest);
        _uiApplyNonInterferenceModeEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventNames.UiApplyNonInterferenceMode);
        _uiApplyNormalModeEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventNames.UiApplyNormalMode);

        _showBubbleWait = Register(_showBubbleEvent, () =>
        {
            _store.Reload();
            UpdateTrayUi();
            RequestBubble();
        });
        _uiClosedWait = Register(_uiClosedEvent, () =>
        {
            _store.Reload();
            UpdateTrayUi();
            TryShowPendingBubble();
        });
        _openUiWait = Register(_openUiRequestEvent, OpenUi);
        _stateChangedWait = Register(_stateChangedEvent, () =>
        {
            _store.Reload();
            UpdateTrayUi();
            ConfigureHotkey();
        });
        _setNonInterferenceModeWait = Register(_setNonInterferenceModeEvent, () => SetSwitchMode(true));
        _setNormalModeWait = Register(_setNormalModeEvent, () => SetSwitchMode(false));

        _bubble = new BubbleForm();
        _bubble.OpenRequested += OpenQuick;
        _bubble.ManageRequested += OpenUi;
        _bubble.HideRequested += () => SetBubbleEnabled(false);
        _bubble.ExitRequested += Shutdown;
        _bubble.ModeChangeRequested += SetSwitchMode;
        _bubble.PositionCommitted += (left, top) => {
            if (IsUiRunning()) _ = SendRequest(new UiRequest("bubble-placement", Left: left, Top: top));
            else { _store.Reload(); _store.SetBubblePlacement(left, top); }
        };

        var icon = LoadAppIcon();
        _trayIcon = new NotifyIcon
        {
            Icon = icon,
            Visible = true,
            Text = "ClipShelf - 輕量背景核心"
        };

        var menu = new ContextMenuStrip();
        var open = _manageLaunchItem = new ToolStripMenuItem("開啟完整管理");
        var quick = _quickLaunchItem = new ToolStripMenuItem("開啟快捷選取");
        quick.Click += (_, _) => OpenQuick();
        open.Click += (_, _) => OpenUi();

        _pauseItem = new ToolStripMenuItem();
        _pauseItem.Click += (_, _) => TogglePause();

        var bubble = _bubbleVisibleItem = new ToolStripMenuItem("顯示桌面浮動圖示");
        bubble.Click += (_, _) => SetBubbleEnabled(!_store.FloatingIconEnabled);

        var hide = new ToolStripMenuItem("真正隱藏（Ctrl+~）");
        hide.Click += (_, _) => TrueHide();

        var modeMenu = new ToolStripMenuItem("視窗切換模式");
        _normalModeItem = new ToolStripMenuItem("一般模式（工作列 / Alt+Tab）");
        _normalModeItem.Click += (_, _) => SetSwitchMode(false);
        _nonInterferenceModeItem = new ToolStripMenuItem("不干擾模式（浮動圖示）");
        _nonInterferenceModeItem.Click += (_, _) => SetSwitchMode(true);
        modeMenu.DropDownItems.Add(_normalModeItem);
        modeMenu.DropDownItems.Add(_nonInterferenceModeItem);

        var storage = new ToolStripMenuItem("打開檔案存放位置");
        storage.Click += (_, _) => OpenStorageDirectory();

        var exit = new ToolStripMenuItem("離開 ClipShelf");
        exit.Click += (_, _) => Shutdown();

        menu.Items.Add(quick);
        menu.Items.Add(open);
        menu.Items.Add(_pauseItem);
        menu.Items.Add(bubble);
        menu.Items.Add(hide);
        menu.Items.Add(modeMenu);
        menu.Items.Add(storage);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exit);
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) OpenQuick();
        };
        

        UpdateTrayUi();
        ConfigureHotkey();
        if (!background) Post(() => RequestUi("preferred"));
        else Post(() => RequestUi("resident"));
    }

    private RegisteredWaitHandle Register(EventWaitHandle handle, Action action)
        => ThreadPool.RegisterWaitForSingleObject(handle, (_, _) => Post(action), null, Timeout.Infinite, false);

    private void Post(Action action)
    {
        if (_shuttingDown || _dispatcher.IsDisposed) return;
        try { _dispatcher.BeginInvoke(action); } catch { }
    }

    private void SetSwitchMode(bool nonInterference)
    {
        if (_shuttingDown) return;

        // If the full UI is alive, let it persist the setting together with its current
        // in-memory history/folder state.  This avoids the lightweight Core overwriting
        // a newer UI snapshot just to change one boolean setting.
        if (IsUiRunning())
        {
            _normalModeItem.Checked = !nonInterference;
            _nonInterferenceModeItem.Checked = nonInterference;
            _bubble.SetNonInterferenceMode(nonInterference);
        _bubbleVisibleItem.Checked = _store.FloatingIconEnabled;
        ApplyBubbleVisibility();
            if (nonInterference) _uiApplyNonInterferenceModeEvent.Set();
            else _uiApplyNormalModeEvent.Set();
            return;
        }

        _store.Reload();
        _store.SetSwitchNonInterferenceMode(nonInterference);
        UpdateTrayUi();

        if (nonInterference)
        {
            ShowBubble();
        }
        else
        {
            ApplyBubbleVisibility();
            OpenUi();
        }
    }

    private void ToggleUiFromHotkey()
    {
        var now = Stopwatch.GetTimestamp();
        if ((now - _lastQuickRequest) * 1000d / Stopwatch.Frequency < 120) return;
        _lastQuickRequest = now;
        RequestUi("toggle-preferred");
    }
    private void OpenQuick() => RequestUi("quick");
    private void OpenUi() => RequestUi("manage");
    private void ConfigureHotkey()
    {
        var (modifiers, key) = _store.PickerHotkey;
        var success = _messageWindow.ConfigureHotkey(modifiers, key);
        var label = key == 0xC0 ? "Ctrl+~" : key == 0x56 ? "Ctrl+Alt+V" : "Ctrl+Alt+Q";
        var status = success ? label + " 已註冊" : label + " 已被其他程式佔用。請從系統匣開啟完整管理 → 設定，選擇替代快捷鍵。";
        _hotkeyStatus = status;
        if (!success) _trayIcon.ShowBalloonTip(10000, "ClipShelf 快捷鍵衝突", status, ToolTipIcon.Warning);
        if (IsUiRunning()) _ = SendRequest(new UiRequest("hotkey-status", Message: status));
    }
    private void RequestUi(string command)
    {
        if (_shuttingDown) return;
        var started = Stopwatch.GetTimestamp();
        var target = GetForegroundWindow();
        GetWindowThreadProcessId(target, out var pid);
        bool external = target != IntPtr.Zero && pid != Environment.ProcessId;
        if (external) {
            try { using var p = Process.GetProcessById((int)pid); external = p.ProcessName != "ClipShelf.UI" && p.ProcessName != "explorer"; }
            catch { external = false; }
        }
        if (external) _lastExternalWindow = target;
        else target = _lastExternalWindow;
        GetWindowThreadProcessId(target, out pid);
        var request = new UiRequest(command, target.ToInt64(), (int)pid, started);
        _bubblePendingAfterUiClose = false;
        ApplyBubbleVisibility(); // Independent preference; never tied to either window.
        if (IsUiRunning()) { _ = SendRequest(request); return; }
        if (_uiStarting) { if (command != "resident") _ = SendRequest(request); return; }
        var path = Path.Combine(AppContext.BaseDirectory, "ui", "ClipShelf.UI.exe");
        if (!File.Exists(path)) { MessageBox.Show("找不到完整介面，請重新安裝 ClipShelf。", "ClipShelf"); return; }
        _uiStarting = true;
        try {
            // Cold startup is hidden. The queued request is acknowledged exactly once.
            var process = Process.Start(new ProcessStartInfo(path, "--resident") { WorkingDirectory = Path.GetDirectoryName(path)!, UseShellExecute = false });
            if (process == null) { _uiStarting = false; return; }
            _startedUiProcess = process; AllowSetForegroundWindow(process.Id);
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => Post(() => {
                _uiStarting = false; _lastUiRunning = false;
                if (ReferenceEquals(_startedUiProcess, process)) _startedUiProcess = null;
                process.Dispose(); _store.Reload(); UpdateTrayUi();
            });
            if (command != "resident") _ = SendRequest(request);
        } catch (Exception ex) { _uiStarting = false; MessageBox.Show("無法啟動介面：" + ex.Message, "ClipShelf"); }
    }
    private async Task SendRequest(UiRequest request)
    {
        await _requestLock.WaitAsync();
        try {
            if (_shuttingDown) return;
            if (_startedUiProcess is { HasExited: false } p) AllowSetForegroundWindow(p.Id);
            else foreach (var process in Process.GetProcessesByName("ClipShelf.UI")) {
                using (process) { try { AllowSetForegroundWindow(process.Id); } catch { } }
            }
            await UiChannel.SendAsync(request);
            if (request.Command is "quick" or "toggle-quick" or "manage" or "toggle-preferred" or "preferred") await UiChannel.SendAsync(new UiRequest("hotkey-status", Message: _hotkeyStatus));
            _uiStarting = false;
        } catch (Exception ex) {
            Post(() => _trayIcon.ShowBalloonTip(8000, "ClipShelf 介面沒有回應", "請從系統匣再試一次；資料未被重設。 " + ex.Message, ToolTipIcon.Warning));
        } finally { _requestLock.Release(); }
    }

    private bool IsUiRunning()
    {
        try
        {
            if (_startedUiProcess is { HasExited: false })
            {
                _lastUiRunning = true;
                return true;
            }

            var running = Process.GetProcessesByName("ClipShelf.UI").Any(p =>
            {
                try { return !p.HasExited; }
                finally { p.Dispose(); }
            });

            if (_lastUiRunning && !running)
                _store.Reload();
            _lastUiRunning = running;
            if (!running) _uiStarting = false;
            return running;
        }
        catch
        {
            return false;
        }
    }

    private void ShowBubble()
    {
        RequestBubble();
    }

    private void RequestBubble()
    {
        if (_shuttingDown) return;

        // WPF remains resident indefinitely; the bubble can coexist with the
        // hidden UI and opens its independent quick picker.
        _bubblePendingAfterUiClose = false;
        ShowBubbleNow();
    }

    private void TryShowPendingBubble()
    {
        if (_shuttingDown || !_bubblePendingAfterUiClose) return;
        if (IsUiRunning() || _uiStarting) return;
        _bubblePendingAfterUiClose = false;
        ShowBubbleNow();
    }

    private void SetBubbleEnabled(bool enabled)
    {
        if (_shuttingDown) return;
        if (IsUiRunning()) { _ = SendRequest(new UiRequest("bubble-visible", Message: enabled ? "true" : "false")); return; }
        _store.Reload(); _store.SetFloatingIconEnabled(enabled); UpdateTrayUi();
    }
    private void ApplyBubbleVisibility()
    {
        if (_shuttingDown) return;
        if (_store.FloatingIconEnabled) ShowBubbleNow(); else _bubble.Hide();
    }
    private void ShowBubbleNow()
    {
        if (_shuttingDown || !_store.FloatingIconEnabled || _bubble.Visible) return;
        var placement = _store.GetBubblePlacement();
        _bubble.ShowAtSavedOrDefault(placement.Has, placement.Left, placement.Top);
    }

    private void TrueHide()
    {
        _bubblePendingAfterUiClose = false;
        SetBubbleEnabled(false);
        if (IsUiRunning()) _ = SendRequest(new UiRequest("hide"));
    }

    private void TogglePause()
    {
        if (IsUiRunning())
        {
            _toggleCaptureEvent.Set();
            return;
        }

        _store.SetCapturePaused(!_store.CapturePaused);
        UpdateTrayUi();
    }

    private void UpdateTrayUi()
    {
        var paused = _store.CapturePaused;
        var nonInterference = _store.SwitchNonInterferenceMode;
        _trayIcon.Text = paused
            ? "ClipShelf - 背景核心（自動保存已暫停）"
            : nonInterference
                ? "ClipShelf - 背景核心（不干擾模式）"
                : "ClipShelf - 背景核心（一般模式）";
        _pauseItem.Text = paused ? "繼續自動保存" : "暫停自動保存";
        var preferQuick = _store.PreferQuickPicker;
        _quickLaunchItem.Text = preferQuick ? "開啟快捷選取  ✓" : "開啟快捷選取";
        _manageLaunchItem.Text = preferQuick ? "開啟完整管理" : "開啟完整管理  ✓";
        _quickLaunchItem.AccessibleDescription = preferQuick ? "目前 Ctrl+~ 預設快捷選取" : "選用快捷選取作為預設";
        _manageLaunchItem.AccessibleDescription = preferQuick ? "選用完整管理作為預設" : "目前 Ctrl+~ 預設完整管理";
        _bubble.SetPreferredLaunchMode(preferQuick);
        _normalModeItem.Checked = !nonInterference;
        _nonInterferenceModeItem.Checked = nonInterference;
        _bubble.SetNonInterferenceMode(nonInterference);
        _bubbleVisibleItem.Checked = _store.FloatingIconEnabled;
        ApplyBubbleVisibility();
    }

    private readonly System.Threading.Timer _backupTimer = new(_ => { try { ClipShelfShared.AutoBackup.Run(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipShelf")); } catch { } }, null, 15000, 60000);

    private void CaptureClipboard()
    {
        var foreground = GetForegroundWindow(); GetWindowThreadProcessId(foreground, out var sourcePid);
        try { using var process = Process.GetProcessById((int)sourcePid); if (process.ProcessName != "ClipShelf.UI" && sourcePid != Environment.ProcessId) _lastExternalWindow = foreground; } catch { }
        if (_shuttingDown || _uiStarting || IsUiRunning()) return;
        try
        {
            if (_store.CapturePaused) return;
            var source = GetSourceInfo();

            if (Clipboard.ContainsFileDropList())
            {
                if (!_store.CaptureFiles) return;
                StringCollection drop = Clipboard.GetFileDropList();
                var files = drop.Cast<string>().ToList();
                if (files.Count > 0) _store.AddOrTouchFiles(files, source.App, source.Title);
                return;
            }

            if (Clipboard.ContainsImage())
            {
                if (!_store.CaptureImages) return;
                using var image = Clipboard.GetImage();
                if (image == null) return;

                using var ms = new MemoryStream();
                image.Save(ms, ImageFormat.Png);
                var bytes = ms.ToArray();
                var hash = CoreStateStore.Sha256(bytes);
                if (_store.HasItem("Image", hash))
                {
                    _store.TouchExisting("Image", hash, source.App, source.Title);
                    return;
                }

                var path = Path.Combine(
                    _store.GetImageStorageDirectory(),
                    $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.png");
                File.WriteAllBytes(path, bytes);
                _store.AddOrTouchImage(path, hash, source.App, source.Title);
                return;
            }

            if (_store.CaptureText && Clipboard.ContainsText(TextDataFormat.UnicodeText))
            {
                var text = Clipboard.GetText(TextDataFormat.UnicodeText);
                if (!string.IsNullOrEmpty(text))
                    _store.AddOrTouchText(text, source.App, source.Title);
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Text.Json.JsonException)
        {
            if (DateTime.Now - _lastSaveError > TimeSpan.FromMinutes(1))
            {
                _lastSaveError = DateTime.Now;
                _trayIcon.ShowBalloonTip(10000, "ClipShelf 儲存失敗", "這次擷取可能尚未儲存，請檢查磁碟空間與資料夾權限。\n" + ex.Message, ToolTipIcon.Error);
            }
        }
        catch
        {
            // Another process can hold the clipboard briefly. The next real clipboard
            // update will be handled normally; never poll in the background.
        }
    }

    private (string App, string Title) GetSourceInfo()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return ("", "");
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == (uint)Environment.ProcessId) return ("", "");
            using var process = Process.GetProcessById((int)pid);
            var len = GetWindowTextLength(hwnd);
            var sb = new StringBuilder(Math.Max(1, len + 1));
            GetWindowText(hwnd, sb, sb.Capacity);
            return (process.ProcessName, sb.ToString());
        }
        catch { return ("", ""); }
    }

    private void OpenStorageDirectory()
    {
        try
        {
            var path = _store.GetStorageRoot();
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch { }
    }

    private Icon LoadAppIcon()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(path))
                return Icon.ExtractAssociatedIcon(path) ?? SystemIcons.Application;
        }
        catch { }
        return SystemIcons.Application;
    }

    private void Shutdown()
    {
        if (_shuttingDown) return;
        _shuttingDown = true;
        _bubblePendingAfterUiClose = false;
        _bubble.Hide();

        // "Exit ClipShelf" is different from hide/minimize:
        // it must leave no ClipShelf.UI process behind.  Ask the WPF UI to
        // close cleanly first so it can flush history/settings. If the UI cannot
        // save or still has a draft prompt, cancel exit instead of killing it.
        if (!StopUiProcessesForExit()) {
            _shuttingDown = false;
            MessageBox.Show("介面尚未關閉；請先處理保存提示，再從系統匣退出。", "ClipShelf", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        _backupTimer.Dispose();
        ExitThread();
    }

    private bool StopUiProcessesForExit()
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName("ClipShelf.UI")
                .Where(p =>
                {
                    try { return !p.HasExited; }
                    catch { return false; }
                })
                .ToArray();
        }
        catch
        {
            processes = Array.Empty<Process>();
        }

        if (processes.Length == 0) return true;

        // Normal path: the UI receives this event, saves its state and closes
        // MainWindow.  App.xaml uses OnMainWindowClose, so the process really ends.
        try { _toggleUiEvent.Set(); } catch { }

        foreach (var process in processes)
        {
            try
            {
                if (process.WaitForExit(3000)) continue;

                // Second graceful attempt for an unusual missed bridge signal.
                try { process.CloseMainWindow(); } catch { }
                if (process.WaitForExit(1200)) continue;

                return false;
            }
            catch { }
            finally
            {
                try { process.Dispose(); } catch { }
            }
        }
        return true;
    }

    protected override void ExitThreadCore()
    {
        _shuttingDown = true;
        try { _trayIcon.Visible = false; } catch { }
        try { _trayIcon.Dispose(); } catch { }
        try { _bubble.Dispose(); } catch { }
        try { _messageWindow.Dispose(); } catch { }
        try { _showBubbleWait.Unregister(null); } catch { }
        try { _uiClosedWait.Unregister(null); } catch { }
        try { _openUiWait.Unregister(null); } catch { }
        try { _stateChangedWait.Unregister(null); } catch { }
        try { _setNonInterferenceModeWait.Unregister(null); } catch { }
        try { _setNormalModeWait.Unregister(null); } catch { }
        try { _toggleUiEvent.Dispose(); } catch { }
        try { _quickToggleUiEvent.Dispose(); } catch { }
        try { _showUiEvent.Dispose(); } catch { }
        try { _showBubbleEvent.Dispose(); } catch { }
        try { _uiClosedEvent.Dispose(); } catch { }
        try { _openUiRequestEvent.Dispose(); } catch { }
        try { _toggleCaptureEvent.Dispose(); } catch { }
        try { _stateChangedEvent.Dispose(); } catch { }
        try { _setNonInterferenceModeEvent.Dispose(); } catch { }
        try { _setNormalModeEvent.Dispose(); } catch { }
        try { _uiApplyNonInterferenceModeEvent.Dispose(); } catch { }
        try { _uiApplyNormalModeEvent.Dispose(); } catch { }
        try { _dispatcher.Dispose(); } catch { }
        try { _startedUiProcess?.Dispose(); } catch { }
        _startedUiProcess = null;
        base.ExitThreadCore();
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowTextLength(IntPtr hWnd);
}
