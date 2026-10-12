using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ClipShelf;

public partial class MainWindow : Window
{
    private const int WM_HOTKEY = 0x0312;
    private const int WM_CLIPBOARDUPDATE = 0x031D;
    private const int WM_SYSCOMMAND = 0x0112;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int VK_TAB = 0x09;
    private const int VK_MENU = 0x12;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int SC_MINIMIZE = 0xF020;
    private const int SC_CLIPSHELF_NONINTERFERENCE = 0x1F10;
    private const uint MF_STRING = 0x00000000;
    private const uint MF_SEPARATOR = 0x00000800;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_APPWINDOW = 0x00040000;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const int HOTKEY_ID = 0x4353;
    private const uint MOD_CONTROL = 0x0002;
    private const uint VK_OEM_3 = 0xC0;

    private readonly ObservableCollection<ClipItem> _allItems = new();
    private readonly ObservableCollection<ClipItem> _visibleItems = new();
    private readonly ObservableCollection<SidebarItem> _categories = new();
    private readonly List<string> _folderPaths = new();
    private readonly HistoryStore _store = new();
    private readonly CoreBridge _coreBridge;
    private AppSettings _settings = new();
    private bool _capturePaused;
    private MiniWindow? _miniWindow;
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private System.Windows.Forms.ToolStripMenuItem? _trayPauseItem;
    private bool _shortcutHidden;
    private HwndSource? _source;
    private IntPtr _hwnd;
    private IntPtr _previousForegroundWindow = IntPtr.Zero;
    private DateTime _ignoreClipboardUntil = DateTime.MinValue;
    private bool _allowClose;
    private int _suspendAutoHide;
    private Point _categoryDragStart;
    private SidebarItem? _dragCategoryItem;
    private DateTime _categoryMouseDownAt;
    private string _folderHoverKey = "";
    private DateTime _folderHoverSince = DateTime.MinValue;
    private FolderDropMode _folderDropMode = FolderDropMode.None;
    private Point _itemDragStart;
    private ClipItem? _dragClipItem;
    private const int BuiltInCategoryCount = 5;
    // Folder dragging should feel deliberate, but sibling reordering must still be quick.
    // We only require a longer dwell when nesting one folder inside another.
    private const int FolderDragHoldMs = 120;
    private const int FolderNestDwellMs = 420;
    private const string CategoryDragFormat = "ClipShelf.CustomCategory";
    private const string ClipDragFormat = "ClipShelf.ClipItemId";
    private ListBoxItem? _dropIndicatorContainer;
    private ListBoxItem? _clipDropTargetContainer;
    private bool _handlingMinimize;
    private bool _multiSelectMode;
    private ClipItem? _multiSelectAnchor;
    private bool _rebuildingSidebar;
    private DateTime _ignoreDeactivationUntil = DateTime.UtcNow.AddMilliseconds(1200);
    private bool _hasActivatedOnce;
    private DateTime _keyboardSwitcherUntil = DateTime.MinValue;
    // A destructive action can rebuild/recycle list rows while Windows is still inside
    // the double-click time window.  Suppress card double-click paste briefly so rapid
    // trash clicks can never turn into an accidental paste + panel close.
    private DateTime _suppressHistoryDoubleClickUntil = DateTime.MinValue;

    private ClipShelfShared.UiServer? _uiServer;
    private QuickPickerWindow? _picker;
    private uint _lastInternalClipboardSequence;
    private bool _categoryMouseCaptured;

    private const int PermanentDeleteGraceSeconds = 8;
    private DispatcherTimer? _pendingPurgeTimer;
    private DispatcherTimer? _undoBarTimer;
    private Action? _undoAction;
    // Normal mode behaves like a regular taskbar/Alt+Tab window.  This special mode
    // is entered only from the Windows taskbar system menu and deliberately removes
    // ClipShelf from the normal switcher until the UI process exits or the user restores it.
    private bool _switchNonInterferenceMode;
    private string _contentKindFilter = "All";

