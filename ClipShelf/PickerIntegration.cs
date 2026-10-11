using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using ClipShelfShared;

namespace ClipShelf;

public partial class MainWindow
{
    private string _hotkeyStatus = "快捷鍵由背景 Core 註冊；若衝突請從系統匣開啟並更改設定。";
    internal void StartUi(string[] args)
    {
        new WindowInteropHelper(this).EnsureHandle(); // Single Clipboard Listener owned by UI while Core yields.
        if (args.Contains("--resident")) return;
        if (args.Contains("--quick")) OpenPicker();
        else ActivateFromCore();
    }
    private void HandleUiRequest(UiRequest request)
    {
        if (_allowClose) return;
        if (request.Command == "hotkey-status") { _hotkeyStatus = request.Message ?? ""; return; }
        if (request.Command == "bubble-placement")
        { _settings.MiniLeft = request.Left; _settings.MiniTop = request.Top; _settings.HasMiniPlacement = true; QueueSave(); _store.SaveNowChecked(); _coreBridge.NotifyStateChanged(); return; }
        if (request.Command == "bubble-visible")
        {
            var before = _settings.FloatingIconEnabled;
            _settings.FloatingIconEnabled = request.Message == "true";
            try { QueueSave(); _store.SaveNowChecked(); }
            catch { _settings.FloatingIconEnabled = before; throw; }
            _coreBridge.NotifyStateChanged(); return;
        }
        if (request.Command == "hide") { CloseForTrueHide(); return; }
        if (request.Command == "quick" || request.Command == "toggle-quick")
        { OpenPicker(request.Command == "toggle-quick", request.Target, request.TargetPid, request.Started); return; }
        if (request.Command == "manage")
        {
            if (PickerClipboard.ValidTarget(new IntPtr(request.Target), request.TargetPid)) _previousForegroundWindow = new IntPtr(request.Target);
            ActivateFromCore();
        }
    }
    private void EnsurePicker()
    {
        _picker ??= new QuickPickerWindow(() => _allItems, () => _settings, QueueSave, ActivateFromCore, CopyForPickerAsync, DeleteForPicker, () => _folderPaths);
    }
    private void OpenPicker(bool toggle = false, long target = 0, int pid = 0, long started = 0)
    {
        if (_allowClose || _suspendAutoHide > 0 || _picker?.IsBusy == true) return;
        EnsurePicker();
        if (toggle && _picker!.IsVisible) { _picker.HidePicker(); return; }
        if (_picker!.IsVisible) { _picker.Activate(); return; }
        var handle = new IntPtr(target);
        if (!PickerClipboard.ValidTarget(handle, pid))
        {
            var foreground = PickerClipboard.GetForegroundWindow();
            PickerClipboard.GetWindowThreadProcessId(foreground, out var foregroundPid);
            if (PickerClipboard.ValidTarget(foreground, (int)foregroundPid)) { target = foreground.ToInt64(); pid = (int)foregroundPid; }
            else { target = _previousForegroundWindow.ToInt64(); PickerClipboard.GetWindowThreadProcessId(_previousForegroundWindow, out var savedPid); pid = (int)savedPid; }
        }
        // Switching windows preserves the manager's draft and selection exactly.
        _ignoreDeactivationUntil = DateTime.UtcNow.AddMilliseconds(400);
        Hide(); _shortcutHidden = true;
        _coreBridge.ShowBubble(); // Quick picker coexists with the no-activate floating icon.
        _picker.OpenPicker(target, pid, started);
    }
    private void QuickPicker_Click(object sender, RoutedEventArgs e) => OpenPicker();
    private void DeleteForPicker(System.Collections.Generic.IReadOnlyList<ClipItem> items)
    {
        if (items.Count == 0 || items.Any(x => !_allItems.Contains(x) || x.IsDeleted || x.IsPendingPurge))
            throw new InvalidOperationException("紀錄已變更，請重新選取。");
        if (items.Any(x => x.IsEditingText))
            throw new InvalidOperationException("選取內容正在完整管理中編輯，請先完成編輯。");
        // Reuse Safe Delete, without refreshing the hidden manager or erasing drafts.
        SoftDeleteItems(items, refresh: false, showUndo: false);
        if (CurrentSection().Kind == "Trash") RefreshVisibleItems();
        else
        {
            // Remove only the deleted containers; keep other cards, drafts,
            // scroll anchors and selected objects alive in the hidden manager.
            foreach (var item in items) _visibleItems.Remove(item);
            CountText.Text = $"{_visibleItems.Count:N0} 筆";
        }
        QueueSave();
    }
    private async Task CopyForPickerAsync(System.Collections.Generic.IReadOnlyList<ClipItem> items, bool plainText)
    {
        if (items.Count == 0 || items.Any(item => !_allItems.Contains(item) || item.IsDeleted || item.IsPendingPurge)) throw new InvalidOperationException("紀錄已移除；請重新開啟快捷選取。");
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (items.Count == 1) PickerClipboard.Write(items[0], plainText);
                else Clipboard.SetText(PickerSelection.CombineText(items));
                _lastInternalClipboardSequence = PickerClipboard.GetClipboardSequenceNumber();
                break;
            }
            catch (System.Runtime.InteropServices.COMException) when (attempt < 3) { await Task.Delay(25); }
        }
        foreach (var item in items) { item.UseCount++; item.LastUsedAt = DateTime.Now; item.HistoryVisible = true; }
        // No manager refresh; no mutation of picker order. Save failure is handled by
        // the existing store error path and never triggers an empty-state fallback.
        QueueSave();
    }
    private void ShutdownUi()
    {
        if (_picker?.IsBusy == true || !ResolveInlineEdit()) return;
        try { QueueSave(); _store.SaveNowChecked(); }
        catch (Exception ex) { MessageBox.Show(this, "尚未成功保存，已取消退出。\n" + ex.Message, "ClipShelf", MessageBoxButton.OK, MessageBoxImage.Error); return; }
        _allowClose = true; Close();
    }
}