    public MainWindow()
    {
        InitializeComponent();
        TrackInlineComposition();
        _previousForegroundWindow = ParseReturnWindowFromArgs();
        HistoryList.ItemsSource = _visibleItems;
        CategoryList.ItemsSource = _categories;

        var loaded = _store.Load();
        _settings = loaded.Settings ?? new AppSettings();
        _capturePaused = _settings.CapturePaused;
        _switchNonInterferenceMode = _settings.SwitchNonInterferenceMode;

        // v2.5 migration: preserve the exact v2.4 visible History on first upgrade.
        // Legacy History showed items with no classification category. After migration
        // History becomes an independent recent-use surface, so future folder copies can
        // appear there without changing/removing the source item.
        if (_settings.HistoryModelVersion < 1)
        {
            foreach (var item in loaded.Items)
                item.HistoryVisible = string.IsNullOrWhiteSpace(item.Category);
            _settings.HistoryModelVersion = 1;
        }

        ApplySavedWindowLayout();
        foreach (var item in loaded.Items.OrderByDescending(x => x.LastUsedAt)) _allItems.Add(item);
        FinalizeExpiredPendingPurges(queueSave: false);
        PurgeExpiredTrash(queueSave: false);
        SchedulePendingPurgeTimer();

        _categories.Add(SidebarItem.System("History", "🕘", "歷史"));
        _categories.Add(SidebarItem.System("Favorite", "⭐", "收藏"));
        _categories.Add(SidebarItem.System("Temporary", "⏱", "暫存"));
        _categories.Add(SidebarItem.System("AI", "🤖", "AI 用語"));
        _categories.Add(SidebarItem.System("Quick", "⚡", "快速用語"));
        _categories.Add(SidebarItem.System("Trash", "🗑", "最近刪除"));
        foreach (var category in loaded.Categories.Where(c => !string.IsNullOrWhiteSpace(c)))
        {
            var path = SidebarItem.NormalizePath(category);
            if (!string.IsNullOrWhiteSpace(path) && !_folderPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
                _folderPaths.Add(path);
        }
        // Older builds sometimes stored the classification on the clip but did not
        // persist the corresponding folder key. Reconstruct those folder keys so old
        // folders (for example prompt / 占星技巧) get the same drag handle as new ones.
        RecoverFolderPathsFromItems();
        NormalizeFolderOrder();
        RebuildFolderSidebar();

        CategoryList.SelectedIndex = 0;
        _coreBridge = new CoreBridge(
            () => Dispatcher.BeginInvoke(new Action(ShutdownUi)),
            () => Dispatcher.BeginInvoke(new Action(ToggleFastHotkey)),
            () => Dispatcher.BeginInvoke(new Action(ActivateFromCore)),
            () => Dispatcher.BeginInvoke(new Action(ToggleCapturePause)),
            () => Dispatcher.BeginInvoke(new Action(EnableSwitchNonInterferenceMode)),
            () => Dispatcher.BeginInvoke(new Action(RestoreNormalSwitchMode)));

        SourceInitialized += MainWindow_SourceInitialized;
        Activated += (_, _) =>
        {
            // WPF can rewrite extended styles across show/hide/state transitions.
            // Re-assert palette-window semantics every time the UI becomes active.
            ApplyTaskSwitcherMode();
            if (!_hasActivatedOnce)
            {
                _hasActivatedOnce = true;
                _ignoreDeactivationUntil = DateTime.UtcNow.AddMilliseconds(450);
            }
            if (_inlineEditItem is ClipItem editing)
            {
                if (!HistoryList.IsKeyboardFocusWithin) FocusInlineEditor(editing, editing.IsEditingTitle);
            }
            else SearchBox.Focus();
        };
        Deactivated += MainWindow_Deactivated;
        SizeChanged += (_, _) => QueueSave();
        LocationChanged += (_, _) => QueueSave();
        _allItems.CollectionChanged += AllItems_CollectionChanged;
        TrimHistory();
        RefreshVisibleItems();
        UpdateCaptureUi();
        UpdateContentFilterUi();
        SyncViewModeControls();
        UpdateModeStatusUi();
        InitializeProductFeatures();
        QueueSave();
        _uiServer = new ClipShelfShared.UiServer(request => Dispatcher.InvokeAsync(() => HandleUiRequest(request)).Task);
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);
        ApplyTaskSwitcherMode();
        InstallTaskbarSystemMenu();
        InstallTaskbarJumpList();
        AddClipboardFormatListener(_hwnd);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN) && wParam.ToInt32() == VK_TAB && IsWindowSwitchChordDown())
            _keyboardSwitcherUntil = DateTime.UtcNow.AddMilliseconds(950);

        if (msg == WM_SYSCOMMAND && wParam.ToInt32() == SC_CLIPSHELF_NONINTERFERENCE)
        {
            // Explicit opt-in from the taskbar right-click menu. Normal mode remains a
            // regular taskbar/Alt+Tab window; only this command enters non-interference mode.
            handled = true;
            Dispatcher.BeginInvoke(new Action(EnableSwitchNonInterferenceMode));
        }
        else if (msg == WM_SYSCOMMAND && ((wParam.ToInt32() & 0xFFF0) == SC_MINIMIZE) && _switchNonInterferenceMode)
        {
            handled = true;
            Dispatcher.BeginInvoke(new Action(MinimizeToBubble));
        }
        else if (msg == WM_CLIPBOARDUPDATE)
        {
            if (PickerClipboard.GetClipboardSequenceNumber() != _lastInternalClipboardSequence && DateTime.UtcNow >= _ignoreClipboardUntil)
                Dispatcher.BeginInvoke(CaptureClipboard);
        }
        return IntPtr.Zero;
    }


    private async void MainWindow_Deactivated(object? sender, EventArgs e)
    {
        // Default mode behaves like a normal Windows app: keyboard switching keeps
        // the full window alive; clicking another app minimizes ClipShelf to taskbar.
        // Only the explicit taskbar command enters bubble/non-interference behavior.
        if (DateTime.UtcNow < _ignoreDeactivationUntil) return;

        var keyboardSwitch = DateTime.UtcNow < _keyboardSwitcherUntil || IsWindowSwitchChordDown();
        await Task.Delay(110);
        if (DateTime.UtcNow < _ignoreDeactivationUntil) return;

        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground == _hwnd) return;
        GetWindowThreadProcessId(foreground, out var pid);
        if (pid == (uint)Process.GetCurrentProcess().Id) return;
        _previousForegroundWindow = foreground;

        // Preserve the full window during keyboard window switching.  Do not turn
        // an Alt+Tab action into a minimize/bubble action.
        if (keyboardSwitch || DateTime.UtcNow < _keyboardSwitcherUntil)
            return;

        // The one suspension owned by an inline draft must not trap the window
        // when another application is clicked. Extra suspensions guard menus/dialogs.
        if (InlineEditDismissal.ShouldBlockOutsideHide(_suspendAutoHide, _inlineEditItem?.IsInlineEditing == true)
            || _shortcutHidden || !IsVisible) return;

        if (_switchNonInterferenceMode)
        {
            _ = Dispatcher.BeginInvoke(new Action(CloseToBubble));
            return;
        }

        // Normal mode: a mouse click into another app should feel like an ordinary
        // Windows program. Minimize, keep the taskbar button, and remain available to
        // Alt+Tab / previous-window switching. No bubble and no UI process teardown.
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_allowClose && IsVisible && WindowState != WindowState.Minimized)
                WindowState = WindowState.Minimized;
        }));
    }

    private bool IsWindowSwitchChordDown()
    {
        static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;
        return (Down(VK_MENU) && Down(VK_TAB)) || ((Down(VK_LWIN) || Down(VK_RWIN)) && Down(VK_TAB));
    }

    private void TogglePopup()
    {
        CloseForTrueHide();
    }

    private void HideWorkingUi()
    {
        CloseForTrueHide();
    }

    private void ShowMainWindow()
    {
        ActivateFromCore();
    }

    private void ToggleFastHotkey() => OpenPicker(toggle: true);

    private void ActivateFromCore()
    {
        _picker?.HidePicker();
        _shortcutHidden = false;
        UpdateTrayUi();
        _ignoreDeactivationUntil = DateTime.UtcNow.AddMilliseconds(650);
        if (!IsVisible) Show();
        WindowState = WindowState.Normal;
        ApplyTaskSwitcherMode();
        ForceForeground();

        // The tray/menu focus hand-off can finish a fraction later than the first
        // activation attempt. One delayed retry makes wake-up deterministic without
        // keeping a timer or background polling loop alive.
        Dispatcher.BeginInvoke(new Action(async () =>
        {
            await Task.Delay(140);
            if (_allowClose || !IsVisible) return;
            ForceForeground();
        }));

        if (HistoryList.Items.Count > 0 && HistoryList.SelectedIndex < 0)
            HistoryList.SelectedIndex = 0;
    }

    private void ForceForeground()
    {
        try
        {
            Activate();
            if (_hwnd != IntPtr.Zero) SetForegroundWindow(_hwnd);
            Topmost = true;
            Topmost = false;
            Focus();
            SearchBox.Focus();
            SearchBox.SelectAll();
        }
        catch { }
    }

    private void SaveBeforeExit()
    {
        try
        {
            CaptureWindowLayout();
            _settings.CapturePaused = _capturePaused;
            _settings.SwitchNonInterferenceMode = _switchNonInterferenceMode;
            QueueSave();
            _store.SaveNow();
        }
        catch { }
    }

    private void CloseForTrueHide()
    {
        if (_allowClose || _picker?.IsBusy == true) return;
        if (!ResolveInlineEdit()) return;
        SaveBeforeExit(); _picker?.HidePicker(); _shortcutHidden = true; Hide();
    }
    private void CloseToBubble()
    {
        if (_allowClose || !ResolveInlineEdit()) return;
        SaveBeforeExit(); _coreBridge.ShowBubble(); Hide();
    }
    private void MinimizeToBubble() => CloseToBubble();
    private void CancelWarmStandby() { } // Compatibility callers: no idle-exit timer.

    private void ApplyTaskSwitcherMode()
    {
        if (_switchNonInterferenceMode) ExcludeFromTaskSwitcher();
        else IncludeInTaskSwitcher();
        UpdateModeStatusUi();
    }

    private void UpdateModeStatusUi()
    {
        if (ModeStateText == null) return;
        ModeStateText.Text = _switchNonInterferenceMode ? "模式：不干擾" : "模式：一般";
        ModeStateText.ToolTip = _switchNonInterferenceMode
            ? "浮動圖示模式，不佔一般 Alt+Tab / 工作列切換"
            : "一般模式，可從工作列 / Alt+Tab 直接切回 ClipShelf";
    }

    private void IncludeInTaskSwitcher()
    {
        if (_hwnd == IntPtr.Zero) return;
        try
        {
            ShowInTaskbar = true;
            var style = GetWindowLong(_hwnd, GWL_EXSTYLE);
            style &= ~WS_EX_TOOLWINDOW;
            style |= WS_EX_APPWINDOW;
            SetWindowLong(_hwnd, GWL_EXSTYLE, style);
            SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }
        catch { }
    }

    private void ExcludeFromTaskSwitcher()
    {
        if (_hwnd == IntPtr.Zero) return;
        try
        {
            ShowInTaskbar = false;
            var style = GetWindowLong(_hwnd, GWL_EXSTYLE);
            style &= ~WS_EX_APPWINDOW;
            style |= WS_EX_TOOLWINDOW;
            SetWindowLong(_hwnd, GWL_EXSTYLE, style);
            SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }
        catch { }
    }

    private void InstallTaskbarSystemMenu()
    {
        if (_hwnd == IntPtr.Zero) return;
        try
        {
            var menu = GetSystemMenu(_hwnd, false);
            if (menu == IntPtr.Zero) return;
            AppendMenu(menu, MF_SEPARATOR, UIntPtr.Zero, null);
            AppendMenu(menu, MF_STRING, new UIntPtr((uint)SC_CLIPSHELF_NONINTERFERENCE), "ClipShelf：切換不干擾模式");
        }
        catch { }
    }

    private void InstallTaskbarJumpList()
    {
        try
        {
            var corePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "ClipShelf.exe"));
            if (!File.Exists(corePath)) return;

            var jumpList = new System.Windows.Shell.JumpList();
            System.Windows.Shell.JumpList.SetJumpList(Application.Current, jumpList);
            jumpList.JumpItems.Add(new System.Windows.Shell.JumpTask
            {
                Title = "切換不干擾模式",
                Description = "縮成浮動圖示，暫時不佔 Alt+Tab / 一般視窗切換",
                ApplicationPath = corePath,
                Arguments = "--noninterference",
                IconResourcePath = corePath
            });
            jumpList.JumpItems.Add(new System.Windows.Shell.JumpTask
            {
                Title = "恢復一般模式",
                Description = "恢復工作列 / Alt+Tab，可直接切回完整 ClipShelf",
                ApplicationPath = corePath,
                Arguments = "--normal-mode",
                IconResourcePath = corePath
            });
            jumpList.Apply();
        }
        catch { }
    }

    private void EnableSwitchNonInterferenceMode()
    {
        if (_switchNonInterferenceMode) return;
        _switchNonInterferenceMode = true;
        _settings.SwitchNonInterferenceMode = true;
        QueueSave();
        _store.SaveNow();
        _coreBridge.NotifyStateChanged();
        ApplyTaskSwitcherMode();
        // Enter the mode immediately and visibly: the normal taskbar/switcher entry is
        // removed and the desktop bubble becomes the explicit return point.
        CloseToBubble();
    }

    private void RestoreNormalSwitchMode()
    {
        _switchNonInterferenceMode = false;
        _settings.SwitchNonInterferenceMode = false;
        QueueSave();
        _store.SaveNow();
        _coreBridge.NotifyStateChanged();
        CancelWarmStandby();
        if (!IsVisible) Show();
        WindowState = WindowState.Normal;
        ApplyTaskSwitcherMode();
        ForceForeground();
    }

    private void PositionNearCursor()
    {
        if (!GetCursorPos(out var p)) return;
        var workArea = SystemParameters.WorkArea;
        var left = p.X - Width * 0.50;
        var top = p.Y - Height * 0.22;
        Left = Math.Max(workArea.Left + 8, Math.Min(left, workArea.Right - Width - 8));
        Top = Math.Max(workArea.Top + 8, Math.Min(top, workArea.Bottom - Height - 8));
    }

    private void CaptureClipboard()
    {
        if (_capturePaused) return;

        _deferInlineClipboardRefresh = _inlineEditItem != null;
        try
        {
            var sourceInfo = GetSourceInfo();
            if (sourceInfo.App.Equals(Process.GetCurrentProcess().ProcessName, StringComparison.OrdinalIgnoreCase)) return;

            if (System.Windows.Clipboard.ContainsFileDropList())
            {
                if (!_settings.CaptureFiles) return;
                var files = System.Windows.Clipboard.GetFileDropList().Cast<string>().ToList();
                if (files.Count > 0)
                {
                    var key = "F|" + string.Join("|", files);
                    AddOrTouchItem(new ClipItem
                    {
                        Kind = "Files",
                        Files = files,
                        ContentHash = Sha256(key),
                        SourceApp = sourceInfo.App,
                        SourceTitle = sourceInfo.Title
                    });
                }
                return;
            }

            if (System.Windows.Clipboard.ContainsImage())
            {
                if (!_settings.CaptureImages) return;
                var image = System.Windows.Clipboard.GetImage();
                if (image != null)
                {
                    var bytes = EncodePng(image);
                    var hash = Sha256(bytes);
                    var existing = _allItems.FirstOrDefault(x => !x.IsDeleted && !x.IsPendingPurge && x.Kind == "Image" && x.ContentHash == hash);
                    if (existing != null)
                    {
                        Touch(existing, sourceInfo.App, sourceInfo.Title);
                        return;
                    }

                    var imagePath = Path.Combine(GetImageStorageDirectory(), $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.png");
                    File.WriteAllBytes(imagePath, bytes);
                    AddOrTouchItem(new ClipItem
                    {
                        Kind = "Image",
                        ImagePath = imagePath,
                        ContentHash = hash,
                        SourceApp = sourceInfo.App,
                        SourceTitle = sourceInfo.Title,
                        ManagedByClipShelf = true
                    });
                }
                return;
            }

            if (_settings.CaptureText && System.Windows.Clipboard.ContainsText())
            {
                var text = System.Windows.Clipboard.GetText();
                if (!string.IsNullOrEmpty(text))
                {
                    AddOrTouchItem(new ClipItem
                    {
                        Kind = "Text",
                        Text = text,
                        ContentHash = Sha256("T|" + text),
                        SourceApp = sourceInfo.App,
                        SourceTitle = sourceInfo.Title
                    });
                }
            }
        }
        catch
        {
            // Clipboard can be temporarily locked by another process. Ignore this update.
        }
        finally { _deferInlineClipboardRefresh = false; }
    }

    private void AddOrTouchItem(ClipItem incoming)
    {
        var existing = _allItems.FirstOrDefault(x => !x.IsDeleted && !x.IsPendingPurge && x.ContentHash == incoming.ContentHash && x.Kind == incoming.Kind);
        if (existing != null)
        {
            Touch(existing, incoming.SourceApp, incoming.SourceTitle);
            return;
        }

        incoming.CreatedAt = DateTime.Now;
        incoming.LastUsedAt = incoming.CreatedAt;
        incoming.HistoryVisible = true;
        incoming.UseCount = 1;
        incoming.ShowSelector = _multiSelectMode;
        _allItems.Insert(0, incoming);
        TrimHistory();
        RefreshVisibleItems();
        QueueSave();
    }

    private void Touch(ClipItem item, string sourceApp, string sourceTitle)
    {
        item.LastUsedAt = DateTime.Now;
        item.HistoryVisible = true;
        item.UseCount++;
        item.SourceApp = sourceApp;
        item.SourceTitle = sourceTitle;
        _allItems.Remove(item);
        _allItems.Insert(0, item);
        RefreshVisibleItems();
        QueueSave();
    }

    private void TrimHistory()
    {
        var maxRegularItems = _settings.MaxRegularItems;
        if (maxRegularItems <= 0) return;

        // Limit the History surface itself, not the user's organized library.
        // Overflow entries that still have a durable placement are merely removed
        // from History; standalone inbox items can be deleted normally.
        var overflow = _allItems
            .Where(x => !x.IsDeleted && !x.IsPendingPurge && x.HistoryVisible && !x.IsPinned)
            .OrderByDescending(x => x.LastUsedAt)
            .Skip(maxRegularItems)
            .ToList();

        foreach (var item in overflow)
        {
            if (HasPersistentPlacement(item)) item.HistoryVisible = false;
            else SoftDeleteItems(new[] { item }, refresh: false, showUndo: false);
        }
    }

    private void RefreshVisibleItems()
    {
        if (_deferInlineClipboardRefresh && _inlineEditItem != null)
        {
            _inlineClipboardRefreshPending = true;
            return;
        }
        var selectedId = (HistoryList.SelectedItem as ClipItem)?.Id;
        var query = SearchBox.Text?.Trim() ?? "";
        var tokens = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var selectedCategory = CurrentSection();

        IEnumerable<ClipItem> items;
        if (selectedCategory.Kind == "Trash")
        {
            items = _allItems.Where(x => x.IsDeleted);
        }
        else
        {
            // Deleted items keep their original placement metadata for accurate restore,
            // but they must not leak back into any active section.
            items = _allItems.Where(x => !x.IsDeleted && !x.IsPendingPurge);
        }

        if (!string.IsNullOrWhiteSpace(query) && _settings.GlobalSearch && selectedCategory.Kind != "Trash") { }
        else if (selectedCategory.Kind == "History")
        {
            // v2.5: History is a recent-use surface, independent from folder ownership.
            // Copying an item from a folder makes it visible here again, while deleting
            // the History entry never destroys an organized source item.
            items = items.Where(x => x.HistoryVisible);
        }
        else if (selectedCategory.Kind == "Favorite")
        {
            items = items.Where(x => x.Favorite);
        }
        else if (selectedCategory.Kind == "Temporary")
        {
            items = items.Where(x => x.IsTemporary);
        }
        else if (selectedCategory.Kind == "AI")
        {
            // Root shows the whole AI subtree; children can then be used to narrow it.
            items = items.Where(x => string.Equals(x.Category, "AI 用語", StringComparison.Ordinal) ||
                                     x.Category.StartsWith("AI 用語/", StringComparison.Ordinal));
        }
        else if (selectedCategory.Kind == "Quick")
        {
            items = items.Where(x => string.Equals(x.Category, "快速用語", StringComparison.Ordinal) ||
                                     x.Category.StartsWith("快速用語/", StringComparison.Ordinal));
        }
        else if (selectedCategory.IsCustomFolder)
        {
            var relative = SidebarItem.GetRelativePath(selectedCategory.Path);
            if (selectedCategory.IsFavoriteFolder)
                items = items.Where(x => x.Favorite && string.Equals(SidebarItem.NormalizePath(x.FavoriteFolder), relative, StringComparison.Ordinal));
            else if (selectedCategory.IsTemporaryFolder)
                items = items.Where(x => x.IsTemporary && string.Equals(SidebarItem.NormalizePath(x.TemporaryFolder), relative, StringComparison.Ordinal));
            else if (selectedCategory.IsAiFolder)
                items = items.Where(x => string.Equals(x.Category, CombineDataCategory("AI 用語", relative), StringComparison.Ordinal));
            else if (selectedCategory.IsQuickFolder)
                items = items.Where(x => string.Equals(x.Category, CombineDataCategory("快速用語", relative), StringComparison.Ordinal));
            else
                items = items.Where(x => string.Equals(x.Category, selectedCategory.Path, StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            items = items.Where(x => tokens.All(t => SearchText(x).Contains(t, StringComparison.OrdinalIgnoreCase)));
        }

        if (_contentKindFilter != "All")
            items = items.Where(x => string.Equals(x.Kind, _contentKindFilter, StringComparison.Ordinal));

        items = selectedCategory.Kind == "Trash"
            ? items.OrderByDescending(x => x.DeletedAt ?? DateTime.MinValue)
            : items.OrderByDescending(x => x.LastUsedAt);
        _visibleItems.Clear();
        foreach (var item in items) { item.Compact = _settings.CompactView; item.SearchExcerpt = BuildExcerpt(item, query); _visibleItems.Add(item); }

        SectionTitle.Text = (!string.IsNullOrWhiteSpace(query) && _settings.GlobalSearch && selectedCategory.Kind != "Trash") ? "搜尋全部內容" : SectionDisplayPath(selectedCategory);
        CountText.Text = $"{_visibleItems.Count:N0} 筆";
        AddSubfolderButton.Visibility = CanHaveSubfolders(selectedCategory) ? Visibility.Visible : Visibility.Collapsed;
        AddFileButton.Visibility = selectedCategory.Kind == "Trash" ? Visibility.Collapsed : Visibility.Visible;
        EmptyTrashButton.Visibility = selectedCategory.Kind == "Trash" ? Visibility.Visible : Visibility.Collapsed;
        EmptyTrashButton.IsEnabled = selectedCategory.Kind == "Trash" && _allItems.Any(x => x.IsDeleted && !x.IsPendingPurge);
        SectionHintText.Text = selectedCategory.Kind == "Trash"
            ? (_settings.TrashRetentionDays <= 0
                ? "刪除的項目會保留到你手動永久刪除；永久刪除確認後仍有 8 秒可反悔"
                : $"刪除的項目會保留 {_settings.TrashRetentionDays} 天；永久刪除確認後仍有 8 秒可反悔")
            : selectedCategory.Kind == "History"
                ? "歷史＝最近複製 / 擷取；移除歷史不會刪掉已整理的原資料"
                : "垃圾桶只用於真正刪除；移出收藏 / 暫存 / 資料夾不會破壞原內容";
        UpdateMultiSelectUi();

        if (selectedId.HasValue)
        {
            var same = _visibleItems.FirstOrDefault(x => x.Id == selectedId.Value);
            if (same != null) HistoryList.SelectedItem = same;
        }
        if (HistoryList.SelectedIndex < 0 && _visibleItems.Count > 0) HistoryList.SelectedIndex = 0;
    }

    private void QueueSave()
    {
        _picker?.Synchronize();
        CaptureWindowLayout();
        _store.QueueSave(_allItems, _folderPaths.ToList(), _settings);
    }

    private void ApplySavedWindowLayout()
    {
        Width = Math.Max(MinWidth, Math.Min(_settings.WindowWidth, SystemParameters.VirtualScreenWidth));
        Height = Math.Max(MinHeight, Math.Min(_settings.WindowHeight, SystemParameters.VirtualScreenHeight));
        SidebarColumn.Width = new GridLength(Math.Max(110, Math.Min(_settings.SidebarWidth, 320)));

        if (_settings.HasWindowPlacement)
        {
            var left = _settings.WindowLeft;
            var top = _settings.WindowTop;
            var onScreen = left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 60 &&
                           top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 60 &&
                           left + Width > SystemParameters.VirtualScreenLeft + 60 &&
                           top + Height > SystemParameters.VirtualScreenTop + 60;
            if (onScreen)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = left;
                Top = top;
            }
        }
    }

    private void CaptureWindowLayout()
    {
        if (!IsLoaded || WindowState != WindowState.Normal) return;
        if (ActualWidth >= MinWidth) _settings.WindowWidth = ActualWidth;
        if (ActualHeight >= MinHeight) _settings.WindowHeight = ActualHeight;
        if (!double.IsNaN(Left) && !double.IsInfinity(Left)) _settings.WindowLeft = Left;
        if (!double.IsNaN(Top) && !double.IsInfinity(Top)) _settings.WindowTop = Top;
        _settings.HasWindowPlacement = true;
        if (SidebarColumn.ActualWidth > 0) _settings.SidebarWidth = SidebarColumn.ActualWidth;
    }

    private void CopyItem(ClipItem item)
    {
        if (item.IsDeleted || item.IsPendingPurge) return;
        try
        {
            _ignoreClipboardUntil = DateTime.UtcNow.AddMilliseconds(700);
            PickerClipboard.Write(item, false);
            _lastInternalClipboardSequence = PickerClipboard.GetClipboardSequenceNumber();
            item.UseCount++;
            item.LastUsedAt = DateTime.Now;
            item.HistoryVisible = true;
            // A copy updates history metadata, but must not recycle the clicked
            // TextBox or move the current card/scroll position. Navigation and
            // subsequent captures still use LastUsedAt to sort recent history.
            CaptureStateText.Text = "已複製";
            QueueSave();
        }
        catch { }
    }

    private async Task PasteItemAsync(ClipItem item)
    {
        var target = _previousForegroundWindow;
        CopyItem(item);
        await Task.Delay(90);
        if (target != IntPtr.Zero)
        {
            SetForegroundWindow(target);
            await Task.Delay(70);
            SendCtrlV();
        }
        CloseForTrueHide();
    }

    private void PurgeItemNow(ClipItem item, bool refresh = true)
    {
        _allItems.Remove(item);
        if (item.Kind == "Image" && !string.IsNullOrWhiteSpace(item.ImagePath))
        {
            try { if (File.Exists(item.ImagePath)) File.Delete(item.ImagePath); } catch { }
        }
        if (item.Kind == "Files" && item.ManagedByClipShelf)
        {
            foreach (var path in item.Files.ToList())
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                    else if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                }
                catch { }
            }
        }
        if (refresh)
        {
            RefreshVisibleItems();
            QueueSave();
        }
    }

    private void SoftDeleteItems(IEnumerable<ClipItem> source, bool refresh = true, bool showUndo = true)
    {
        var items = source.Where(x => !x.IsDeleted && !x.IsPendingPurge).Distinct().ToList();
        if (items.Count == 0) return;

        var deletedAt = DateTime.Now;
        var ids = items.Select(x => x.Id).ToList();
        foreach (var item in items)
        {
            item.IsDeleted = true;
            item.DeletedAt = deletedAt;
            item.IsPendingPurge = false;
            item.PendingPurgeAt = null;
            item.IsMarked = false;
        }

        if (refresh)
        {
            RefreshVisibleItems();
            QueueSave();
        }

        if (showUndo)
        {
            var message = items.Count == 1 ? "已移到「最近刪除」" : $"已將 {items.Count} 筆移到「最近刪除」";
            ShowUndo(message, () => RestoreTrashByIds(ids));
        }
    }

    private void RestoreTrashByIds(IEnumerable<Guid> ids, bool showFeedback = false)
    {
        var idSet = ids.ToHashSet();
        var items = _allItems.Where(x => idSet.Contains(x.Id) && x.IsDeleted).ToList();
        if (items.Count == 0) return;

        foreach (var item in items)
        {
            item.IsDeleted = false;
            item.DeletedAt = null;
            item.IsPendingPurge = false;
            item.PendingPurgeAt = null;
            item.IsMarked = false;

            // A valid item should always remain reachable. If all placements were removed
            // while it sat in Recently Deleted, restore it to History as a safe fallback.
            if (!HasPersistentPlacement(item) && !item.HistoryVisible)
                item.HistoryVisible = true;
        }

        SchedulePendingPurgeTimer();
        RefreshVisibleItems();
        QueueSave();
        if (showFeedback)
            ShowUndo(items.Count == 1 ? "已復原 1 筆" : $"已復原 {items.Count} 筆", null, 3);
    }

    private void RequestPermanentPurge(IEnumerable<ClipItem> source, bool confirm = true)
    {
        var items = source.Where(x => x.IsDeleted && !x.IsPendingPurge).Distinct().ToList();
        if (items.Count == 0) return;

        if (confirm)
        {
            var payloadNote = items.Any(x => x.ManagedByClipShelf && (x.Kind == "Image" || x.Kind == "Files"))
                ? "\n\nClipShelf 管理的圖片 / 檔案也會一起清除。"
                : "";
            var text = items.Count == 1
                ? $"永久刪除這筆內容？{payloadNote}\n\n確認後仍有 {PermanentDeleteGraceSeconds} 秒可以反悔，之後無法復原。"
                : $"永久刪除選取的 {items.Count} 筆內容？{payloadNote}\n\n確認後仍有 {PermanentDeleteGraceSeconds} 秒可以反悔，之後無法復原。";
            if (MessageBox.Show(this, text, "永久刪除", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
        }

        var due = DateTime.Now.AddSeconds(PermanentDeleteGraceSeconds);
        var ids = items.Select(x => x.Id).ToList();
        foreach (var item in items)
        {
            item.IsPendingPurge = true;
            item.PendingPurgeAt = due;
            item.IsMarked = false;
        }

        RefreshVisibleItems();
        QueueSave();
        SchedulePendingPurgeTimer();
        var message = items.Count == 1
            ? $"永久刪除將在 {PermanentDeleteGraceSeconds} 秒後完成"
            : $"{items.Count} 筆永久刪除將在 {PermanentDeleteGraceSeconds} 秒後完成";
        ShowUndo(message, () => CancelPendingPurge(ids), PermanentDeleteGraceSeconds);
    }

    private void CancelPendingPurge(IEnumerable<Guid> ids)
    {
        var idSet = ids.ToHashSet();
        var items = _allItems.Where(x => idSet.Contains(x.Id) && x.IsPendingPurge).ToList();
        if (items.Count == 0) return;
        foreach (var item in items)
        {
            item.IsPendingPurge = false;
            item.PendingPurgeAt = null;
        }
        SchedulePendingPurgeTimer();
        RefreshVisibleItems();
        QueueSave();
    }

    private void EmptyTrash()
    {
        var items = _allItems.Where(x => x.IsDeleted && !x.IsPendingPurge).ToList();
        if (items.Count == 0) return;

        var text = $"清空最近刪除中的 {items.Count} 筆內容？\n\n確認後仍有 {PermanentDeleteGraceSeconds} 秒可以反悔，之後無法復原。";
        if (items.Any(x => x.ManagedByClipShelf && (x.Kind == "Image" || x.Kind == "Files")))
            text += "\nClipShelf 管理的圖片 / 檔案也會一起清除。";
        if (MessageBox.Show(this, text, "清空最近刪除", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        RequestPermanentPurge(items, confirm: false);
    }

    private void FinalizeExpiredPendingPurges(bool queueSave = true)
    {
        var now = DateTime.Now;
        var due = _allItems
            .Where(x => x.IsDeleted && x.IsPendingPurge && x.PendingPurgeAt.HasValue && x.PendingPurgeAt.Value <= now)
            .ToList();
        if (due.Count == 0) return;

        foreach (var item in due)
            PurgeItemNow(item, false);

        if (queueSave)
        {
            RefreshVisibleItems();
            QueueSave();
        }
    }

    private void PurgeExpiredTrash(bool queueSave = true)
    {
        var days = _settings.TrashRetentionDays;
        if (days <= 0) return;

        var cutoff = DateTime.Now.AddDays(-days);
        var expired = _allItems
            .Where(x => x.IsDeleted && !x.IsPendingPurge && x.DeletedAt.HasValue && x.DeletedAt.Value <= cutoff)
            .ToList();
        if (expired.Count == 0) return;

        foreach (var item in expired)
            PurgeItemNow(item, false);

        if (queueSave)
        {
            RefreshVisibleItems();
            QueueSave();
        }
    }

    private void SchedulePendingPurgeTimer()
    {
        _pendingPurgeTimer?.Stop();
        _pendingPurgeTimer = null;

        var next = _allItems
            .Where(x => x.IsDeleted && x.IsPendingPurge && x.PendingPurgeAt.HasValue)
            .Select(x => x.PendingPurgeAt!.Value)
            .OrderBy(x => x)
            .FirstOrDefault();
        if (next == default) return;

        var delay = next - DateTime.Now;
        if (delay < TimeSpan.FromMilliseconds(100)) delay = TimeSpan.FromMilliseconds(100);
        _pendingPurgeTimer = new DispatcherTimer { Interval = delay };
        _pendingPurgeTimer.Tick += (_, _) =>
        {
            _pendingPurgeTimer?.Stop();
            _pendingPurgeTimer = null;
            FinalizeExpiredPendingPurges();
            SchedulePendingPurgeTimer();
        };
        _pendingPurgeTimer.Start();
    }

    private void ShowUndo(string message, Action? undoAction, int seconds = 8)
    {
        _undoBarTimer?.Stop();
        _undoBarTimer = null;
        _undoAction = undoAction;
        UndoBarText.Text = message;
        UndoBar.Visibility = Visibility.Visible;

        if (undoAction == null)
        {
            // Feedback-only messages do not need a clickable undo button.
            if (UndoBar.Child is StackPanel panel && panel.Children.OfType<Button>().FirstOrDefault() is Button button)
                button.Visibility = Visibility.Collapsed;
        }
        else if (UndoBar.Child is StackPanel panel2 && panel2.Children.OfType<Button>().FirstOrDefault() is Button button2)
        {
            button2.Visibility = Visibility.Visible;
        }

        _undoBarTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(1, seconds)) };
        _undoBarTimer.Tick += (_, _) => HideUndoBar();
        _undoBarTimer.Start();
    }

    private void HideUndoBar()
    {
        _undoBarTimer?.Stop();
        _undoBarTimer = null;
        _undoAction = null;
        UndoBar.Visibility = Visibility.Collapsed;
    }

    private void UndoBarButton_Click(object sender, RoutedEventArgs e)
    {
        var action = _undoAction;
        HideUndoBar();
        action?.Invoke();
    }

    private static string CombineDataCategory(string root, string relative)
    {
        relative = SidebarItem.NormalizePath(relative);
        return string.IsNullOrWhiteSpace(relative) ? root : $"{root}/{relative}";
    }

    private static string DataCategoryForFolderKey(string folderKey)
    {
        var scope = SidebarItem.GetScope(folderKey);
        var relative = SidebarItem.GetRelativePath(folderKey);
        return scope switch
        {
            "AI" => CombineDataCategory("AI 用語", relative),
            "Quick" => CombineDataCategory("快速用語", relative),
            "Custom" => SidebarItem.NormalizePath(folderKey),
            _ => ""
        };
    }

    private static string SectionDisplayPath(SidebarItem section)
    {
        if (!section.IsCustomFolder) return section.Name;
        var relative = SidebarItem.GetRelativePath(section.Path).Replace("/", "  ›  ");
        return section.Scope switch
        {
            "Favorite" => $"收藏  ›  {relative}",
            "Temporary" => $"暫存  ›  {relative}",
            "AI" => $"AI 用語  ›  {relative}",
            "Quick" => $"快速用語  ›  {relative}",
            _ => section.Path.Replace("/", "  ›  ")
        };
    }

    private static bool CanHaveSubfolders(SidebarItem section)
        => section.IsCustomFolder || section.Kind is "Favorite" or "Temporary" or "AI" or "Quick";

    private static string RootKeyForSection(SidebarItem section)
        => SidebarItem.RootKeyForKind(section.Kind);

    private IEnumerable<string> GetAssignableCategories()
    {
        // Classification is still exclusive: AI/Quick/custom folders share Category.
        // Favorite/Temporary folders are intentionally NOT included here because they are
        // independent dimensions and are handled by their own flags/folder fields.
        yield return "AI 用語";
        yield return "快速用語";
        foreach (var key in _folderPaths)
        {
            var scope = SidebarItem.GetScope(key);
            if (scope is "Favorite" or "Temporary") continue;
            var dataCategory = DataCategoryForFolderKey(key);
            if (!string.IsNullOrWhiteSpace(dataCategory)) yield return dataCategory;
        }
    }

    private void MoveItemToCategory(ClipItem item, string category)
    {
        if (item.IsDeleted || item.IsPendingPurge) return;
        // A clip has exactly ONE classification category. 收藏與暫存 are separate dimensions.
        var fromHistory = CurrentSection().Kind == "History";
        item.Category = category ?? "";
        item.LastUsedAt = DateTime.Now;

        // Explicitly moving an inbox/history item into a classification folder keeps
        // the old "real move" UX: it leaves History. Copying it later from that folder
        // will surface it in History again without duplicating the source object.
        if (string.IsNullOrWhiteSpace(item.Category)) item.HistoryVisible = true;
        else if (fromHistory) item.HistoryVisible = false;

        RefreshVisibleItems();
        QueueSave();
    }

    private MenuItem BuildMoveToCategoryMenu(ClipItem item)
    {
        var root = new MenuItem { Header = "📁  移動到分類" };

        var none = new MenuItem
        {
            Header = string.IsNullOrWhiteSpace(item.Category) ? "✓  無分類（回到歷史）" : "　 無分類（回到歷史）"
        };
        none.Click += (_, _) => MoveItemToCategory(item, "");
        root.Items.Add(none);
        root.Items.Add(new Separator());

        foreach (var category in GetAssignableCategories().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var target = category;
            var display = target.Replace("/", "  ›  ");
            var child = new MenuItem
            {
                Header = string.Equals(item.Category, target, StringComparison.Ordinal) ? $"✓  {display}" : $"　 {display}"
            };
            child.Click += (_, _) => MoveItemToCategory(item, target);
            root.Items.Add(child);
        }
        return root;
    }

    private MenuItem? BuildMoveToFlagFolderMenu(ClipItem item, string scope)
    {
        var prefix = scope == "Favorite" ? SidebarItem.FavoritePrefix : SidebarItem.TemporaryPrefix;
        var folderKeys = _folderPaths.Where(x => string.Equals(SidebarItem.GetScope(x), scope, StringComparison.Ordinal)).ToList();
        if (folderKeys.Count == 0) return null;

        var isFavorite = scope == "Favorite";
        var root = new MenuItem { Header = isFavorite ? "⭐  移動到收藏資料夾" : "⏱  移動到暫存資料夾" };
        var currentPath = SidebarItem.NormalizePath(isFavorite ? item.FavoriteFolder : item.TemporaryFolder);

        var rootItem = new MenuItem { Header = string.IsNullOrWhiteSpace(currentPath) && (isFavorite ? item.Favorite : item.IsTemporary) ? "✓  根目錄" : "　 根目錄" };
        rootItem.Click += (_, _) =>
        {
            if (isFavorite) { item.Favorite = true; item.FavoriteFolder = ""; }
            else { item.IsTemporary = true; item.TemporaryFolder = ""; }
            item.LastUsedAt = DateTime.Now;
            RefreshVisibleItems();
            QueueSave();
        };
        root.Items.Add(rootItem);
        root.Items.Add(new Separator());

        foreach (var key in folderKeys)
        {
            var rel = SidebarItem.GetRelativePath(key);
            var display = rel.Replace("/", "  ›  ");
            var child = new MenuItem { Header = string.Equals(currentPath, rel, StringComparison.Ordinal) ? $"✓  {display}" : $"　 {display}" };
            child.Click += (_, _) =>
            {
                if (isFavorite) { item.Favorite = true; item.FavoriteFolder = rel; }
                else { item.IsTemporary = true; item.TemporaryFolder = rel; }
                item.LastUsedAt = DateTime.Now;
                RefreshVisibleItems();
                QueueSave();
            };
            root.Items.Add(child);
        }
        return root;
    }

    private SidebarItem CurrentSection()
    {
        if (CategoryList.SelectedItem is SidebarItem selected) {
            if (_settings.GlobalSearch && !string.IsNullOrWhiteSpace(SearchBox.Text) && selected.Kind != "Trash")
                return SidebarItem.System("Search", "⌕", "搜尋全部內容");
            return selected;
        }
        return _categories.FirstOrDefault(x => x.Kind == "History")
               ?? SidebarItem.System("History", "🕘", "歷史");
    }

    private bool IsFolderSection(SidebarItem section)
        => section.IsFolderSection;

    private string FolderNameFromSection(SidebarItem section)
    {
        if (section.Kind == "AI") return "AI 用語";
        if (section.Kind == "Quick") return "快速用語";
        if (section.IsCustomFolder) return DataCategoryForFolderKey(section.Path);
        return "";
    }

    private void RemoveFromCurrentSectionOrDelete(ClipItem item, bool refresh = true)
    {
        if (item.IsDeleted || item.IsPendingPurge) return;

        var section = CurrentSection();
        if (section.Kind == "Favorite")
        {
            item.Favorite = false;
            item.FavoriteFolder = "";
        }
        else if (section.Kind == "Temporary")
        {
            item.IsTemporary = false;
            item.TemporaryFolder = "";
        }
        else if (section.IsFavoriteFolder)
        {
            // Remove only from this favorite subfolder; keep it favorited at the 收藏 root.
            item.FavoriteFolder = "";
        }
        else if (section.IsTemporaryFolder)
        {
            // Remove only from this temporary subfolder; keep it in 暫存 root.
            item.TemporaryFolder = "";
        }
        else if (IsFolderSection(section))
        {
            var folder = FolderNameFromSection(section);
            if (string.Equals(item.Category, folder, StringComparison.Ordinal))
            {
                item.Category = "";
                item.HistoryVisible = true;
            }
        }
        else if (section.Kind == "History")
        {
            if (HasPersistentPlacement(item))
                item.HistoryVisible = false;
            else
            {
                SoftDeleteItems(new[] { item }, refresh, showUndo: refresh);
                return;
            }
        }
        else
        {
            SoftDeleteItems(new[] { item }, refresh, showUndo: refresh);
            return;
        }

        if (refresh)
        {
            RefreshVisibleItems();
            QueueSave();
        }
    }

    private string CurrentRemoveActionText(ClipItem item)
    {
        var section = CurrentSection();
        if (section.Kind == "Favorite") return "☆  從收藏移除";
        if (section.Kind == "Temporary") return "⏱  從暫存移除";
        if (section.IsFavoriteFolder) return "↩  從此收藏資料夾移出";
        if (section.IsTemporaryFolder) return "↩  從此暫存資料夾移出";
        if (IsFolderSection(section)) return "↩  從此資料夾移出";
        if (section.Kind == "History" && HasPersistentPlacement(item)) return "🕘  從歷史移除（保留原資料）";
        return "🗑  移到最近刪除";
    }

    private void CopyAndHide(ClipItem item)
    {
        CopyItem(item);
        CloseForTrueHide();
    }

    private void EditTextItem(ClipItem item)
    {
        if (item.Kind != "Text" || item.IsDeleted || item.IsPendingPurge) return;

        var custom = GetAssignableCategories().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var dialog = new TextEditorDialog(
            custom,
            item.Category,
            item.Text,
            item.Favorite,
            item.IsTemporary,
            editMode: true, title: item.Title, note: item.Note)
        { Owner = this };

        var before = JsonSerializer.Deserialize<ClipItem>(JsonSerializer.Serialize(item))!;
        _suspendAutoHide++;
        try
        {
            if (dialog.ShowDialog() != true) return;

            // Keep the original dialog/keyboard flow, but share content identity
            // and repeat-count semantics with the new inline editor.
            ClipEditing.SaveText(item, dialog.Value, _allItems, () => { });
            item.Title = dialog.ItemTitle; item.Note = dialog.ItemNote;
            item.Category = dialog.SelectedCategory;
            item.Favorite = dialog.Favorite;
            item.IsTemporary = dialog.Temporary;
            if (!item.Favorite) item.FavoriteFolder = "";
            if (!item.IsTemporary) item.TemporaryFolder = "";
            item.LastUsedAt = DateTime.Now;
            item.SourceApp = "ClipShelf";
            item.SourceTitle = "手動編輯";

            QueueSave();
            _store.SaveNowChecked();
            RefreshVisibleItems();
            try { _coreBridge.NotifyStateChanged(); } catch { }
        }
        catch (Exception ex)
        {
            item.Title = before.Title; item.Note = before.Note;
            item.Text = before.Text; item.ContentHash = before.ContentHash; item.UseCount = before.UseCount;
            item.Category = before.Category; item.Favorite = before.Favorite; item.IsTemporary = before.IsTemporary;
            item.FavoriteFolder = before.FavoriteFolder; item.TemporaryFolder = before.TemporaryFolder;
            item.LastUsedAt = before.LastUsedAt; item.SourceApp = before.SourceApp; item.SourceTitle = before.SourceTitle;
            QueueSave();
            MessageBox.Show(this, "尚未儲存，原紀錄已保留。\n" + ex.Message, "ClipShelf", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _suspendAutoHide = Math.Max(0, _suspendAutoHide - 1);
        }
    }

    private static MenuItem BuildClipDetailsMenu(ClipItem item)
    {
        // Metadata stays available on demand, without competing with the actual clip.
        var info = new MenuItem { Header = "ⓘ  詳細資訊" };
        void Add(string label, string value)
        {
            var shortValue = value.Length > 72 ? value[..72] + "…" : value;
            info.Items.Add(new MenuItem { Header = label + "：" + shortValue, ToolTip = value, IsEnabled = false });
        }
        Add("類型", item.KindLabel);
        Add("建立時間", item.CreatedAt.ToString("yyyy/MM/dd HH:mm:ss"));
        Add("最近使用", item.LastUsedAt.ToString("yyyy/MM/dd HH:mm:ss"));
        Add("使用次數", item.UseCount.ToString());
        Add("分類", string.IsNullOrWhiteSpace(item.Category) ? "未分類" : item.Category);
        Add("來源", string.IsNullOrWhiteSpace(item.SourceApp) ? "未知" : item.SourceApp);
        if (!string.IsNullOrWhiteSpace(item.SourceTitle)) Add("來源標題", item.SourceTitle);
        Add("收藏", item.Favorite ? "是" : "否");
        Add("釘選", item.IsPinned ? "是" : "否");
        if (item.IsDeleted) Add("刪除時間", item.DeletedAt?.ToString("yyyy/MM/dd HH:mm:ss") ?? "未知");
        return info;
    }

    private void ShowMoreMenu(ClipItem item, FrameworkElement owner)
    {
        var menu = new ContextMenu();

        if (item.IsDeleted || CurrentSection().Kind == "Trash")
        {
            var restore = new MenuItem { Header = "↩  復原" };
            restore.Click += (_, _) => RestoreTrashByIds(new[] { item.Id }, showFeedback: true);
            var permanentDelete = new MenuItem
            {
                Header = item.IsPendingPurge ? "⏳  永久刪除倒數中" : "🗑  永久刪除",
                IsEnabled = !item.IsPendingPurge
            };
            permanentDelete.Click += (_, _) => RequestPermanentPurge(new[] { item });
            menu.Items.Add(restore);
            menu.Items.Add(new Separator());
            menu.Items.Add(permanentDelete);
            menu.Items.Add(new Separator());
            menu.Items.Add(BuildClipDetailsMenu(item));

            owner.ContextMenu = menu;
            _suspendAutoHide++;
            menu.Closed += (_, _) => _suspendAutoHide = Math.Max(0, _suspendAutoHide - 1);
            menu.IsOpen = true;
            return;
        }

        var copy = new MenuItem { Header = "▣  複製" };
        copy.Click += (_, _) => CopyItem(item);
        menu.Items.Add(copy);

        var openLocation = new MenuItem { Header = "📂  打開這筆內容的保存位置" };
        openLocation.Click += (_, _) => OpenItemLocation(item);
        menu.Items.Add(openLocation);

        if (item.Kind == "Text")
        {
            var preview = new MenuItem { Header = item.IsContentExpanded ? "收起全文預覽" : "展開全文預覽" };
            preview.Click += (_, _) => item.IsContentExpanded = !item.IsContentExpanded;
            menu.Items.Add(preview);
            var edit = new MenuItem { Header = "✎  編輯文字" };
            edit.Click += (_, _) => BeginInlineTextEdit(item);
            menu.Items.Add(new Separator());
            menu.Items.Add(edit);
            var details = new MenuItem { Header = "文字詳細設定（標題／備註／分類）" };
            details.Click += (_, _) => { if (ResolveInlineEdit()) EditTextItem(item); };
            menu.Items.Add(details);
        }

        var rename = new MenuItem { Header = "✎  重新命名標題" };
        rename.Click += (_, _) => BeginInlineTitleEdit(item);
        menu.Items.Add(rename);
        menu.Items.Add(BuildClipDetailsMenu(item));

        var category = BuildMoveToCategoryMenu(item);
        var favoriteFolderMenu = BuildMoveToFlagFolderMenu(item, "Favorite");
        var temporaryFolderMenu = BuildMoveToFlagFolderMenu(item, "Temporary");

        var fav = new MenuItem { Header = item.Favorite ? "☆  取消收藏" : "★  加入收藏" };
        fav.Click += (_, _) =>
        {
            item.Favorite = !item.Favorite;
            if (!item.Favorite) item.FavoriteFolder = "";
            RefreshVisibleItems();
            QueueSave();
        };

        var temp = new MenuItem { Header = item.IsTemporary ? "⏱  移出暫存" : "⏱  加入暫存" };
        temp.Click += (_, _) =>
        {
            item.IsTemporary = !item.IsTemporary;
            if (!item.IsTemporary) item.TemporaryFolder = "";
            RefreshVisibleItems();
            QueueSave();
        };

        var read = new MenuItem { Header = "閱讀 / 填空模板…" };
        read.Click += (_, _) => ReadItem(item); menu.Items.Add(read);
        var locate = new MenuItem { Header = "前往所在分類" };
        locate.Click += (_, _) => { SearchBox.Clear(); SelectFolderSection(item.Category); };
        menu.Items.Add(locate);
        var removeHere = new MenuItem { Header = CurrentRemoveActionText(item) };
        removeHere.Click += (_, _) => RemoveFromCurrentSectionOrDelete(item);

        menu.Items.Add(new Separator());
        menu.Items.Add(category);
        if (favoriteFolderMenu != null) menu.Items.Add(favoriteFolderMenu);
        if (temporaryFolderMenu != null) menu.Items.Add(temporaryFolderMenu);
        menu.Items.Add(fav);
        menu.Items.Add(temp);
        menu.Items.Add(new Separator());
        menu.Items.Add(removeHere);

        // Permanent deletion is intentionally not exposed in normal library views.
        // A true delete first enters Recently Deleted, where restore remains possible.
        if (!(CurrentSection().Kind == "History" && !HasPersistentPlacement(item)))
        {
            var moveToTrash = new MenuItem { Header = "🗑  移到最近刪除" };
            moveToTrash.Click += (_, _) => SoftDeleteItems(new[] { item });
            menu.Items.Add(moveToTrash);
        }

        owner.ContextMenu = menu;
        _suspendAutoHide++;
        menu.Closed += (_, _) => _suspendAutoHide = Math.Max(0, _suspendAutoHide - 1);
        TrackCardMenuFocus(menu);
        menu.IsOpen = true;
    }

    private static IntPtr ParseReturnWindowFromArgs()
    {
        try
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (!string.Equals(args[i], "--return-hwnd", StringComparison.OrdinalIgnoreCase)) continue;
                if (long.TryParse(args[i + 1], out var value)) return new IntPtr(value);
            }
        }
        catch { }
        return IntPtr.Zero;
    }

    private (string App, string Title) GetSourceInfo()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return ("", "");
            GetWindowThreadProcessId(hwnd, out var pid);
            var process = Process.GetProcessById((int)pid);
            var titleLength = GetWindowTextLength(hwnd);
            var sb = new StringBuilder(Math.Max(1, titleLength + 1));
            GetWindowText(hwnd, sb, sb.Capacity);
            return (process.ProcessName, sb.ToString());
        }
        catch { return ("", ""); }
    }

    private static byte[] EncodePng(System.Windows.Media.Imaging.BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static string Sha256(string input) => Sha256(Encoding.UTF8.GetBytes(input));
    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private void SetupMiniWindow()
    {
        _miniWindow = new MiniWindow();
        _miniWindow.RestoreRequested += () => Dispatcher.Invoke(ShowMainWindow);
        _miniWindow.HideRequested += () => Dispatcher.Invoke(HideWorkingUi);
        _miniWindow.ExitRequested += () => Dispatcher.Invoke(ExitApplication);
        _miniWindow.PositionChangedByUser += (left, top) => Dispatcher.Invoke(() =>
        {
            _settings.MiniLeft = left;
            _settings.MiniTop = top;
            _settings.HasMiniPlacement = true;
            QueueSave();
        });
    }

    private void SetupTrayIcon()
    {
        System.Drawing.Icon icon;
        try
        {
            var exePath = Environment.ProcessPath;
            icon = !string.IsNullOrWhiteSpace(exePath)
                ? (System.Drawing.Icon.ExtractAssociatedIcon(exePath) ?? System.Drawing.SystemIcons.Application)
                : System.Drawing.SystemIcons.Application;
        }
        catch
        {
            icon = System.Drawing.SystemIcons.Application;
        }

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = icon,
            Visible = true,
            Text = "ClipShelf - 執行中"
        };

        var menu = new System.Windows.Forms.ContextMenuStrip();

        var open = new System.Windows.Forms.ToolStripMenuItem("打開 ClipShelf");
        open.Click += (_, _) => Dispatcher.Invoke(ShowMainWindow);
        var quick = new System.Windows.Forms.ToolStripMenuItem("開啟快捷選取");
        quick.Click += (_, _) => Dispatcher.Invoke(() => OpenPicker()); menu.Items.Add(quick);

        _trayPauseItem = new System.Windows.Forms.ToolStripMenuItem("暫停自動保存");
        _trayPauseItem.Click += (_, _) => Dispatcher.Invoke(ToggleCapturePause);

        var bubble = new System.Windows.Forms.ToolStripMenuItem("縮成桌面浮動圖示");
        bubble.Click += (_, _) => Dispatcher.Invoke(MinimizeToBubble);

        var trueHide = new System.Windows.Forms.ToolStripMenuItem("真正隱藏（Ctrl+~）");
        trueHide.Click += (_, _) => Dispatcher.Invoke(HideWorkingUi);

        var openStorage = new System.Windows.Forms.ToolStripMenuItem("打開檔案存放位置");
        openStorage.Click += (_, _) => Dispatcher.Invoke(OpenStorageDirectory);

        var settings = new System.Windows.Forms.ToolStripMenuItem("設定");
        settings.Click += (_, _) => Dispatcher.Invoke(() =>
        {
            ShowMainWindow();
            Settings_Click(this, new RoutedEventArgs());
        });

        var exit = new System.Windows.Forms.ToolStripMenuItem("離開 ClipShelf");
        exit.Click += (_, _) => Dispatcher.Invoke(ExitApplication);

        menu.Items.Add(open);
        menu.Items.Add(_trayPauseItem);
        menu.Items.Add(bubble);
        menu.Items.Add(trueHide);
        menu.Items.Add(openStorage);
        menu.Items.Add(settings);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(exit);
        _trayIcon.ContextMenuStrip = menu;

        _trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
                Dispatcher.Invoke(ShowMainWindow);
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowMainWindow);

        UpdateTrayUi();
    }

    private void UpdateTrayUi()
    {
        if (_trayIcon == null) return;

        _trayIcon.Text = _capturePaused
            ? "ClipShelf - 執行中 (自動保存已暫停)"
            : _shortcutHidden
                ? "ClipShelf - 執行中 (介面已隱藏)"
                : "ClipShelf - 執行中";

        if (_trayPauseItem != null)
            _trayPauseItem.Text = _capturePaused ? "繼續自動保存" : "暫停自動保存";
    }

    private void DisposeTrayIcon()
    {
        if (_trayIcon == null) return;
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _trayIcon = null;
    }

    private void ExitApplication()
    {
        CloseForTrueHide();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!ResolveInlineEdit()) { e.Cancel = true; return; }
        CancelWarmStandby();
        if (!_allowClose)
        {
            e.Cancel = true;
            _ = Dispatcher.BeginInvoke(new Action(CloseToBubble));
            return;
        }

        _pendingPurgeTimer?.Stop();
        _undoBarTimer?.Stop();
        if (_hwnd != IntPtr.Zero)
            RemoveClipboardFormatListener(_hwnd);
        _uiServer?.Dispose(); _picker?.ShutdownPicker();
        _source?.RemoveHook(WndProc);
        SaveBeforeExit();
        try { _coreBridge.NotifyUiClosed(); } catch { }
        try { _coreBridge.Dispose(); } catch { }
        _store.Dispose();
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState != WindowState.Minimized && MaximizeButton != null)
        {
            MaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
            MaximizeButton.ToolTip = WindowState == WindowState.Maximized ? "還原視窗" : "最大化";
        }

        // Normal mode intentionally keeps a genuine minimized taskbar window.  Only
        // the explicitly selected non-interference mode converts minimize into bubble.
        if (!_switchNonInterferenceMode || _handlingMinimize || WindowState != WindowState.Minimized) return;
        _handlingMinimize = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                WindowState = WindowState.Normal;
                MinimizeToBubble();
            }
            finally
            {
                _handlingMinimize = false;
            }
        }));
    }

    private void Header_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var menu = new ContextMenu();
        var quick = new MenuItem { Header = "開啟快捷選取" };
        quick.Click += (_, _) => Dispatcher.BeginInvoke(new Action(() => OpenPicker()));
        menu.Items.Add(quick);
        var manage = new MenuItem { Header = "開啟完整管理" };
        manage.Click += (_, _) => ActivateFromCore(); menu.Items.Add(manage);
        menu.Items.Add(new Separator());
        if (_switchNonInterferenceMode)
        {
            var restoreSwitch = new MenuItem { Header = "恢復一般工作列 / Alt+Tab 模式" };
            restoreSwitch.Click += (_, _) => RestoreNormalSwitchMode();
            menu.Items.Add(restoreSwitch);
            menu.Items.Add(new Separator());
        }
        var bubble = new MenuItem { Header = "縮成桌面浮動圖示" };
        bubble.Click += (_, _) => MinimizeToBubble();
        var hide = new MenuItem { Header = "真正隱藏（Ctrl+~）" };
        hide.Click += (_, _) => HideWorkingUi();
        var storage = new MenuItem { Header = "打開檔案存放位置" };
        storage.Click += (_, _) => OpenStorageDirectory();
        menu.Items.Add(bubble);
        menu.Items.Add(hide);
        menu.Items.Add(new Separator());
        menu.Items.Add(storage);
        _suspendAutoHide++;
        menu.Closed += (_, _) => _suspendAutoHide = Math.Max(0, _suspendAutoHide - 1);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }

        if (WindowState == WindowState.Normal && e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void SidebarSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) => QueueSave();

    private void ContentFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string filter) return;
        if (!ResolveInlineEdit()) return;
        _contentKindFilter = filter;
        UpdateContentFilterUi();
        RefreshVisibleItems();
    }

    private void UpdateContentFilterUi()
    {
        if (FilterAllButton == null) return;
        var buttons = new[] { FilterAllButton, FilterTextButton, FilterImageButton, FilterFilesButton };
        foreach (var button in buttons)
        {
            var active = string.Equals(button.Tag as string, _contentKindFilter, StringComparison.Ordinal);
            button.Background = new SolidColorBrush(active ? Color.FromRgb(15, 118, 110) : Color.FromRgb(248, 250, 252));
            button.Foreground = new SolidColorBrush(active ? Colors.White : Color.FromRgb(71, 85, 105));
            button.BorderBrush = new SolidColorBrush(active ? Color.FromRgb(15, 118, 110) : Color.FromRgb(226, 232, 240));
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) { if (_searchDelay == null) return; _searchDelay.Stop(); _searchDelay.Start(); }
    private void ClearSearchButton_Click(object sender, RoutedEventArgs e) { SearchBox.Clear(); SearchBox.Focus(); }
    private void CategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Rebuilding the sidebar clears the collection first.  Do not refresh in that
        // transient empty state; older builds could throw here and close ClipShelf.
        if (_rebuildingSidebar || _restoringInlineEditSection) return;
        var editSection = _inlineEditSection;
        if (!ResolveInlineEdit())
        {
            _restoringInlineEditSection = true;
            try { CategoryList.SelectedItem = editSection; }
            finally { _restoringInlineEditSection = false; }
            return;
        }
        ClearMarkedItems();
        RefreshVisibleItems();
        UpdateMultiSelectUi();
    }
    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_switchNonInterferenceMode) MinimizeToBubble();
        else WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private enum FolderDropMode
    {
        None,
        Before,
        Inside,
        After,
        RootEnd
    }

    private static bool IsCustomCategory(SidebarItem? item)
        => item?.IsCustomFolder == true;

    private static string ParentPath(string? path)
    {
        path = SidebarItem.NormalizePath(path);
        if (string.IsNullOrWhiteSpace(path)) return "";
        var scope = SidebarItem.GetScope(path);
        var relative = SidebarItem.GetRelativePath(path);
        if (scope != "Custom")
        {
            var root = SidebarItem.GetScopeRoot(path);
            if (string.IsNullOrWhiteSpace(relative)) return "";
            var slash = relative.LastIndexOf('/');
            return slash < 0 ? root : $"{root}/{relative[..slash]}";
        }
        var index = path.LastIndexOf('/');
        return index < 0 ? "" : path[..index];
    }

    private static string LeafName(string? path)
    {
        var relative = SidebarItem.GetRelativePath(path);
        var slash = relative.LastIndexOf('/');
        return slash < 0 ? relative : relative[(slash + 1)..];
    }

    private static string CombineFolderPath(string parent, string name)
    {
        parent = SidebarItem.NormalizePath(parent);
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(parent)) return name;
        return $"{parent}/{name}";
    }

    private static bool IsScopeRootKey(string? path)
    {
        path = SidebarItem.NormalizePath(path);
        return path is SidebarItem.FavoritePrefix or SidebarItem.TemporaryPrefix or SidebarItem.AiPrefix or SidebarItem.QuickPrefix;
    }

    private static bool IsSameOrDescendant(string path, string parent)
    {
        path = SidebarItem.NormalizePath(path);
        parent = SidebarItem.NormalizePath(parent);
        return string.Equals(path, parent, StringComparison.OrdinalIgnoreCase) ||
               (!string.IsNullOrWhiteSpace(parent) && path.StartsWith(parent + "/", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> AncestorFolderKeys(string key)
    {
        key = SidebarItem.NormalizePath(key);
        if (string.IsNullOrWhiteSpace(key) || IsScopeRootKey(key)) yield break;
        var scope = SidebarItem.GetScope(key);
        var relative = SidebarItem.GetRelativePath(key);
        var parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var root = SidebarItem.GetScopeRoot(key);
        for (var i = 1; i <= parts.Length; i++)
        {
            var rel = string.Join("/", parts.Take(i));
            yield return scope == "Custom" ? rel : $"{root}/{rel}";
        }
    }

    private void RecoverFolderPathsFromItems()
    {
        void AddKey(string key)
        {
            key = SidebarItem.NormalizePath(key);
            if (string.IsNullOrWhiteSpace(key) || IsScopeRootKey(key)) return;
            if (!_folderPaths.Contains(key, StringComparer.OrdinalIgnoreCase)) _folderPaths.Add(key);
        }

        foreach (var item in _allItems.Where(x => !x.IsDeleted && !x.IsPendingPurge))
        {
            var category = SidebarItem.NormalizePath(item.Category);
            if (category.StartsWith("AI 用語/", StringComparison.OrdinalIgnoreCase))
                AddKey(SidebarItem.MakeScopedPath("AI", category["AI 用語/".Length..]));
            else if (category.StartsWith("快速用語/", StringComparison.OrdinalIgnoreCase))
                AddKey(SidebarItem.MakeScopedPath("Quick", category["快速用語/".Length..]));
            else if (!string.IsNullOrWhiteSpace(category) &&
                     !category.Equals("AI 用語", StringComparison.OrdinalIgnoreCase) &&
                     !category.Equals("快速用語", StringComparison.OrdinalIgnoreCase))
                AddKey(category);

            if (item.Favorite && !string.IsNullOrWhiteSpace(item.FavoriteFolder))
                AddKey(SidebarItem.MakeScopedPath("Favorite", item.FavoriteFolder));
            if (item.IsTemporary && !string.IsNullOrWhiteSpace(item.TemporaryFolder))
                AddKey(SidebarItem.MakeScopedPath("Temporary", item.TemporaryFolder));
        }
    }

    private void NormalizeFolderOrder()
    {
        var source = new List<string>();
        foreach (var raw in _folderPaths.ToList())
        {
            var path = SidebarItem.NormalizePath(raw);
            if (string.IsNullOrWhiteSpace(path) || IsScopeRootKey(path)) continue;
            foreach (var prefix in AncestorFolderKeys(path))
                if (!source.Contains(prefix, StringComparer.OrdinalIgnoreCase)) source.Add(prefix);
        }

        var ordered = new List<string>();
        void AddChildren(string parent)
        {
            foreach (var path in source.Where(x => string.Equals(ParentPath(x), parent, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                if (ordered.Contains(path, StringComparer.OrdinalIgnoreCase)) continue;
                ordered.Add(path);
                AddChildren(path);
            }
        }

        // Each built-in root owns its own independent tree. Custom folders form the last tree.
        AddChildren(SidebarItem.FavoritePrefix);
        AddChildren(SidebarItem.TemporaryPrefix);
        AddChildren(SidebarItem.AiPrefix);
        AddChildren(SidebarItem.QuickPrefix);
        AddChildren("");

        _folderPaths.Clear();
        _folderPaths.AddRange(ordered);
    }

    private static string SystemRootToken(string kind) => $"sys:{kind}";
    private static string CustomRootToken(string path) => $"folder:{SidebarItem.NormalizePath(path)}";

    private static bool TryGetSystemKindFromToken(string? token, out string kind)
    {
        token ??= "";
        const string prefix = "sys:";
        if (token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            kind = token[prefix.Length..];
            return kind is "Favorite" or "Temporary" or "AI" or "Quick";
        }
        kind = "";
        return false;
    }

    private static bool TryGetFolderPathFromToken(string? token, out string path)
    {
        token ??= "";
        const string prefix = "folder:";
        if (token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            path = SidebarItem.NormalizePath(token[prefix.Length..]);
            return !string.IsNullOrWhiteSpace(path);
        }
        // Backward compatibility with v2.2/v2.3 drag payloads that contained only the path.
        path = SidebarItem.NormalizePath(token);
        return !string.IsNullOrWhiteSpace(path);
    }

    private static string SidebarDragPayload(SidebarItem entry)
        => entry.IsSystemFolderRoot ? SystemRootToken(entry.Kind) : CustomRootToken(entry.Path);

    private static string? RootOrderTokenForEntry(SidebarItem? entry)
    {
        if (entry == null) return null;
        if (entry.IsSystemFolderRoot) return SystemRootToken(entry.Kind);
        if (entry.IsTopLevelCustomFolder) return CustomRootToken(entry.Path);
        return null;
    }

    private void NormalizeSidebarRootOrder()
    {
        var valid = new List<string>
        {
            SystemRootToken("Favorite"),
            SystemRootToken("Temporary"),
            SystemRootToken("AI"),
            SystemRootToken("Quick")
        };
        valid.AddRange(_folderPaths
            .Where(x => SidebarItem.GetScope(x) == "Custom" && string.IsNullOrWhiteSpace(ParentPath(x)))
            .Select(CustomRootToken));

        var normalized = new List<string>();
        foreach (var token in _settings.SidebarRootOrder ?? new List<string>())
        {
            if (valid.Contains(token, StringComparer.OrdinalIgnoreCase) &&
                !normalized.Contains(token, StringComparer.OrdinalIgnoreCase))
                normalized.Add(token);
        }
        foreach (var token in valid)
            if (!normalized.Contains(token, StringComparer.OrdinalIgnoreCase)) normalized.Add(token);

        _settings.SidebarRootOrder = normalized;
    }

    private void MoveSidebarRootByDrop(string draggedToken, string targetToken, bool after)
    {
        NormalizeSidebarRootOrder();
        var order = _settings.SidebarRootOrder;
        var dragged = order.FirstOrDefault(x => string.Equals(x, draggedToken, StringComparison.OrdinalIgnoreCase));
        var target = order.FirstOrDefault(x => string.Equals(x, targetToken, StringComparison.OrdinalIgnoreCase));
        if (dragged == null || target == null || string.Equals(dragged, target, StringComparison.OrdinalIgnoreCase)) return;

        order.Remove(dragged);
        var targetIndex = order.FindIndex(x => string.Equals(x, target, StringComparison.OrdinalIgnoreCase));
        if (targetIndex < 0) targetIndex = order.Count;
        var insertIndex = after ? targetIndex + 1 : targetIndex;
        order.Insert(Math.Max(0, Math.Min(insertIndex, order.Count)), dragged);

        RebuildFolderSidebar();
        QueueSave();
    }

    private bool IsSidebarNodeCollapsed(string token)
        => (_settings.CollapsedSidebarNodes ?? new List<string>())
            .Any(x => string.Equals(x, token, StringComparison.OrdinalIgnoreCase));

    private bool FolderHasChildren(string path)
    {
        path = SidebarItem.NormalizePath(path);
        return _folderPaths.Any(x => !string.Equals(SidebarItem.NormalizePath(x), path, StringComparison.OrdinalIgnoreCase) &&
                                     IsSameOrDescendant(x, path));
    }

    private bool ShouldShowFolderByExpansion(string key)
    {
        key = SidebarItem.NormalizePath(key);
        if (string.IsNullOrWhiteSpace(key)) return false;

        var scope = SidebarItem.GetScope(key);
        if (scope != "Custom" && IsSidebarNodeCollapsed(SystemRootToken(scope))) return false;

        foreach (var ancestor in AncestorFolderKeys(key))
        {
            if (string.Equals(ancestor, key, StringComparison.OrdinalIgnoreCase)) continue;
            if (IsSidebarNodeCollapsed(CustomRootToken(ancestor))) return false;
        }
        return true;
    }

    private void ToggleSidebarNode(SidebarItem entry)
    {
        if (!entry.HasChildren) return;
        var token = entry.IsSystemFolderRoot ? SystemRootToken(entry.Kind) : CustomRootToken(entry.Path);
        _settings.CollapsedSidebarNodes ??= new List<string>();

        if (IsSidebarNodeCollapsed(token))
            _settings.CollapsedSidebarNodes.RemoveAll(x => string.Equals(x, token, StringComparison.OrdinalIgnoreCase));
        else
            _settings.CollapsedSidebarNodes.Add(token);

        if (entry.IsSystemFolderRoot)
            RebuildFolderSidebar(null, entry.Kind);
        else
            RebuildFolderSidebar(entry.Path, null);
        QueueSave();
    }

    private void RebuildFolderSidebar(string? selectFolderPath = null, string? keepSystemKind = null)
    {
        if (string.IsNullOrWhiteSpace(selectFolderPath) && string.IsNullOrWhiteSpace(keepSystemKind) && CategoryList.SelectedItem is SidebarItem current)
        {
            if (current.IsCustomFolder) selectFolderPath = current.Path;
            else keepSystemKind = current.Kind;
        }

        NormalizeSidebarRootOrder();
        _rebuildingSidebar = true;
        try
        {
            _categories.Clear();
            _categories.Add(SidebarItem.System("History", "🕘", "歷史"));

            void AddSystemWithChildren(string kind, string icon, string name, string prefix)
            {
                var childKeys = _folderPaths.Where(x => SidebarItem.GetScopeRoot(x) == prefix).ToList();
                var hasChildren = childKeys.Count > 0;
                var expanded = !IsSidebarNodeCollapsed(SystemRootToken(kind));
                _categories.Add(SidebarItem.System(kind, icon, name, hasChildren, expanded));
                if (!expanded) return;

                foreach (var key in childKeys.Where(ShouldShowFolderByExpansion))
                    _categories.Add(SidebarItem.Folder(key, FolderHasChildren(key), !IsSidebarNodeCollapsed(CustomRootToken(key))));
            }

            void AddCustomRootWithChildren(string rootPath)
            {
                foreach (var key in _folderPaths.Where(x => SidebarItem.GetScope(x) == "Custom" &&
                                                             IsSameOrDescendant(x, rootPath) &&
                                                             ShouldShowFolderByExpansion(x)))
                {
                    _categories.Add(SidebarItem.Folder(key, FolderHasChildren(key), !IsSidebarNodeCollapsed(CustomRootToken(key))));
                }
            }

            foreach (var token in _settings.SidebarRootOrder)
            {
                if (TryGetSystemKindFromToken(token, out var kind))
                {
                    switch (kind)
                    {
                        case "Favorite": AddSystemWithChildren("Favorite", "⭐", "收藏", SidebarItem.FavoritePrefix); break;
                        case "Temporary": AddSystemWithChildren("Temporary", "⏱", "暫存", SidebarItem.TemporaryPrefix); break;
                        case "AI": AddSystemWithChildren("AI", "🤖", "AI 用語", SidebarItem.AiPrefix); break;
                        case "Quick": AddSystemWithChildren("Quick", "⚡", "快速用語", SidebarItem.QuickPrefix); break;
                    }
                }
                else if (TryGetFolderPathFromToken(token, out var rootPath) &&
                         SidebarItem.GetScope(rootPath) == "Custom" && string.IsNullOrWhiteSpace(ParentPath(rootPath)))
                {
                    AddCustomRootWithChildren(rootPath);
                }
            }

            // Recently Deleted is a fixed safety surface, not part of the draggable
            // classification order. Keeping it at the bottom prevents accidental reordering.
            _categories.Add(SidebarItem.System("Trash", "🗑", "最近刪除"));

            SidebarItem? selected = null;
            if (!string.IsNullOrWhiteSpace(selectFolderPath))
                selected = _categories.FirstOrDefault(x => x.IsCustomFolder && string.Equals(x.Path, selectFolderPath, StringComparison.OrdinalIgnoreCase));
            if (selected == null && !string.IsNullOrWhiteSpace(keepSystemKind))
                selected = _categories.FirstOrDefault(x => x.Kind == keepSystemKind);
            CategoryList.SelectedItem = selected ?? _categories.FirstOrDefault();
        }
        finally
        {
            _rebuildingSidebar = false;
        }
    }

    private ListBoxItem? GetCategoryContainer(DependencyObject? origin)
    {
        if (origin == null) return null;
        return ItemsControl.ContainerFromElement(CategoryList, origin) as ListBoxItem;
    }

    private void CategoryList_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var container = GetCategoryContainer(e.OriginalSource as DependencyObject);
        if (container?.Content is not SidebarItem entry) return;

        container.IsSelected = true;
        var menu = new ContextMenu();

        if (entry.Kind == "Trash")
        {
            var empty = new MenuItem { Header = "🗑  清空最近刪除", IsEnabled = _allItems.Any(x => x.IsDeleted && !x.IsPendingPurge) };
            empty.Click += (_, _) => EmptyTrash();
            menu.Items.Add(empty);
        }
        else if (entry.IsCustomFolder)
        {
            if (IsFileImportTarget(entry))
            {
                var addFiles = new MenuItem { Header = "＋  加入檔案到這裡…" };
                addFiles.Click += (_, _) => ImportFilesWithPickerToSection(entry);
                menu.Items.Add(addFiles);
            }

            var addChild = new MenuItem { Header = "＋  在此建立子資料夾…" };
            addChild.Click += (_, _) => CreateCategory(entry.Path);
            menu.Items.Add(addChild);

            var addSibling = new MenuItem { Header = "＋  建立同層資料夾…" };
            addSibling.Click += (_, _) => CreateCategory(ParentPath(entry.Path));
            menu.Items.Add(addSibling);
            menu.Items.Add(new Separator());

            var rename = new MenuItem { Header = "✎  重新命名…" };
            rename.Click += (_, _) => RenameCategory(entry);
            menu.Items.Add(rename);

            var parent = ParentPath(entry.Path);
            var siblings = _folderPaths.Where(x => string.Equals(ParentPath(x), parent, StringComparison.OrdinalIgnoreCase)).ToList();
            var siblingIndex = siblings.FindIndex(x => string.Equals(x, entry.Path, StringComparison.OrdinalIgnoreCase));

            var up = new MenuItem { Header = "↑  同層上移", IsEnabled = siblingIndex > 0 };
            up.Click += (_, _) => MoveCategory(entry, -1);
            menu.Items.Add(up);

            var down = new MenuItem { Header = "↓  同層下移", IsEnabled = siblingIndex >= 0 && siblingIndex < siblings.Count - 1 };
            down.Click += (_, _) => MoveCategory(entry, +1);
            menu.Items.Add(down);

            var scopeRoot = SidebarItem.GetScopeRoot(entry.Path);
            if (!string.IsNullOrWhiteSpace(parent) && !string.Equals(parent, scopeRoot, StringComparison.OrdinalIgnoreCase))
            {
                var outOne = new MenuItem { Header = "↰  移到上一層" };
                outOne.Click += (_, _) => MoveFolderByDrop(entry.Path, parent, FolderDropMode.After);
                menu.Items.Add(outOne);
            }
            else if (!string.IsNullOrWhiteSpace(scopeRoot) && string.Equals(parent, scopeRoot, StringComparison.OrdinalIgnoreCase))
            {
                // Already directly under its built-in root.
            }

            menu.Items.Add(new Separator());
            var delete = new MenuItem { Header = "🗑  刪除資料夾" };
            delete.Click += (_, _) => DeleteCategory(entry);
            menu.Items.Add(delete);
        }
        else
        {
            if (entry.Kind is "Favorite" or "Temporary" or "AI" or "Quick")
            {
                if (IsFileImportTarget(entry))
                {
                    var addFiles = new MenuItem { Header = "＋  加入檔案到這裡…" };
                    addFiles.Click += (_, _) => ImportFilesWithPickerToSection(entry);
                    menu.Items.Add(addFiles);
                }

                var addChild = new MenuItem { Header = "＋  在此建立子資料夾…" };
                var rootKey = RootKeyForSection(entry);
                addChild.Click += (_, _) => CreateCategory(rootKey);
                menu.Items.Add(addChild);
                menu.Items.Add(new Separator());
            }

            var add = new MenuItem { Header = "＋  新增根資料夾…" };
            add.Click += (_, _) => CreateCategory();
            menu.Items.Add(add);
        }

        container.ContextMenu = menu;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void FolderDragHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement handle || handle.Tag is not SidebarItem entry || !entry.CanDragSidebar) return;

        _dragCategoryItem = entry;
        _categoryDragStart = e.GetPosition(CategoryList);
        _categoryMouseDownAt = DateTime.UtcNow;
        ResetFolderHover();
        ClearCategoryDropIndicator();
        try { handle.CaptureMouse(); } catch { }
        e.Handled = true;
    }

    private void FolderDragHandle_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement handle || e.LeftButton != MouseButtonState.Pressed || _dragCategoryItem?.CanDragSidebar != true) return;
        if ((DateTime.UtcNow - _categoryMouseDownAt).TotalMilliseconds < FolderDragHoldMs) return;

        var pos = e.GetPosition(CategoryList);
        var dx = Math.Abs(pos.X - _categoryDragStart.X);
        var dy = Math.Abs(pos.Y - _categoryDragStart.Y);
        if (dx < Math.Max(6, SystemParameters.MinimumHorizontalDragDistance) &&
            dy < Math.Max(6, SystemParameters.MinimumVerticalDragDistance)) return;

        var entry = _dragCategoryItem!;
        _dragCategoryItem = null;
        try { handle.ReleaseMouseCapture(); } catch { }

        var data = new DataObject();
        data.SetData(CategoryDragFormat, SidebarDragPayload(entry));
        DragDrop.DoDragDrop(handle, data, DragDropEffects.Move);
        ResetFolderHover();
        ClearCategoryDropIndicator();
        e.Handled = true;
    }

    private void FolderDragHandle_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement handle)
        {
            try { handle.ReleaseMouseCapture(); } catch { }
        }
        _dragCategoryItem = null;
        ResetFolderHover();
        ClearCategoryDropIndicator();
        e.Handled = true;
    }

    private void CategoryList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _categoryDragStart = e.GetPosition(CategoryList);
        _categoryMouseDownAt = DateTime.UtcNow;
        var container = GetCategoryContainer(e.OriginalSource as DependencyObject);
        _dragCategoryItem = container?.Content as SidebarItem;
        if (_dragCategoryItem?.CanDragSidebar != true)
        {
            _dragCategoryItem = null;
            ReleaseCategoryMouseCapture();
            return;
        }

        // Keep receiving move events even when the pointer crosses other folder rows.
        // Without capture, upper/middle folders could lose the gesture while the last
        // row appeared to work because there was empty space beneath it.
        try
        {
            _categoryMouseCaptured = Mouse.Capture(CategoryList, CaptureMode.SubTree);
        }
        catch
        {
            _categoryMouseCaptured = false;
        }
    }

    private void CategoryList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        ReleaseCategoryMouseCapture();
        _dragCategoryItem = null;
        ResetFolderHover();
        ClearCategoryDropIndicator();

        if (e.OriginalSource is not DependencyObject source) return;
        // The drag handle remains drag-only. Clicking the row itself is what toggles children.
        if (FindVisualAncestor<Border>(source) is Border nearestBorder && nearestBorder.Name == "FolderDragHandle") return;

        var container = GetCategoryContainer(source);
        if (container?.Content is not SidebarItem entry || !entry.HasChildren) return;
        ToggleSidebarNode(entry);
        e.Handled = true;
    }

    private void CategoryList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragCategoryItem?.CanDragSidebar != true) return;

        // Deliberate rhythm: hold briefly + move a real distance.  Mouse capture makes
        // this reliable for every row instead of only the last visible folder.
        if ((DateTime.UtcNow - _categoryMouseDownAt).TotalMilliseconds < FolderDragHoldMs) return;
        var pos = e.GetPosition(CategoryList);
        var dx = Math.Abs(pos.X - _categoryDragStart.X);
        var dy = Math.Abs(pos.Y - _categoryDragStart.Y);
        if (dx < Math.Max(8, SystemParameters.MinimumHorizontalDragDistance * 2) &&
            dy < Math.Max(8, SystemParameters.MinimumVerticalDragDistance * 2))
            return;

        var entry = _dragCategoryItem!;
        var container = CategoryList.ItemContainerGenerator.ContainerFromItem(entry) as ListBoxItem;
        _dragCategoryItem = null;
        ReleaseCategoryMouseCapture();
        if (container == null) return;

        var data = new DataObject();
        data.SetData(CategoryDragFormat, SidebarDragPayload(entry));
        DragDrop.DoDragDrop(container, data, DragDropEffects.Move);
        ResetFolderHover();
        ClearCategoryDropIndicator();
    }

    private void ReleaseCategoryMouseCapture()
    {
        if (!_categoryMouseCaptured) return;
        _categoryMouseCaptured = false;
        try
        {
            if (Mouse.Captured == CategoryList || Mouse.Captured is DependencyObject)
                Mouse.Capture(null);
        }
        catch { }
    }

    private static bool IsClipDropTarget(SidebarItem? entry)
        => entry != null && (entry.Kind is "Favorite" or "Temporary" or "AI" or "Quick" or "Trash" || entry.IsCustomFolder);

    private static string FolderScopeForSection(SidebarItem? section)
    {
        if (section == null) return "";
        if (section.IsCustomFolder) return section.Scope;
        return section.Kind switch
        {
            "Favorite" => "Favorite",
            "Temporary" => "Temporary",
            "AI" => "AI",
            "Quick" => "Quick",
            _ => ""
        };
    }

    private static string DropTargetFolderKey(SidebarItem? section)
    {
        if (section == null) return "";
        if (section.IsCustomFolder) return section.Path;
        return SidebarItem.RootKeyForKind(section.Kind);
    }

    private FolderDropMode GetFolderDropMode(SidebarItem? target, ListBoxItem? targetContainer, DragEventArgs e, string draggedPath)
    {
        var draggedScope = SidebarItem.GetScope(draggedPath);
        var targetScope = FolderScopeForSection(target);

        if (target == null)
            return draggedScope == "Custom" ? FolderDropMode.RootEnd : FolderDropMode.None;

        // Never let a folder accidentally cross semantic roots (e.g. 收藏 -> AI 用語).
        // A deliberate clip can move between sections, but folder trees themselves stay scoped.
        if (!string.Equals(draggedScope, targetScope, StringComparison.Ordinal)) return FolderDropMode.None;

        if (!target.IsCustomFolder)
            return FolderDropMode.RootEnd; // drop on 收藏/暫存/AI/Quick root => move to root level

        if (IsSameOrDescendant(target.Path, draggedPath)) return FolderDropMode.None;
        if (targetContainer == null) return FolderDropMode.RootEnd;

        var p = e.GetPosition(targetContainer);
        var ratio = targetContainer.ActualHeight <= 0 ? 0.5 : p.Y / targetContainer.ActualHeight;
        // The outer thirds are dedicated reorder zones. The center is the only
        // nesting zone, so vertical sorting feels much more like Explorer.
        if (ratio < 0.34) return FolderDropMode.Before;
        if (ratio > 0.66) return FolderDropMode.After;
        return FolderDropMode.Inside;
    }

    private void CategoryList_DragOver(object sender, DragEventArgs e)
    {
        AutoScrollCategoryList(e.GetPosition(CategoryList));
        var targetContainer = GetCategoryContainer(e.OriginalSource as DependencyObject);
        var target = targetContainer?.Content as SidebarItem;

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            ResetFolderHover();
            ClearCategoryDropIndicator();
            if (targetContainer != null && IsFileImportTarget(target))
            {
                e.Effects = DragDropEffects.Copy;
                ShowClipDropTarget(targetContainer);
            }
            else
            {
                e.Effects = DragDropEffects.None;
                ClearClipDropTarget();
            }
            e.Handled = true;
            return;
        }

        if (e.Data.GetDataPresent(ClipDragFormat))
        {
            ResetFolderHover();
            ClearCategoryDropIndicator();
            if (targetContainer != null && IsClipDropTarget(target))
            {
                e.Effects = DragDropEffects.Move;
                ShowClipDropTarget(targetContainer);
            }
            else
            {
                e.Effects = DragDropEffects.None;
                ClearClipDropTarget();
            }
            e.Handled = true;
            return;
        }

        ClearClipDropTarget();
        if (!e.Data.GetDataPresent(CategoryDragFormat))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var payload = e.Data.GetData(CategoryDragFormat) as string ?? "";

        // Built-in folder roots (收藏 / 暫存 / AI 用語 / 快速用語) are real draggable
        // sidebar sections now. They reorder only at the root level and never get nested.
        if (TryGetSystemKindFromToken(payload, out var draggedSystemKind))
        {
            var draggedRootToken = SystemRootToken(draggedSystemKind);
            var targetRootToken = RootOrderTokenForEntry(target);
            if (targetContainer == null || string.IsNullOrWhiteSpace(targetRootToken) ||
                string.Equals(draggedRootToken, targetRootToken, StringComparison.OrdinalIgnoreCase))
            {
                e.Effects = DragDropEffects.None;
                ClearCategoryDropIndicator();
                e.Handled = true;
                return;
            }

            var p = e.GetPosition(targetContainer);
            var after = targetContainer.ActualHeight > 0 && p.Y >= targetContainer.ActualHeight / 2;
            ShowCategoryDropIndicator(targetContainer, after ? FolderDropMode.After : FolderDropMode.Before);
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }

        if (!TryGetFolderPathFromToken(payload, out var draggedPath) ||
            !_folderPaths.Contains(draggedPath, StringComparer.OrdinalIgnoreCase))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        // A top-level custom folder can be sorted around the built-in roots. This is a
        // pure sidebar-order operation; it does not change the folder's data semantics.
        if (string.IsNullOrWhiteSpace(ParentPath(draggedPath)) && target?.IsSystemFolderRoot == true && targetContainer != null)
        {
            var p = e.GetPosition(targetContainer);
            var after = targetContainer.ActualHeight > 0 && p.Y >= targetContainer.ActualHeight / 2;
            ShowCategoryDropIndicator(targetContainer, after ? FolderDropMode.After : FolderDropMode.Before);
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }

        var mode = GetFolderDropMode(target, targetContainer, e, draggedPath);
        if (mode == FolderDropMode.None)
        {
            e.Effects = DragDropEffects.None;
            ResetFolderHover();
            ClearCategoryDropIndicator();
            e.Handled = true;
            return;
        }

        var hoverTarget = target?.IsCustomFolder == true ? target.Path : target?.Kind ?? "<ROOT>";
        var hoverKey = $"{hoverTarget}|{mode}";
        if (!string.Equals(_folderHoverKey, hoverKey, StringComparison.Ordinal))
        {
            _folderHoverKey = hoverKey;
            _folderHoverSince = DateTime.UtcNow;
            _folderDropMode = mode;
        }

        var requiresDwell = mode == FolderDropMode.Inside;
        var dwellReady = !requiresDwell ||
                         (DateTime.UtcNow - _folderHoverSince).TotalMilliseconds >= FolderNestDwellMs;

        if (!dwellReady)
        {
            if (targetContainer != null && target?.IsCustomFolder == true)
                ShowCategoryNestArming(targetContainer);
            else
                ClearCategoryDropIndicator();
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Move;
        if (targetContainer != null)
            ShowCategoryDropIndicator(targetContainer, mode);
        else
            ClearCategoryDropIndicator();
        e.Handled = true;
    }

    private void CategoryList_DragLeave(object sender, DragEventArgs e)
    {
        if (!CategoryList.IsMouseOver)
        {
            ResetFolderHover();
            ClearCategoryDropIndicator();
            ClearClipDropTarget();
        }
    }

    private void CategoryList_Drop(object sender, DragEventArgs e)
    {
        var targetContainer = GetCategoryContainer(e.OriginalSource as DependencyObject);
        var target = targetContainer?.Content as SidebarItem;

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            ClearCategoryDropIndicator();
            ClearClipDropTarget();
            if (!IsFileImportTarget(target)) return;
            var files = (e.Data.GetData(DataFormats.FileDrop) as string[]) ?? Array.Empty<string>();
            ImportExternalFiles(files, FolderNameFromSection(target!));
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            return;
        }

        if (e.Data.GetDataPresent(ClipDragFormat))
        {
            ClearCategoryDropIndicator();
            ClearClipDropTarget();
            var idText = e.Data.GetData(ClipDragFormat) as string;
            if (!Guid.TryParse(idText, out var id) || !IsClipDropTarget(target)) return;
            var item = _allItems.FirstOrDefault(x => x.Id == id);
            if (item == null) return;

            ApplyClipDropToSection(item, target!);
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }

        ClearClipDropTarget();
        ClearCategoryDropIndicator();
        if (!e.Data.GetDataPresent(CategoryDragFormat)) return;
        var payload = e.Data.GetData(CategoryDragFormat) as string ?? "";

        if (TryGetSystemKindFromToken(payload, out var draggedSystemKind))
        {
            var targetToken = RootOrderTokenForEntry(target);
            if (targetContainer == null || string.IsNullOrWhiteSpace(targetToken)) return;
            var p = e.GetPosition(targetContainer);
            var after = targetContainer.ActualHeight > 0 && p.Y >= targetContainer.ActualHeight / 2;
            MoveSidebarRootByDrop(SystemRootToken(draggedSystemKind), targetToken!, after);
            ResetFolderHover();
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }

        if (!TryGetFolderPathFromToken(payload, out var draggedPath)) return;

        if (string.IsNullOrWhiteSpace(ParentPath(draggedPath)) && target?.IsSystemFolderRoot == true && targetContainer != null)
        {
            var p = e.GetPosition(targetContainer);
            var after = targetContainer.ActualHeight > 0 && p.Y >= targetContainer.ActualHeight / 2;
            MoveSidebarRootByDrop(CustomRootToken(draggedPath), SystemRootToken(target.Kind), after);
            ResetFolderHover();
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }

        var mode = GetFolderDropMode(target, targetContainer, e, draggedPath);
        var stable = mode != FolderDropMode.None &&
                     (mode != FolderDropMode.Inside ||
                      (mode == _folderDropMode &&
                       (DateTime.UtcNow - _folderHoverSince).TotalMilliseconds >= FolderNestDwellMs));
        if (!stable)
        {
            ResetFolderHover();
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        // Root custom folders use the root-order list for before/after sorting. The
        // center zone still means real nesting, preserving the deliberate folder model.
        if (string.IsNullOrWhiteSpace(ParentPath(draggedPath)) && target?.IsTopLevelCustomFolder == true &&
            mode is FolderDropMode.Before or FolderDropMode.After)
        {
            MoveSidebarRootByDrop(CustomRootToken(draggedPath), CustomRootToken(target.Path), mode == FolderDropMode.After);
        }
        else
        {
            MoveFolderByDrop(draggedPath, DropTargetFolderKey(target), mode);
        }

        ResetFolderHover();
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void ApplyClipDropToSection(ClipItem item, SidebarItem target)
    {
        if (item.IsDeleted || item.IsPendingPurge) return;
        if (target.Kind == "Trash")
        {
            SoftDeleteItems(new[] { item });
            return;
        }

        var fromHistory = CurrentSection().Kind == "History";
        var movedToClassification = false;

        // 收藏/暫存 are independent dimensions. Their child folders only organize those
        // dimensions and never overwrite the clip's classification Category.
        if (target.Kind == "Favorite")
        {
            item.Favorite = true;
            item.FavoriteFolder = "";
        }
        else if (target.Kind == "Temporary")
        {
            item.IsTemporary = true;
            item.TemporaryFolder = "";
        }
        else if (target.Kind == "AI") { item.Category = "AI 用語"; movedToClassification = true; }
        else if (target.Kind == "Quick") { item.Category = "快速用語"; movedToClassification = true; }
        else if (target.IsFavoriteFolder)
        {
            item.Favorite = true;
            item.FavoriteFolder = SidebarItem.GetRelativePath(target.Path);
        }
        else if (target.IsTemporaryFolder)
        {
            item.IsTemporary = true;
            item.TemporaryFolder = SidebarItem.GetRelativePath(target.Path);
        }
        else if (target.IsClassificationFolder)
        {
            item.Category = DataCategoryForFolderKey(target.Path);
            movedToClassification = true;
        }
        else return;

        item.LastUsedAt = DateTime.Now;
        if (fromHistory && movedToClassification) item.HistoryVisible = false;
        RefreshVisibleItems();
        QueueSave();
    }

    private void ShowClipDropTarget(ListBoxItem container)
    {
        if (_clipDropTargetContainer != container) ClearClipDropTarget();
        _clipDropTargetContainer = container;
        container.BorderBrush = new SolidColorBrush(Color.FromRgb(13, 148, 136));
        container.BorderThickness = new Thickness(2);
    }

    private void ClearClipDropTarget()
    {
        if (_clipDropTargetContainer == null) return;
        _clipDropTargetContainer.BorderBrush = Brushes.Transparent;
        _clipDropTargetContainer.BorderThickness = new Thickness(0);
        _clipDropTargetContainer = null;
    }

    private void ShowCategoryNestArming(ListBoxItem container)
    {
        if (_dropIndicatorContainer != container) ClearCategoryDropIndicator();
        _dropIndicatorContainer = container;
        container.BorderBrush = new SolidColorBrush(Color.FromRgb(94, 234, 212));
        container.BorderThickness = new Thickness(1);
    }

    private void ShowCategoryDropIndicator(ListBoxItem container, FolderDropMode mode)
    {
        if (_dropIndicatorContainer != container) ClearCategoryDropIndicator();
        _dropIndicatorContainer = container;
        container.BorderBrush = new SolidColorBrush(Color.FromRgb(13, 148, 136));
        container.BorderThickness = mode switch
        {
            FolderDropMode.Before => new Thickness(0, 3, 0, 0),
            FolderDropMode.After => new Thickness(0, 0, 0, 3),
            FolderDropMode.Inside => new Thickness(2.5),
            _ => new Thickness(0)
        };
    }

    private void ClearCategoryDropIndicator()
    {
        if (_dropIndicatorContainer == null) return;
        _dropIndicatorContainer.BorderBrush = Brushes.Transparent;
        _dropIndicatorContainer.BorderThickness = new Thickness(0);
        _dropIndicatorContainer = null;
    }

    private void ResetFolderHover()
    {
        _folderHoverKey = "";
        _folderHoverSince = DateTime.MinValue;
        _folderDropMode = FolderDropMode.None;
    }

    private void AutoScrollCategoryList(Point point)
    {
        var viewer = FindVisualChild<ScrollViewer>(CategoryList);
        if (viewer == null) return;
        if (point.Y < 28) viewer.LineUp();
        else if (point.Y > CategoryList.ActualHeight - 28) viewer.LineDown();
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) return found;
            var nested = FindVisualChild<T>(child);
            if (nested != null) return nested;
        }
        return null;
    }

    private static T? FindVisualAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        var current = source;
        while (current != null)
        {
            if (current is T found) return found;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private int IndexAfterSubtree(List<string> paths, string rootPath)
    {
        var last = -1;
        for (var i = 0; i < paths.Count; i++)
            if (IsSameOrDescendant(paths[i], rootPath)) last = i;
        return last < 0 ? paths.Count : last + 1;
    }

    private static string FolderKeyDisplayPath(string? key)
    {
        key = SidebarItem.NormalizePath(key);
        if (string.IsNullOrWhiteSpace(key)) return "根目錄";
        var scope = SidebarItem.GetScope(key);
        var relative = SidebarItem.GetRelativePath(key).Replace("/", " › ");
        return scope switch
        {
            "Favorite" => string.IsNullOrWhiteSpace(relative) ? "收藏" : $"收藏 › {relative}",
            "Temporary" => string.IsNullOrWhiteSpace(relative) ? "暫存" : $"暫存 › {relative}",
            "AI" => string.IsNullOrWhiteSpace(relative) ? "AI 用語" : $"AI 用語 › {relative}",
            "Quick" => string.IsNullOrWhiteSpace(relative) ? "快速用語" : $"快速用語 › {relative}",
            _ => key.Replace("/", " › ")
        };
    }

    private void RemapItemsForFolderMove(string oldKey, string newKey)
    {
        var scope = SidebarItem.GetScope(oldKey);
        var oldRel = SidebarItem.GetRelativePath(oldKey);
        var newRel = SidebarItem.GetRelativePath(newKey);

        if (scope == "Favorite")
        {
            foreach (var item in _allItems.Where(x => x.Favorite && IsSameOrDescendant(x.FavoriteFolder, oldRel)).ToList())
            {
                var current = SidebarItem.NormalizePath(item.FavoriteFolder);
                var suffix = current.Length == oldRel.Length ? "" : current[oldRel.Length..];
                item.FavoriteFolder = newRel + suffix;
            }
            return;
        }
        if (scope == "Temporary")
        {
            foreach (var item in _allItems.Where(x => x.IsTemporary && IsSameOrDescendant(x.TemporaryFolder, oldRel)).ToList())
            {
                var current = SidebarItem.NormalizePath(item.TemporaryFolder);
                var suffix = current.Length == oldRel.Length ? "" : current[oldRel.Length..];
                item.TemporaryFolder = newRel + suffix;
            }
            return;
        }

        var oldData = DataCategoryForFolderKey(oldKey);
        var newData = DataCategoryForFolderKey(newKey);
        foreach (var item in _allItems.Where(x => IsSameOrDescendant(x.Category, oldData)).ToList())
        {
            var current = SidebarItem.NormalizePath(item.Category);
            var suffix = current.Length == oldData.Length ? "" : current[oldData.Length..];
            item.Category = newData + suffix;
        }
    }

    private int CountItemsInFolderSubtree(string key)
    {
        var scope = SidebarItem.GetScope(key);
        var rel = SidebarItem.GetRelativePath(key);
        if (scope == "Favorite") return _allItems.Count(x => !x.IsDeleted && !x.IsPendingPurge && x.Favorite && IsSameOrDescendant(x.FavoriteFolder, rel));
        if (scope == "Temporary") return _allItems.Count(x => !x.IsDeleted && !x.IsPendingPurge && x.IsTemporary && IsSameOrDescendant(x.TemporaryFolder, rel));
        var data = DataCategoryForFolderKey(key);
        return _allItems.Count(x => !x.IsDeleted && !x.IsPendingPurge && IsSameOrDescendant(x.Category, data));
    }

    private void MoveFolderByDrop(string draggedPath, string targetPath, FolderDropMode mode)
    {
        draggedPath = SidebarItem.NormalizePath(draggedPath);
        targetPath = SidebarItem.NormalizePath(targetPath);
        if (!_folderPaths.Contains(draggedPath, StringComparer.OrdinalIgnoreCase)) return;

        var draggedScope = SidebarItem.GetScope(draggedPath);
        if (!string.IsNullOrWhiteSpace(targetPath))
        {
            var targetScope = SidebarItem.GetScope(targetPath);
            if (!string.Equals(draggedScope, targetScope, StringComparison.Ordinal)) return;
            if (IsSameOrDescendant(targetPath, draggedPath)) return;
        }

        var oldSubtree = _folderPaths.Where(x => IsSameOrDescendant(x, draggedPath)).ToList();
        var remaining = _folderPaths.Where(x => !IsSameOrDescendant(x, draggedPath)).ToList();
        var scopeRoot = SidebarItem.GetScopeRoot(draggedPath);

        var newParent = mode switch
        {
            FolderDropMode.Inside => targetPath,
            FolderDropMode.Before or FolderDropMode.After => ParentPath(targetPath),
            FolderDropMode.RootEnd => string.IsNullOrWhiteSpace(scopeRoot) ? "" : scopeRoot,
            _ => ParentPath(draggedPath)
        };
        var newRoot = CombineFolderPath(newParent, LeafName(draggedPath));

        if (!string.Equals(newRoot, draggedPath, StringComparison.OrdinalIgnoreCase) &&
            remaining.Contains(newRoot, StringComparer.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "目的位置已經有同名資料夾。", "ClipShelf", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var renamedSubtree = oldSubtree.Select(path =>
        {
            var suffix = path.Length == draggedPath.Length ? "" : path[draggedPath.Length..];
            return newRoot + suffix;
        }).ToList();

        var insertIndex = remaining.Count;
        if (mode == FolderDropMode.Before && !string.IsNullOrWhiteSpace(targetPath))
        {
            var idx = remaining.FindIndex(x => string.Equals(x, targetPath, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) insertIndex = idx;
        }
        else if (mode == FolderDropMode.After && !string.IsNullOrWhiteSpace(targetPath))
        {
            insertIndex = IndexAfterSubtree(remaining, targetPath);
        }
        else if (mode == FolderDropMode.Inside && !string.IsNullOrWhiteSpace(targetPath))
        {
            insertIndex = IndexAfterSubtree(remaining, targetPath);
        }

        insertIndex = Math.Max(0, Math.Min(insertIndex, remaining.Count));
        remaining.InsertRange(insertIndex, renamedSubtree);
        _folderPaths.Clear();
        _folderPaths.AddRange(remaining);
        NormalizeFolderOrder();

        if (!string.Equals(newRoot, draggedPath, StringComparison.OrdinalIgnoreCase))
            RemapItemsForFolderMove(draggedPath, newRoot);

        RebuildFolderSidebar(newRoot);
        RefreshVisibleItems();
        QueueSave();
    }

    private void MoveCategory(SidebarItem entry, int direction)
    {
        if (!entry.IsCustomFolder) return;
        var parent = ParentPath(entry.Path);
        var siblings = _folderPaths.Where(x => string.Equals(ParentPath(x), parent, StringComparison.OrdinalIgnoreCase)).ToList();
        var index = siblings.FindIndex(x => string.Equals(x, entry.Path, StringComparison.OrdinalIgnoreCase));
        var targetIndex = index + direction;
        if (index < 0 || targetIndex < 0 || targetIndex >= siblings.Count) return;
        MoveFolderByDrop(entry.Path, siblings[targetIndex], direction < 0 ? FolderDropMode.Before : FolderDropMode.After);
    }

    private void RenameCategory(SidebarItem entry)
    {
        if (!entry.IsCustomFolder) return;
        var oldPath = entry.Path;
        var dialog = new InputDialog("重新命名資料夾", "輸入新的資料夾名稱", LeafName(oldPath)) { Owner = this };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Value)) return;

        var newName = dialog.Value.Trim();
        if (newName.Contains('/') || newName.Contains('\\'))
        {
            MessageBox.Show(this, "資料夾名稱不能包含 / 或 \\\\。", "ClipShelf", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var newPath = CombineFolderPath(ParentPath(oldPath), newName);
        if (_folderPaths.Any(x => string.Equals(x, newPath, StringComparison.OrdinalIgnoreCase) && !IsSameOrDescendant(x, oldPath)))
        {
            MessageBox.Show(this, "同一層已經有同名資料夾。", "ClipShelf", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        for (var i = 0; i < _folderPaths.Count; i++)
        {
            if (!IsSameOrDescendant(_folderPaths[i], oldPath)) continue;
            var suffix = _folderPaths[i].Length == oldPath.Length ? "" : _folderPaths[i][oldPath.Length..];
            _folderPaths[i] = newPath + suffix;
        }
        RemapItemsForFolderMove(oldPath, newPath);

        NormalizeFolderOrder();
        RebuildFolderSidebar(newPath);
        RefreshVisibleItems();
        QueueSave();
    }

    private void DeleteCategory(SidebarItem entry)
    {
        if (!entry.IsCustomFolder) return;
        var path = entry.Path;
        var scope = SidebarItem.GetScope(path);
        var subfolders = _folderPaths.Count(x => !string.Equals(x, path, StringComparison.OrdinalIgnoreCase) && IsSameOrDescendant(x, path));
        var count = CountItemsInFolderSubtree(path);
        var parent = ParentPath(path);
        var scopeRoot = SidebarItem.GetScopeRoot(path);
        var destination = scope switch
        {
            "Favorite" => FolderKeyDisplayPath(parent),
            "Temporary" => FolderKeyDisplayPath(parent),
            "AI" => FolderKeyDisplayPath(parent),
            "Quick" => FolderKeyDisplayPath(parent),
            _ => string.IsNullOrWhiteSpace(parent) ? "一般歷史" : FolderKeyDisplayPath(parent)
        };
        var text = $"刪除資料夾「{FolderKeyDisplayPath(path)}」？\n\n" +
                   (subfolders > 0 ? $"其中包含 {subfolders} 個子資料夾。\n" : "") +
                   (count > 0 ? $"其中 {count} 筆內容不會刪除，會移到「{destination}」。" : "內容不會被永久刪除。 ");

        if (MessageBox.Show(this, text, "刪除資料夾", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        var parentRelative = SidebarItem.GetRelativePath(parent);
        if (scope == "Favorite")
        {
            var oldRel = SidebarItem.GetRelativePath(path);
            foreach (var item in _allItems.Where(x => x.Favorite && IsSameOrDescendant(x.FavoriteFolder, oldRel)).ToList())
                item.FavoriteFolder = parentRelative;
        }
        else if (scope == "Temporary")
        {
            var oldRel = SidebarItem.GetRelativePath(path);
            foreach (var item in _allItems.Where(x => x.IsTemporary && IsSameOrDescendant(x.TemporaryFolder, oldRel)).ToList())
                item.TemporaryFolder = parentRelative;
        }
        else
        {
            var oldData = DataCategoryForFolderKey(path);
            var parentData = scope switch
            {
                "AI" => CombineDataCategory("AI 用語", parentRelative),
                "Quick" => CombineDataCategory("快速用語", parentRelative),
                _ => parent
            };
            foreach (var item in _allItems.Where(x => IsSameOrDescendant(x.Category, oldData)).ToList())
            {
                item.Category = parentData;
                if (string.IsNullOrWhiteSpace(parentData)) item.HistoryVisible = true;
            }
        }

        _folderPaths.RemoveAll(x => IsSameOrDescendant(x, path));
        NormalizeFolderOrder();
        var keepKind = scope == "Custom" ? "History" : scope;
        RebuildFolderSidebar(null, keepKind);
        RefreshVisibleItems();
        QueueSave();
    }

    private void CreateCategory(string parentPath = "")
    {
        try
        {
            parentPath = SidebarItem.NormalizePath(parentPath);
            var subtitle = string.IsNullOrWhiteSpace(parentPath)
                ? "建立新的根資料夾"
                : $"建立在「{FolderKeyDisplayPath(parentPath)}」裡面";
            var dialog = new InputDialog("新增資料夾", subtitle) { Owner = this };
            if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Value)) return;

            var name = dialog.Value.Trim();
            if (name.Contains('/') || name.Contains('\\'))
            {
                MessageBox.Show(this, "資料夾名稱不能包含 / 或 \\。", "ClipShelf", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var path = CombineFolderPath(parentPath, name);
            var existing = _folderPaths.FirstOrDefault(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                RebuildFolderSidebar(existing);
                RefreshVisibleItems();
                return;
            }

            var insertIndex = string.IsNullOrWhiteSpace(parentPath) ? _folderPaths.Count : IndexAfterSubtree(_folderPaths, parentPath);
            _folderPaths.Insert(Math.Max(0, Math.Min(insertIndex, _folderPaths.Count)), path);
            NormalizeFolderOrder();
            RebuildFolderSidebar(path);
            RefreshVisibleItems();
            QueueSave();
        }
        catch (Exception ex)
        {
            // A folder operation should never be able to terminate the whole clipboard app.
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipShelf");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "error.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] CreateCategory\r\n{ex}\r\n\r\n", Encoding.UTF8);
            }
            catch { }
            MessageBox.Show(this, "新增資料夾時發生錯誤，但 ClipShelf 會繼續執行。\n錯誤紀錄：%LOCALAPPDATA%\\ClipShelf\\error.log",
                "ClipShelf", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void AddSubfolder_Click(object sender, RoutedEventArgs e)
    {
        var current = CurrentSection();
        if (current.IsCustomFolder) CreateCategory(current.Path);
        else if (current.Kind is "Favorite" or "Temporary" or "AI" or "Quick") CreateCategory(RootKeyForSection(current));
    }

    private static readonly HashSet<string> ImportableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".txt", ".pdf", ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp"
    };

    private static readonly HashSet<string> PreviewImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff"
    };

    private bool IsFileImportTarget(SidebarItem? entry)
        => entry != null && (entry.Kind is "AI" or "Quick" || entry.IsClassificationFolder);

    private void ImportFilesWithPickerToSection(SidebarItem entry)
    {
        var target = FolderNameFromSection(entry);
        if (!string.IsNullOrWhiteSpace(target)) ImportFilesWithPicker(target);
    }

    private string ResolveStorageRoot()
    {
        var configured = _settings.StorageDirectory?.Trim();
        var root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipShelf", "storage")
            : configured;
        Directory.CreateDirectory(root);
        return root;
    }

    private string GetImageStorageDirectory()
    {
        var path = Path.Combine(ResolveStorageRoot(), "images");
        Directory.CreateDirectory(path);
        return path;
    }

    private string GetFileStorageDirectory()
    {
        var path = Path.Combine(ResolveStorageRoot(), "files");
        Directory.CreateDirectory(path);
        return path;
    }

    private void OpenStorageDirectory()
    {
        try
        {
            var path = ResolveStorageRoot();
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"無法打開存放位置。\n\n{ex.Message}", "ClipShelf", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void OpenItemLocation(ClipItem item)
    {
        try
        {
            // Text clips live inside ClipShelf's history.json rather than as one file per
            // message, so selecting history.json is the truthful physical save location.
            if (item.Kind == "Text")
            {
                var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipShelf");
                Directory.CreateDirectory(dataDir);
                var historyPath = Path.Combine(dataDir, "history.json");
                if (File.Exists(historyPath))
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{historyPath}\"") { UseShellExecute = true });
                else
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dataDir}\"") { UseShellExecute = true });
                return;
            }

            string? path = item.Kind == "Image" ? item.ImagePath : item.Files.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(path))
            {
                OpenStorageDirectory();
                return;
            }
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            }
            else if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            }
            else
            {
                MessageBox.Show(this, "原檔案已不存在或被移動。", "ClipShelf", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch { }
    }

    private IEnumerable<string> GetImportTargetFolders()
        => GetAssignableCategories().Distinct(StringComparer.OrdinalIgnoreCase);

    private string? PickImportTarget(string initial = "")
    {
        var dialog = new FolderPickerDialog(GetImportTargetFolders(), initial) { Owner = this };
        _suspendAutoHide++;
        try
        {
            return dialog.ShowDialog() == true ? dialog.SelectedPath : null;
        }
        finally
        {
            _suspendAutoHide = Math.Max(0, _suspendAutoHide - 1);
        }
    }

    private string CurrentImportTargetOrPrompt()
    {
        var current = CurrentSection();
        if (IsFileImportTarget(current)) return FolderNameFromSection(current);
        return PickImportTarget() ?? "";
    }

    private void ImportFilesWithPicker(string targetCategory = "")
    {
        if (string.IsNullOrWhiteSpace(targetCategory))
        {
            targetCategory = CurrentImportTargetOrPrompt();
            if (string.IsNullOrWhiteSpace(targetCategory)) return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "加入檔案到 ClipShelf",
            Multiselect = true,
            CheckFileExists = true,
            Filter = "支援的檔案|*.md;*.txt;*.pdf;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp|文字 / 文件|*.md;*.txt;*.pdf|圖片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp|所有檔案|*.*"
        };

        _suspendAutoHide++;
        try
        {
            if (dialog.ShowDialog(this) == true)
                ImportExternalFiles(dialog.FileNames, targetCategory);
        }
        finally
        {
            _suspendAutoHide = Math.Max(0, _suspendAutoHide - 1);
        }
    }

    private void ImportExternalFiles(IEnumerable<string> sourcePaths, string targetCategory)
    {
        targetCategory = SidebarItem.NormalizePath(targetCategory);
        if (string.IsNullOrWhiteSpace(targetCategory)) return;

        var imported = 0;
        var skipped = new List<string>();
        foreach (var source in sourcePaths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var ext = Path.GetExtension(source);
            if (!ImportableExtensions.Contains(ext))
            {
                skipped.Add(Path.GetFileName(source));
                continue;
            }

            try
            {
                ImportOneManagedFile(source, targetCategory);
                imported++;
            }
            catch
            {
                skipped.Add(Path.GetFileName(source));
            }
        }

        SelectFolderSection(targetCategory);
        RefreshVisibleItems();
        QueueSave();

        if (skipped.Count > 0)
        {
            var names = string.Join("、", skipped.Take(5));
            if (skipped.Count > 5) names += $"…另 {skipped.Count - 5} 個";
            MessageBox.Show(this, $"已加入 {imported} 個檔案。\n\n以下檔案未加入：{names}", "ClipShelf", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void ImportOneManagedFile(string sourcePath, string targetCategory)
    {
        var ext = Path.GetExtension(sourcePath);
        var isPreviewImage = PreviewImageExtensions.Contains(ext);
        var hash = "I|" + Sha256File(sourcePath);
        var existing = _allItems.FirstOrDefault(x => !x.IsDeleted && !x.IsPendingPurge && x.ContentHash == hash && x.Kind == (isPreviewImage ? "Image" : "Files"));
        if (existing != null)
        {
            existing.Category = targetCategory; // real move; never duplicate the same imported file between folders
            Touch(existing, "ClipShelf", $"重新歸類 · {Path.GetFileName(sourcePath)}");
            return;
        }

        var destinationDir = isPreviewImage ? GetImageStorageDirectory() : GetFileStorageDirectory();
        var destination = UniqueDestination(destinationDir, Path.GetFileName(sourcePath));
        File.Copy(sourcePath, destination, false);

        if (isPreviewImage)
        {
            AddOrTouchItem(new ClipItem
            {
                Kind = "Image",
                ImagePath = destination,
                ContentHash = hash,
                SourceApp = "ClipShelf",
                SourceTitle = $"匯入 · {Path.GetFileName(sourcePath)}",
                Category = targetCategory,
                ManagedByClipShelf = true
            });
        }
        else
        {
            AddOrTouchItem(new ClipItem
            {
                Kind = "Files",
                Files = new List<string> { destination },
                ContentHash = hash,
                SourceApp = "ClipShelf",
                SourceTitle = $"匯入 · {Path.GetFileName(sourcePath)}",
                Category = targetCategory,
                ManagedByClipShelf = true
            });
        }
    }

    private static string UniqueDestination(string directory, string fileName)
    {
        var safeName = string.IsNullOrWhiteSpace(fileName) ? "file" : fileName;
        var candidate = Path.Combine(directory, safeName);
        if (!File.Exists(candidate)) return candidate;

        var stem = Path.GetFileNameWithoutExtension(safeName);
        var ext = Path.GetExtension(safeName);
        for (var i = 2; i < 10000; i++)
        {
            candidate = Path.Combine(directory, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
        return Path.Combine(directory, $"{stem}_{Guid.NewGuid():N}{ext}");
    }

    private static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    private void SelectFolderSection(string targetCategory)
    {
        SidebarItem? entry;
        if (string.Equals(targetCategory, "AI 用語", StringComparison.OrdinalIgnoreCase))
            entry = _categories.FirstOrDefault(x => x.Kind == "AI");
        else if (string.Equals(targetCategory, "快速用語", StringComparison.OrdinalIgnoreCase))
            entry = _categories.FirstOrDefault(x => x.Kind == "Quick");
        else if (targetCategory.StartsWith("AI 用語/", StringComparison.OrdinalIgnoreCase))
        {
            var rel = targetCategory["AI 用語/".Length..];
            var key = SidebarItem.MakeScopedPath("AI", rel);
            entry = _categories.FirstOrDefault(x => x.IsCustomFolder && string.Equals(x.Path, key, StringComparison.OrdinalIgnoreCase));
        }
        else if (targetCategory.StartsWith("快速用語/", StringComparison.OrdinalIgnoreCase))
        {
            var rel = targetCategory["快速用語/".Length..];
            var key = SidebarItem.MakeScopedPath("Quick", rel);
            entry = _categories.FirstOrDefault(x => x.IsCustomFolder && string.Equals(x.Path, key, StringComparison.OrdinalIgnoreCase));
        }
        else
            entry = _categories.FirstOrDefault(x => x.IsCustomFolder && string.Equals(x.Path, targetCategory, StringComparison.OrdinalIgnoreCase));

        if (entry != null) CategoryList.SelectedItem = entry;
    }

    private void AddFile_Click(object sender, RoutedEventArgs e)
        => ImportFilesWithPicker();

    private void HistoryList_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        // Even from History/Favorite/Temporary we accept the drop and then ask which
        // ClipShelf folder should receive the files.
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void HistoryList_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var current = CurrentSection();
        if (!IsFileImportTarget(current))
        {
            var target = PickImportTarget();
            if (string.IsNullOrWhiteSpace(target)) return;
            var files = (e.Data.GetData(DataFormats.FileDrop) as string[]) ?? Array.Empty<string>();
            ImportExternalFiles(files, target);
        }
        else
        {
            var files = (e.Data.GetData(DataFormats.FileDrop) as string[]) ?? Array.Empty<string>();
            ImportExternalFiles(files, FolderNameFromSection(current));
        }
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void AddText_Click(object sender, RoutedEventArgs e)
    {
        var custom = GetAssignableCategories().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var current = CurrentSection();
        var initialCategory = current.Kind == "AI" ? "AI 用語"
            : current.Kind == "Quick" ? "快速用語"
            : current.IsClassificationFolder ? DataCategoryForFolderKey(current.Path)
            : "";
        var initialFavorite = current.Kind == "Favorite" || current.IsFavoriteFolder;
        var initialTemporary = current.Kind == "Temporary" || current.IsTemporaryFolder;

        var dialog = new TextEditorDialog(custom, initialCategory, favorite: initialFavorite, temporary: initialTemporary) { Owner = this };
        _suspendAutoHide++;
        try
        {
            if (dialog.ShowDialog() == true)
            {
                var text = dialog.Value;
                var hash = Sha256("T|" + text);
                var existing = _allItems.FirstOrDefault(x => !x.IsDeleted && !x.IsPendingPurge && x.Kind == "Text" && x.ContentHash == hash);
                if (existing != null)
                {
                    existing.Title = dialog.ItemTitle; existing.Note = dialog.ItemNote;
                    existing.Favorite = existing.Favorite || dialog.Favorite;
                    existing.IsTemporary = existing.IsTemporary || dialog.Temporary;
                    if (current.Kind == "Favorite" && dialog.Favorite) existing.FavoriteFolder = "";
                    else if (current.IsFavoriteFolder && dialog.Favorite) existing.FavoriteFolder = SidebarItem.GetRelativePath(current.Path);
                    if (current.Kind == "Temporary" && dialog.Temporary) existing.TemporaryFolder = "";
                    else if (current.IsTemporaryFolder && dialog.Temporary) existing.TemporaryFolder = SidebarItem.GetRelativePath(current.Path);
                    if (!string.IsNullOrWhiteSpace(dialog.SelectedCategory)) existing.Category = dialog.SelectedCategory;
                    Touch(existing, "ClipShelf", "手動新增");
                }
                else
                {
                    AddOrTouchItem(new ClipItem
                    {
                        Kind = "Text",
                        Text = text,
                        Title = dialog.ItemTitle, Note = dialog.ItemNote,
                        ContentHash = hash,
                        SourceApp = "ClipShelf",
                        SourceTitle = "手動新增",
                        Category = dialog.SelectedCategory,
                        Favorite = dialog.Favorite,
                        IsTemporary = dialog.Temporary,
                        FavoriteFolder = current.IsFavoriteFolder && dialog.Favorite ? SidebarItem.GetRelativePath(current.Path) : "",
                        TemporaryFolder = current.IsTemporaryFolder && dialog.Temporary ? SidebarItem.GetRelativePath(current.Path) : ""
                    });
                }
            }
        }
        finally
        {
            _suspendAutoHide = Math.Max(0, _suspendAutoHide - 1);
        }
    }

    private void CaptureToggle_Click(object sender, RoutedEventArgs e) => ToggleCapturePause();

    private void ToggleCapturePause()
    {
        _settings.CaptureResumeAt = null;
        _capturePaused = !_capturePaused;
        _settings.CapturePaused = _capturePaused;
        UpdateCaptureUi();
        QueueSave();
        _store.SaveNow();
        _coreBridge.NotifyStateChanged();
    }

    private void UpdateCaptureUi()
    {
        if (CaptureToggleButton == null || CaptureStateText == null) return;
        if (_capturePaused)
        {
            CaptureToggleButton.Content = "繼續";
            CaptureToggleButton.ToolTip = "繼續自動保存";
            CaptureStateText.Text = "已暫停自動保存";
            CaptureStateText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(190, 24, 93));
        }
        else
        {
            CaptureToggleButton.Content = "暫停";
            CaptureToggleButton.ToolTip = "暫停自動保存";
            CaptureStateText.Text = "已自動儲存";
            CaptureStateText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(15, 118, 110));
        }
    }

    private void BackupMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button owner) return;
        var menu = new ContextMenu { PlacementTarget = owner };
        var export = new MenuItem { Header = "匯出完整備份…" };
        export.Click += async (_, _) => await ExportBackupAsync();
        var import = new MenuItem { Header = "匯入備份（先預掃描去重）…" };
        import.Click += async (_, _) => await ImportBackupAsync();
        menu.Items.Add(export);
        menu.Items.Add(import);
        menu.IsOpen = true;
    }

    private async Task ExportBackupAsync()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "匯出 ClipShelf 完整備份",
            Filter = "ClipShelf 備份|*.clipshelfbackup|所有檔案|*.*",
            DefaultExt = ".clipshelfbackup",
            AddExtension = true,
            FileName = $"ClipShelf_Backup_{DateTime.Now:yyyyMMdd_HHmmss}.clipshelfbackup"
        };

        _suspendAutoHide++;
        try
        {
            if (dialog.ShowDialog(this) != true) return;

            var itemSnapshot = _allItems.ToList();
            var folderSnapshot = _folderPaths.ToList();
            var settingsSnapshot = _settings.Clone();
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                var result = await Task.Run(() => BackupService.Export(dialog.FileName, itemSnapshot, folderSnapshot, settingsSnapshot));
                var warningText = result.Warnings.Count == 0
                    ? ""
                    : $"\n\n注意：有 {result.Warnings.Count} 個原始檔案／資料夾無法讀取，因此未完整放入備份。";
                MessageBox.Show(this,
                    $"完整備份已完成。\n\n項目：{result.ItemCount}\n檔案／資料夾內容：{result.PayloadCount}\n\n{dialog.FileName}{warningText}",
                    "ClipShelf 備份完成", MessageBoxButton.OK,
                    result.Warnings.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"匯出備份失敗。\n\n{ex.Message}\n\n原本的 ClipShelf 資料沒有被修改。",
                    "ClipShelf", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }
        finally
        {
            _suspendAutoHide = Math.Max(0, _suspendAutoHide - 1);
        }
    }

    private async Task ImportBackupAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "匯入 ClipShelf 備份",
            Filter = "ClipShelf 備份|*.clipshelfbackup|所有檔案|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        _suspendAutoHide++;
        try
        {
            if (dialog.ShowDialog(this) != true) return;

            var existingItemsSnapshot = _allItems.ToList();
            var existingFoldersSnapshot = _folderPaths.ToList();
            BackupService.ImportPreviewResult preview;

            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                // Preview phase is read-only. It validates the archive and hashes current/backup
                // content, but it does not change History and does not extract payloads to storage.
                preview = await Task.Run(() => BackupService.AnalyzeImport(
                    dialog.FileName,
                    existingItemsSnapshot,
                    existingFoldersSnapshot));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    $"無法讀取這份備份。\n\n{ex.Message}\n\n目前 ClipShelf 資料沒有被修改。",
                    "ClipShelf", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }

            if (preview.NewItemCount == 0 && preview.UncertainCount == 0 && preview.NewFolderCount == 0)
            {
                MessageBox.Show(this,
                    $"掃描完成，沒有需要匯入的新內容。\n\n" +
                    $"同 ID 已存在：{preview.DuplicateIdCount}\n" +
                    $"內容相同而跳過：{preview.DuplicateContentCount}",
                    "ClipShelf 匯入預覽", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var previewDialog = new ImportPreviewDialog(preview) { Owner = this };
            if (previewDialog.ShowDialog() != true) return;

            var selectedIds = preview.CertainNewItemIds.ToList();
            if (previewDialog.IncludeUncertain)
                selectedIds.AddRange(preview.UncertainItemIds);

            var existingIds = _allItems.Select(x => x.Id).ToList();
            var storageRoot = ResolveStorageRoot();
            var oldItems = _allItems.ToList();
            var oldFolders = _folderPaths.ToList();
            var oldSettings = _settings.Clone();
            BackupService.ImportResult? result = null;
            var committed = false;

            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                // Only the IDs explicitly approved by the preview are extracted. Duplicate IDs
                // and duplicate content are never materialized into storage in the default path.
                result = await Task.Run(() => BackupService.Import(
                    dialog.FileName,
                    storageRoot,
                    selectedIds,
                    existingIds));

                foreach (var folder in result.FolderPaths)
                {
                    var normalized = SidebarItem.NormalizePath(folder);
                    if (!string.IsNullOrWhiteSpace(normalized) &&
                        !_folderPaths.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                        _folderPaths.Add(normalized);
                }

                foreach (var token in result.SidebarRootOrder)
                {
                    if (!(_settings.SidebarRootOrder ??= new List<string>()).Contains(token, StringComparer.OrdinalIgnoreCase))
                        _settings.SidebarRootOrder.Add(token);
                }

                foreach (var item in result.Items.OrderByDescending(x => x.LastUsedAt))
                {
                    item.ShowSelector = _multiSelectMode;
                    _allItems.Add(item);
                }

                RecoverFolderPathsFromItems();
                NormalizeFolderOrder();
                RebuildFolderSidebar();
                RefreshVisibleItems();

                // Commit remains atomic at history.json. If this fails, memory state is restored
                // and this import's newly staged physical payload directory is discarded.
                QueueSave();
                _store.SaveNowChecked();
                committed = true;

                var uncertainSkipped = previewDialog.IncludeUncertain ? 0 : preview.UncertainCount;
                var warningText = result.Warnings.Count == 0
                    ? ""
                    : $"\n\n注意：{result.Warnings.Count} 個已選項目沒有可還原的實體內容，已略過。";
                var raceText = result.SkippedExisting == 0
                    ? ""
                    : $"\n匯入期間已存在而再次跳過：{result.SkippedExisting}";

                MessageBox.Show(this,
                    $"匯入完成。\n\n" +
                    $"新增項目：{result.Items.Count}\n" +
                    $"同 ID 已存在：{preview.DuplicateIdCount}\n" +
                    $"內容相同而跳過：{preview.DuplicateContentCount}\n" +
                    $"無法安全判定而未匯入：{uncertainSkipped}\n" +
                    $"新增資料夾／子資料夾：{preview.NewFolderCount}" +
                    $"{raceText}{warningText}",
                    "ClipShelf 匯入完成", MessageBoxButton.OK,
                    result.Warnings.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                if (!committed)
                {
                    _allItems.Clear();
                    foreach (var item in oldItems) _allItems.Add(item);

                    _folderPaths.Clear();
                    _folderPaths.AddRange(oldFolders);
                    _settings = oldSettings;

                    try
                    {
                        RebuildFolderSidebar();
                        RefreshVisibleItems();
                        QueueSave();
                        _store.SaveNow();
                    }
                    catch { }

                    BackupService.DiscardImportedPayload(result);
                }

                MessageBox.Show(this,
                    $"匯入失敗。\n\n{ex.Message}\n\n現有 ClipShelf 歷史不會被清空或替換。",
                    "ClipShelf", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }
        finally
        {
            _suspendAutoHide = Math.Max(0, _suspendAutoHide - 1);
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog(_settings, _hotkeyStatus) { Owner = this };
        _suspendAutoHide++;
        try
        {
            if (dialog.ShowDialog() == true)
            {
                _settings = dialog.Result;
                _settings.CapturePaused = _capturePaused;
                TrimHistory();
                RefreshVisibleItems();
                QueueSave();
                _store.SaveNow();
                _coreBridge.NotifyStateChanged();
            }
        }
        finally
        {
            _suspendAutoHide = Math.Max(0, _suspendAutoHide - 1);
        }
    }

    private void AddCategory_Click(object sender, RoutedEventArgs e) => CreateCategory();

    private bool RapidDeleteGuardActive()
        => DateTime.UtcNow < _suppressHistoryDoubleClickUntil;

    private void FavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        // If the row just reflowed after a trash click, ignore adjacent action buttons
        // briefly. The trash button itself remains live, so rapid delete is still possible.
        if (RapidDeleteGuardActive())
        {
            e.Handled = true;
            return;
        }

        if ((sender as Button)?.Tag is ClipItem item && !item.IsDeleted && !item.IsPendingPurge)
        {
            item.Favorite = !item.Favorite;
            if (!item.Favorite) item.FavoriteFolder = "";
            RefreshVisibleItems();
            QueueSave();
        }
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (RapidDeleteGuardActive())
        {
            e.Handled = true;
            return;
        }

        if ((sender as Button)?.Tag is ClipItem item) CopyItem(item);
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        // Keep rapid deletion fast, but disarm the ListBox double-click paste route while
        // rows are being removed/recycled under the pointer.
        _suppressHistoryDoubleClickUntil = DateTime.UtcNow.AddMilliseconds(750);
        if ((sender as Button)?.Tag is ClipItem item)
            RemoveFromCurrentSectionOrDelete(item);
        e.Handled = true;
    }

    private void RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is ClipItem item)
            RestoreTrashByIds(new[] { item.Id }, showFeedback: true);
        e.Handled = true;
    }

    private void PurgeButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is ClipItem item)
            RequestPermanentPurge(new[] { item });
        e.Handled = true;
    }

    private void EmptyTrashButton_Click(object sender, RoutedEventArgs e)
    {
        EmptyTrash();
        e.Handled = true;
    }

    private void ClipDragHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_multiSelectMode || sender is not FrameworkElement handle || handle.Tag is not ClipItem item || item.IsDeleted || item.IsPendingPurge) return;
        _dragClipItem = item;
        _itemDragStart = e.GetPosition(HistoryList);
        try { handle.CaptureMouse(); } catch { }
        e.Handled = true;
    }

    private void ClipDragHandle_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_multiSelectMode || sender is not FrameworkElement handle || e.LeftButton != MouseButtonState.Pressed || _dragClipItem == null) return;
        var pos = e.GetPosition(HistoryList);
        if (Math.Abs(pos.X - _itemDragStart.X) < Math.Max(6, SystemParameters.MinimumHorizontalDragDistance) &&
            Math.Abs(pos.Y - _itemDragStart.Y) < Math.Max(6, SystemParameters.MinimumVerticalDragDistance)) return;

        var item = _dragClipItem;
        _dragClipItem = null;
        try { handle.ReleaseMouseCapture(); } catch { }
        var data = new DataObject();
        data.SetData(ClipDragFormat, item.Id.ToString());
        DragDrop.DoDragDrop(handle, data, DragDropEffects.Move);
        ClearClipDropTarget();
        e.Handled = true;
    }

    private void ClipDragHandle_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement handle)
        {
            try { handle.ReleaseMouseCapture(); } catch { }
        }
        _dragClipItem = null;
        ClearClipDropTarget();
        e.Handled = true;
    }

    private void HistoryList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _itemDragStart = e.GetPosition(HistoryList);
        _dragClipItem = null;

        if (e.OriginalSource is not DependencyObject source) return;
        var targetRow = ItemsControl.ContainerFromElement(HistoryList, source) as ListBoxItem;
        if (_inlineEditItem is ClipItem editing && targetRow?.DataContext is ClipItem targetItem && !ReferenceEquals(editing, targetItem))
        {
            if (!ResolveInlineEdit()) { e.Handled = true; FocusInlineEditor(editing, editing.IsEditingTitle); return; }
        }
        if (FindVisualAncestor<Button>(source) != null || FindVisualAncestor<CheckBox>(source) != null) return;
        // The body text deliberately remains a native selectable TextBox. Ctrl/Shift inside
        // the text therefore belongs to text selection and never becomes card selection.
        if (FindVisualAncestor<TextBox>(source) is TextBox selectable && (selectable.Name == "ClipSelectableText" || IsInlineEditor(selectable))) return;
        var container = ItemsControl.ContainerFromElement(HistoryList, source) as ListBoxItem;
        if (container?.DataContext is not ClipItem item) return;

        var modifiers = Keyboard.Modifiers;
        var modifierSelect = modifiers.HasFlag(ModifierKeys.Control) || modifiers.HasFlag(ModifierKeys.Shift);

        // Explorer-style direct selection: Ctrl/Shift on card chrome/blank space enters
        // multiselect automatically. No separate "多選" button is required first.
        if (modifierSelect && !_multiSelectMode)
        {
            var previous = HistoryList.SelectedItem as ClipItem;
            EnterMultiSelectMode();
            if (modifiers.HasFlag(ModifierKeys.Shift) && previous != null && _visibleItems.Contains(previous))
                _multiSelectAnchor = previous;
        }

        if (_multiSelectMode)
        {
            HandleWindowsStyleMultiSelectClick(item, modifiers);
            e.Handled = true;
            return;
        }

        _dragClipItem = item;
    }

    private void HistoryList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragClipItem = null;
        ClearClipDropTarget();
    }

    private void HistoryList_MouseMove(object sender, MouseEventArgs e)
    {
        if (_multiSelectMode || e.LeftButton != MouseButtonState.Pressed || _dragClipItem == null) return;
        var pos = e.GetPosition(HistoryList);
        if (Math.Abs(pos.X - _itemDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _itemDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var item = _dragClipItem!;
        _dragClipItem = null;
        var data = new DataObject();
        data.SetData(ClipDragFormat, item.Id.ToString());
        DragDrop.DoDragDrop(HistoryList, data, DragDropEffects.Move);
        ClearClipDropTarget();
    }

    private void EnterMultiSelectMode()
    {
        if (_multiSelectMode) return;
        _multiSelectMode = true;
        foreach (var item in _allItems) item.ShowSelector = true;
        UpdateMultiSelectUi();
    }

    private void ExitMultiSelectMode()
    {
        if (!_multiSelectMode) return;
        _multiSelectMode = false;
        _multiSelectAnchor = null;
        ClearMarkedItems();
        foreach (var item in _allItems) item.ShowSelector = false;
        UpdateMultiSelectUi();
    }

    private void MultiSelectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_multiSelectMode) ExitMultiSelectMode();
        else
        {
            _multiSelectAnchor = HistoryList.SelectedItem as ClipItem;
            EnterMultiSelectMode();
        }
    }

    private void ExitMultiSelectButton_Click(object sender, RoutedEventArgs e)
        => ExitMultiSelectMode();

    private void ItemMark_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as CheckBox)?.Tag is ClipItem item)
            _multiSelectAnchor = item;
        UpdateMultiSelectUi();
        e.Handled = true;
    }

    private void HandleWindowsStyleMultiSelectClick(ClipItem item, ModifierKeys modifiers)
    {
        if (!_multiSelectMode) return;

        var ctrl = modifiers.HasFlag(ModifierKeys.Control);
        var shift = modifiers.HasFlag(ModifierKeys.Shift);

        if (shift)
        {
            var targetIndex = _visibleItems.IndexOf(item);
            var anchorIndex = _multiSelectAnchor == null ? -1 : _visibleItems.IndexOf(_multiSelectAnchor);
            if (targetIndex < 0) return;
            if (anchorIndex < 0) anchorIndex = targetIndex;

            // Windows-style: Shift replaces the current selection with a range;
            // Ctrl+Shift adds the range to what was already selected.
            if (!ctrl) ClearMarkedItems();
            var from = Math.Min(anchorIndex, targetIndex);
            var to = Math.Max(anchorIndex, targetIndex);
            for (var i = from; i <= to; i++) _visibleItems[i].IsMarked = true;
        }
        else if (ctrl)
        {
            // Ctrl-click toggles only this one item and keeps the rest.
            item.IsMarked = !item.IsMarked;
            _multiSelectAnchor = item;
        }
        else
        {
            // Plain click behaves like Explorer: one item becomes the selection.
            ClearMarkedItems();
            item.IsMarked = true;
            _multiSelectAnchor = item;
        }

        HistoryList.SelectedItem = item;
        HistoryList.ScrollIntoView(item);
        UpdateMultiSelectUi();
    }

    private static bool HasPersistentPlacement(ClipItem item)
    {
        return item.Favorite ||
               item.IsPinned ||
               item.IsTemporary ||
               !string.IsNullOrWhiteSpace(item.Category) ||
               !string.IsNullOrWhiteSpace(item.FavoriteFolder) ||
               !string.IsNullOrWhiteSpace(item.TemporaryFolder);
    }

    private static bool IsHistoryBulkDeleteProtected(ClipItem item) => HasPersistentPlacement(item);

    private IEnumerable<ClipItem> SelectAllCandidates()
    {
        var section = CurrentSection();
        // History entries are now independent from ownership; selecting all is safe
        // because organized items are only removed from History, not destroyed.
        return _visibleItems;
    }

    private void SelectAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_multiSelectMode) return;
        var candidates = SelectAllCandidates().ToList();
        var allSelected = candidates.Count > 0 && candidates.All(x => x.IsMarked);

        ClearMarkedItems();
        if (!allSelected)
            foreach (var item in candidates) item.IsMarked = true;

        _multiSelectAnchor = candidates.FirstOrDefault();
        UpdateMultiSelectUi();
    }

    private void DeleteSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = _visibleItems.Where(x => x.IsMarked).ToList();
        if (selected.Count == 0) return;

        var section = CurrentSection();
        if (section.Kind == "Trash")
        {
            RestoreTrashByIds(selected.Select(x => x.Id), showFeedback: true);
            ClearMarkedItems();
            _multiSelectAnchor = null;
            UpdateMultiSelectUi();
            return;
        }

        if (section.Kind == "History")
        {
            var linked = selected.Where(HasPersistentPlacement).ToList();
            var standalone = selected.Where(x => !HasPersistentPlacement(x)).ToList();

            var message = $"確定從歷史移除選取的 {selected.Count} 筆紀錄？";
            if (standalone.Count > 0)
                message += $"\n\n其中 {standalone.Count} 筆沒有其他保存位置，會移到「最近刪除」，不會立即永久刪除。";
            if (linked.Count > 0)
                message += $"\n{linked.Count} 筆已有原始資料位置，只會從歷史消失，原資料保留。";

            var result = MessageBox.Show(this, message, "清理歷史", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            foreach (var item in linked) item.HistoryVisible = false;
            SoftDeleteItems(standalone, refresh: false, showUndo: standalone.Count > 0);
        }
        else
        {
            foreach (var item in selected) RemoveFromCurrentSectionOrDelete(item, false);
        }

        ClearMarkedItems();
        _multiSelectAnchor = null;
        RefreshVisibleItems();
        QueueSave();
        UpdateMultiSelectUi();
    }

    private void PurgeSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentSection().Kind != "Trash") return;
        var selected = _visibleItems.Where(x => x.IsMarked && !x.IsPendingPurge).ToList();
        if (selected.Count == 0) return;
        RequestPermanentPurge(selected);
        ClearMarkedItems();
        _multiSelectAnchor = null;
        UpdateMultiSelectUi();
    }

    private void ClearMarkedItems()
    {
        foreach (var item in _allItems) item.IsMarked = false;
    }

    private void UpdateMultiSelectUi()
    {
        if (MultiSelectButton == null || SelectAllButton == null || DeleteSelectedButton == null || PurgeSelectedButton == null ||
            NormalToolbarPanel == null || SelectionToolbar == null || SelectedCountText == null) return;

        NormalToolbarPanel.Visibility = _multiSelectMode ? Visibility.Collapsed : Visibility.Visible;
        SelectionToolbar.Visibility = _multiSelectMode ? Visibility.Visible : Visibility.Collapsed;
        SelectAllButton.Visibility = _multiSelectMode ? Visibility.Visible : Visibility.Collapsed;

        var marked = _visibleItems.Count(x => x.IsMarked);
        SelectedCountText.Text = $"已選 {marked} 項";
        var section = CurrentSection();
        var candidates = SelectAllCandidates().ToList();
        var allSelected = candidates.Count > 0 && candidates.All(x => x.IsMarked);
        SelectAllButton.Content = allSelected ? "取消全選" : "全選";
        SelectAllButton.ToolTip = section.Kind == "History"
            ? "全選目前顯示的歷史紀錄；已整理資料只會移出歷史，不會刪除原資料"
            : section.Kind == "Trash"
                ? "全選目前顯示的最近刪除項目"
                : "全選目前畫面";

        DeleteSelectedButton.Visibility = _multiSelectMode ? Visibility.Visible : Visibility.Collapsed;
        PurgeSelectedButton.Visibility = _multiSelectMode && section.Kind == "Trash" ? Visibility.Visible : Visibility.Collapsed;

        if (section.Kind == "Trash")
        {
            DeleteSelectedButton.Content = $"↩ 復原 {marked}";
            DeleteSelectedButton.IsEnabled = marked > 0;
            var purgeableMarked = _visibleItems.Count(x => x.IsMarked && !x.IsPendingPurge);
            PurgeSelectedButton.Content = $"🗑 永久刪除 {purgeableMarked}";
            PurgeSelectedButton.IsEnabled = purgeableMarked > 0;
        }
        else if (section.Kind == "History")
        {
            var linkedMarked = _visibleItems.Count(x => x.IsMarked && HasPersistentPlacement(x));
            DeleteSelectedButton.Content = linkedMarked > 0
                ? $"🕘 移除歷史 {marked} · 原資料保留 {linkedMarked}"
                : $"🗑 移到最近刪除 {marked}";
            DeleteSelectedButton.IsEnabled = marked > 0;
        }
        else
        {
            DeleteSelectedButton.Content = section.Kind == "Favorite" ? $"☆ 移出收藏 {marked}"
                : section.Kind == "Temporary" ? $"⏱ 移出暫存 {marked}"
                : IsFolderSection(section) ? $"↩ 移出資料夾 {marked}"
                : $"🗑 移到最近刪除 {marked}";
            DeleteSelectedButton.IsEnabled = marked > 0;
        }
    }

    private void ClipSelectableText_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBox box || box.DataContext is not ClipItem item) return;
        if (item.Kind != "Text") return;
        _textPressBox = box;
        _textPressPoint = e.GetPosition(this);
        _textPressCanCopy = !item.IsInlineEditing && !item.IsDeleted && !item.IsPendingPurge &&
            !_multiSelectMode && e.ClickCount == 1 && Keyboard.Modifiers == ModifierKeys.None && box.SelectionLength == 0;
        if (item.IsEditingText) return;

        // Native selection starts in this same control. A plain click copies on
        // release; it must not also expand/reflow the card during the mouse press.
    }

    private void CollapseContentButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ClipItem item) return;
        CollapseExpandedItem(item);
        e.Handled = true;
    }

    private void CollapseContentBlankArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ClipItem item) return;

        // PreviewMouseLeftButtonDown already handles Ctrl/Shift card selection before this
        // bubbling handler runs.  In multiselect mode, blank-space clicks belong to selection
        // and must not collapse the text card as a side effect.
        if (_multiSelectMode)
        {
            e.Handled = true;
            return;
        }

        if (!item.IsContentExpanded) return;
        CollapseExpandedItem(item);
        e.Handled = true;
    }

    private void CollapseExpandedItem(ClipItem item)
    {
        if (item.IsEditingText) return;
        item.IsContentExpanded = false;

        // After collapsing a long internal text viewport, keep the source card visible.
        Dispatcher.BeginInvoke(new Action(() => HistoryList.ScrollIntoView(item)), DispatcherPriority.Loaded);
    }

    private void ClipSelectableText_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not TextBox box || box.DataContext is not ClipItem item || (!item.IsContentExpanded && !item.IsEditingText))
            return;

        var viewer = FindVisualChild<ScrollViewer>(box);
        if (viewer == null || viewer.ScrollableHeight <= 0)
            return;

        // Keep long-text reading controllable.  This only affects the expanded text box;
        // the outer history list keeps its normal wheel behaviour.
        var notches = Math.Max(1.0, Math.Abs(e.Delta) / 120.0);
        var direction = e.Delta > 0 ? -1.0 : 1.0;
        const double pixelsPerNotch = 30.0;
        var target = Math.Clamp(viewer.VerticalOffset + direction * pixelsPerNotch * notches, 0, viewer.ScrollableHeight);
        viewer.ScrollToVerticalOffset(target);
        e.Handled = true;
    }

    private void HistoryList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Expanded text owns an internal ScrollViewer.  Let it receive the wheel instead of
        // moving the outer card list; this is what makes native selection/autoscroll stable.
        if (FindVisualAncestor<TextBox>(e.OriginalSource as DependencyObject) is TextBox textBox &&
            textBox.Name == "ClipSelectableText" &&
            textBox.DataContext is ClipItem textItem && (textItem.IsContentExpanded || textItem.IsEditingText))
            return;

        // Keep the old gentle outer-list scrolling for expanded cards when the pointer is
        // outside the text body (for example over the card margin or action area).
        if (!_visibleItems.Any(x => x.IsContentExpanded)) return;

        var viewer = FindVisualChild<ScrollViewer>(HistoryList);
        if (viewer == null) return;

        var notches = Math.Max(1.0, Math.Abs(e.Delta) / 120.0);
        var direction = e.Delta > 0 ? -1.0 : 1.0;
        const double pixelsPerNotch = 36.0;
        var target = Math.Clamp(viewer.VerticalOffset + direction * pixelsPerNotch * notches, 0, viewer.ScrollableHeight);
        viewer.ScrollToVerticalOffset(target);
        e.Handled = true;
    }

    private void ClipSelectableText_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not TextBox box) return;
        if (IsInlineEditor(box)) return;
        if (e.Key == Key.C && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && box.SelectionLength > 0)
        {
            try
            {
                var selected = box.SelectedText;
                _ignoreClipboardUntil = DateTime.UtcNow.AddMilliseconds(700);
                Clipboard.SetText(selected);
                AddOrTouchItem(new ClipItem
                {
                    Kind = "Text",
                    Text = selected,
                    ContentHash = Sha256("T|" + selected),
                    SourceApp = "ClipShelf",
                    SourceTitle = "局部複製"
                });
            }
            catch { }
            e.Handled = true;
        }
    }

    private void ClipSelectableText_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is TextBox editor && IsInlineEditor(editor))
        {
            ShowInlineTextMenu(editor);
            e.Handled = true;
            return;
        }
        if (sender is not TextBox box || box.DataContext is not ClipItem item) return;
        if (_inlineEditItem is ClipItem editing && !ReferenceEquals(editing, item) && !ResolveInlineEdit())
        {
            e.Handled = true;
            FocusInlineEditor(editing, editing.IsEditingTitle);
            return;
        }
        if (box.SelectionLength <= 0)
        {
            HistoryList.SelectedItem = item;
            ShowMoreMenu(item, box);
            e.Handled = true;
            return;
        }

        var selected = box.SelectedText;
        var menu = new ContextMenu();
        var copySelection = new MenuItem { Header = "▣  複製選取文字" };
        copySelection.Click += (_, _) =>
        {
            try
            {
                _ignoreClipboardUntil = DateTime.UtcNow.AddMilliseconds(700);
                Clipboard.SetText(selected);
                AddOrTouchItem(new ClipItem
                {
                    Kind = "Text",
                    Text = selected,
                    ContentHash = Sha256("T|" + selected),
                    SourceApp = "ClipShelf",
                    SourceTitle = "局部複製"
                });
            }
            catch { }
        };
        menu.Items.Add(copySelection);

        if (item.Kind == "Text" && !item.IsDeleted && !item.IsPendingPurge)
        {
            var copyWhole = new MenuItem { Header = "▣  複製整筆文字" };
            copyWhole.Click += (_, _) => CopyItem(item);
            menu.Items.Add(copyWhole);

            var edit = new MenuItem { Header = "✎  編輯文字（原地）" };
            edit.Click += (_, _) => BeginInlineTextEdit(item);
            menu.Items.Add(new Separator());
            menu.Items.Add(edit);
        }

        _suspendAutoHide++;
        menu.Closed += (_, _) => _suspendAutoHide = Math.Max(0, _suspendAutoHide - 1);
        box.ContextMenu = menu;
        menu.PlacementTarget = box;
        TrackCardMenuFocus(menu);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (RapidDeleteGuardActive())
        {
            e.Handled = true;
            return;
        }

        if (sender is Button button && button.Tag is ClipItem item) ShowMoreMenu(item, button);
    }

    private void HistoryList_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_multiSelectMode) return;

        if (e.OriginalSource is not DependencyObject source) return;
        if (FindVisualAncestor<TextBox>(source) is TextBox selectable && (IsInlineEditor(selectable) || selectable.Name == "ClipSelectableText"))
            return;
        var container = ItemsControl.ContainerFromElement(HistoryList, source) as ListBoxItem;
        if (container?.DataContext is not ClipItem item) return;

        HistoryList.SelectedItem = item;
        if (_inlineEditItem is ClipItem editing && !ReferenceEquals(editing, item) && !ResolveInlineEdit())
        {
            e.Handled = true;
            FocusInlineEditor(editing, editing.IsEditingTitle);
            return;
        }
        ShowMoreMenu(item, container);
        e.Handled = true;
    }

    private async void HistoryList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DateTime.UtcNow < _suppressHistoryDoubleClickUntil)
        {
            e.Handled = true;
            return;
        }

        if (e.OriginalSource is DependencyObject source)
        {
            // Action controls are not card activation surfaces. Rapid clicks on trash/copy/
            // favorite/more must never bubble into the list's double-click paste command.
            if (FindVisualAncestor<Button>(source) != null || FindVisualAncestor<CheckBox>(source) != null)
            {
                e.Handled = true;
                return;
            }
            if (FindVisualAncestor<TextBox>(source) is TextBox selectable && (selectable.Name == "ClipSelectableText" || IsInlineEditor(selectable)))
                return;
        }

        if (_multiSelectMode || HistoryList.SelectedItem is not ClipItem item) return;
        if (item.IsDeleted)
            RestoreTrashByIds(new[] { item.Id }, showFeedback: true);
        else
            await PasteItemAsync(item);
    }

    private async void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // The Window preview handler runs before the TextBox. Route drafts first,
        // so Enter never pastes a card and Esc never clears the active search.
        if (Keyboard.FocusedElement is TextBox body && StartBodyEditForKey(body, e) && e.Handled) return;
        if (Keyboard.FocusedElement is TextBox inlineEditor && IsInlineEditor(inlineEditor))
        {
            InlineEditor_KeyDown(inlineEditor, e);
            return;
        }
        if (_searchDelay?.IsEnabled == true && (e.Key == Key.Enter || e.Key == Key.Up || e.Key == Key.Down)) { _searchDelay.Stop(); RefreshVisibleItems(); }
        var actualKey = e.Key == Key.System ? e.SystemKey : e.Key;
        if (actualKey == Key.Tab && (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) ||
                                     Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)))
            _keyboardSwitcherUntil = DateTime.UtcNow.AddMilliseconds(950);

        if (e.Key == Key.Escape && !string.IsNullOrWhiteSpace(SearchBox.Text))
        {
            SearchBox.Clear();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.F && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Delete && Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase) return;
        if (e.Key == Key.Delete)
        {
            if (CurrentSection().Kind == "Trash")
            {
                if (_multiSelectMode && _visibleItems.Any(x => x.IsMarked))
                    PurgeSelectedButton_Click(this, new RoutedEventArgs());
                else if (HistoryList.SelectedItem is ClipItem deletedItem)
                    RequestPermanentPurge(new[] { deletedItem });
            }
            else if (_multiSelectMode && _visibleItems.Any(x => x.IsMarked))
            {
                DeleteSelectedButton_Click(this, new RoutedEventArgs());
            }
            else if (HistoryList.SelectedItem is ClipItem deleteItem)
            {
                RemoveFromCurrentSectionOrDelete(deleteItem);
            }
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Enter && HistoryList.SelectedItem is ClipItem item)
        {
            if (_multiSelectMode)
            {
                item.IsMarked = !item.IsMarked;
                _multiSelectAnchor = item;
                UpdateMultiSelectUi();
            }
            else if (item.IsDeleted) RestoreTrashByIds(new[] { item.Id }, showFeedback: true);
            else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) CopyItem(item);
            else await PasteItemAsync(item);
            e.Handled = true;
            return;
        }
        if ((e.Key == Key.Down || e.Key == Key.Up) && SearchBox.IsKeyboardFocusWithin && _visibleItems.Count > 0)
        {
            var idx = HistoryList.SelectedIndex;
            idx = e.Key == Key.Down ? Math.Min(_visibleItems.Count - 1, idx + 1) : Math.Max(0, idx - 1);
            HistoryList.SelectedIndex = idx;
            HistoryList.ScrollIntoView(HistoryList.SelectedItem);
            e.Handled = true;
        }
    }

    private bool _pickerSyncQueued;
    private void AllItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // TouchExisting removes/reinserts one object in the same dispatcher turn.
        // Observe only the completed transaction, so picker order cannot jump.
        if (_pickerSyncQueued) return;
        _pickerSyncQueued = true;
        Dispatcher.BeginInvoke(new Action(() => { _pickerSyncQueued = false; _picker?.Synchronize(); }));
    }

    private static void SendCtrlV()
    {
        keybd_event(0x11, 0, 0, UIntPtr.Zero);      // Ctrl down
        keybd_event(0x56, 0, 0, UIntPtr.Zero);      // V down
        keybd_event(0x56, 0, 0x0002, UIntPtr.Zero); // V up
        keybd_event(0x11, 0, 0x0002, UIntPtr.Zero); // Ctrl up
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string? lpNewItem);
    [DllImport("user32.dll", SetLastError = true)] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", SetLastError = true)] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")] private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }
}
