using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ClipShelf;

public partial class QuickPickerWindow : Window
{
    private readonly QuickPickerModel _model = new();
    private readonly PickerSession _session = new();
    private readonly ObservableCollection<PickerRow> _rows = new();
    private readonly Func<IEnumerable<ClipItem>> _items;
    private readonly Func<AppSettings> _settings;
    private readonly Action _save;
    private readonly Action _manage;
    private readonly Func<IReadOnlyList<ClipItem>, bool, Task> _copy;
    private readonly Action<IReadOnlyList<ClipItem>> _delete;
    private readonly Action<ClipItem> _togglePin;
    private readonly Func<IEnumerable<string>> _folders;
    private int _ownedInteraction;
    private bool _refreshingScopes;
    private readonly DispatcherTimer _searchTimer;
    private bool _shuttingDown;
    private bool _restoringPlacement;
    private bool _opening;
    private bool _composing;
    internal bool IsBusy => _session.Busy;

    internal QuickPickerWindow(Func<IEnumerable<ClipItem>> items, Func<AppSettings> settings,
        Action save, Action manage, Func<IReadOnlyList<ClipItem>, bool, Task> copy,
        Action<IReadOnlyList<ClipItem>> delete, Action<ClipItem> togglePin, Func<IEnumerable<string>> folders)
    {
        InitializeComponent(); _items = items; _settings = settings; _save = save; _manage = manage; _copy = copy; _delete = delete; _togglePin = togglePin; _folders = folders;
        KeepOpen.IsChecked = !settings().PickerCloseOnDeactivate;
        Deactivated += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            // Do not steal focus back from an outside click; owned preview/menu/dropdown are internal interactions.
            if (PickerDismissal.ShouldDismiss(IsVisible, IsActive, IsBusy, _ownedInteraction > 0 || ScopeBox.IsDropDownOpen || Rows.ContextMenu.IsOpen, _settings().PickerCloseOnDeactivate))
                HidePicker(restoreTarget: false);
        }));
        Rows.ItemsSource = _rows;
        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); ApplySearch(); };
        Closing += (_, e) => { if (!_shuttingDown) { e.Cancel = true; HidePicker(); } };
        SizeChanged += (_, _) => PersistPlacement(); LocationChanged += (_, _) => PersistPlacement();
        TextCompositionManager.AddPreviewTextInputStartHandler(Search, (_, _) => _composing = true);
        TextCompositionManager.AddPreviewTextInputHandler(Search, (_, _) => _composing = false);
        RestorePlacement();
    }

    internal void Toggle(long target, int pid, long started)
    {
        if (IsBusy) return;
        if (IsVisible) { HidePicker(); return; }
        OpenPicker(target, pid, started);
    }
    internal void OpenPicker(long target, int pid, long started = 0)
    {
        if (IsBusy) return;
        _session.Open(target, pid); _opening = true;
        RefreshScopes(); _model.Open(_items()); Search.Clear(); ApplySearch(); _opening = false;
        Status.Text = Rows.Items.Count == 0 ? "目前沒有最近紀錄" : "選取內容後按 Enter 貼上";
        Show(); Activate(); Search.Focus();
        if (started > 0) Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            var elapsed = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
            PickerDiagnostics.Record(elapsed);
        }));
    }
    internal void HidePicker(bool restoreTarget = true)
    {
        if (IsBusy) return;
        _searchTimer.Stop(); PersistPlacement(); _session.Hide(); Hide();
        if (restoreTarget) PickerClipboard.RestoreTarget(new IntPtr(_session.Target), _session.TargetPid);
    }
    internal void ShutdownPicker() { _shuttingDown = true; _searchTimer.Stop(); Close(); }
    internal void Synchronize()
    {
        if (!IsVisible || IsBusy) return;
        RefreshScopes(); _model.Synchronize(_items()); ApplySearch();
    }
    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        if (_searchTimer == null || _opening) return;
        _searchTimer.Stop(); _searchTimer.Start();
    }
    private void ApplySearch()
    {
        var selectedIds = Rows.SelectedItems.Cast<PickerRow>().Select(r => r.Item.Id).ToHashSet();
        var previousIndex = Rows.SelectedIndex;
        var found = _model.Search(Search.Text);
        var ids = found.Select(i => i.Id).ToHashSet();
        for (var n = _rows.Count - 1; n >= 0; n--) if (!ids.Contains(_rows[n].Item.Id)) _rows.RemoveAt(n);
        for (var n = 0; n < found.Count; n++)
        {
            var item = found[n];
            var existing = _rows.FirstOrDefault(r => r.Item.Id == item.Id);
            if (existing == null) _rows.Insert(n, new PickerRow(item));
            else
            {
                var index = _rows.IndexOf(existing);
                if (index != n) _rows.Move(index, n);
                if (!ReferenceEquals(existing.Item, item)) _rows[n] = new PickerRow(item);
                else existing.Refresh();
            }
        }
        Rows.SelectedItems.Clear();
        foreach (var row in _rows.Where(r => selectedIds.Contains(r.Item.Id))) Rows.SelectedItems.Add(row);
        if (Rows.SelectedIndex < 0 && Rows.Items.Count > 0) Rows.SelectedIndex = Math.Clamp(previousIndex, 0, Rows.Items.Count - 1);
        Status.Text = found.Count == 0 ? "沒有符合的紀錄" : $"{found.Count} 筆" + (found.Count == 1000 ? "（最多顯示 1000 筆，輸入搜尋縮小範圍）" : "");
    }
    private async void Picker_KeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (_composing || key == Key.ImeProcessed) return;
        if (IsBusy) { e.Handled = true; return; }
        // Controls own their activation keys; selecting a folder or activating
        // Delete/Manage must never be interpreted as a clipboard paste.
        if (ScopeBox.IsDropDownOpen || (ScopeBox.IsKeyboardFocusWithin && key is Key.Up or Key.Down or Key.Enter) ||
            (key == Key.Enter && IsButtonSource(Keyboard.FocusedElement as DependencyObject))) return;
        if (key == Key.A && Keyboard.Modifiers == ModifierKeys.Control && Rows.IsKeyboardFocusWithin)
        { e.Handled = true; Rows.SelectAll(); return; }
        if (key == Key.Delete && !Search.IsKeyboardFocusWithin) { e.Handled = true; if (!e.IsRepeat) DeleteSelection(); return; }
        if (key == Key.Escape) { e.Handled = true; HidePicker(); return; }
        if (key == Key.Up || key == Key.Down || key == Key.Enter)
        {
            if (_searchTimer.IsEnabled) { _searchTimer.Stop(); ApplySearch(); }
            e.Handled = true;
            if (key == Key.Enter) { if (!e.IsRepeat) await Commit(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)); return; }
            if (Rows.Items.Count == 0) return;
            Rows.SelectedIndex = Math.Clamp(Rows.SelectedIndex + (key == Key.Down ? 1 : -1), 0, Rows.Items.Count - 1);
            Rows.ScrollIntoView(Rows.SelectedItem); return;
        }
        if (key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            if (Rows.SelectedItem is PickerRow row && _session.TryBeginPaste())
                try { await _copy(SelectedItems(), false); Status.Text = "已複製；沒有送出貼上"; }
                catch (Exception ex) { Status.Text = ex.Message; }
                finally { _session.EndPaste(); }
        }
        if (key == Key.F3 && Rows.SelectedItem is PickerRow selected)
        {
            e.Handled = true;
            var preview = new Window { Title = "內容預覽", Width = 520, Height = 420, Owner = this, ShowInTaskbar = false,
                Content = new TextBox { Text = selected.Item.DisplayText, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(12) } };
            _ownedInteraction++;
            try { preview.ShowDialog(); } finally { _ownedInteraction--; }
            Search.Focus();
        }
    }
    private void Picker_TextInput(object sender, TextCompositionEventArgs e)
    {
        if (Search.IsKeyboardFocusWithin || ScopeBox.IsKeyboardFocusWithin || IsButtonSource(e.OriginalSource as DependencyObject) || string.IsNullOrEmpty(e.Text) || IsBusy) return;
        Search.Focus(); Search.SelectedText = e.Text; Search.CaretIndex = Search.Text.Length; e.Handled = true;
    }
    private async void Rows_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Empty space / scrollbar double-click must never paste the selected row.
        if (Keyboard.Modifiers == ModifierKeys.None && !IsButtonSource(e.OriginalSource as DependencyObject) &&
            ItemsControl.ContainerFromElement(Rows, e.OriginalSource as DependencyObject) is ListBoxItem)
        { e.Handled = true; await Commit(false); }
    }
    private async Task Commit(bool plainText)
    {
        if (Rows.SelectedItem is not PickerRow row || !_session.TryBeginPaste()) return;
        try
        {
            if (!PickerClipboard.ValidTarget(new IntPtr(_session.Target), _session.TargetPid))
            { Status.Text = "原視窗已關閉或沒有可用目標；Ctrl+C 可只複製。"; return; }
            await _copy(SelectedItems(), plainText);
            Hide();
            var failure = await PickerClipboard.SendPasteAsync(new IntPtr(_session.Target), _session.TargetPid);
            if (failure != null) { Show(); Activate(); Search.Focus(); Status.Text = failure; }
            else _session.Hide();
        }
        catch (Exception ex) { if (!IsVisible) Show(); Status.Text = ex.Message; }
        finally { _session.EndPaste(); }
    }
    private IReadOnlyList<ClipItem> SelectedItems() => _rows.Where(r => Rows.SelectedItems.Contains(r)).Select(r => r.Item).ToList();
    private static bool IsButtonSource(DependencyObject? source)
    {
        while (source != null) {
            if (source is System.Windows.Controls.Primitives.ButtonBase) return true;
            source = source is System.Windows.Media.Visual || source is System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
        return false;
    }
    private void Rows_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BatchActions == null) return;
        var count = Rows.SelectedItems.Count;
        BatchActions.Visibility = count > 1 ? Visibility.Visible : Visibility.Collapsed;
        SelectionCount.Text = $"已選取 {count} 筆";
    }
    private void DeleteSelection() => DeleteItems(SelectedItems());
    private void Delete_MouseDown(object sender, MouseButtonEventArgs e) { if (e.ClickCount > 1 || IsBusy) e.Handled = true; }
    private void Rows_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (IsBusy) { e.Handled = true; return; }
        if (ItemsControl.ContainerFromElement(Rows, e.OriginalSource as DependencyObject) is ListBoxItem container && !container.IsSelected)
            Rows.SelectedItem = container.DataContext;
    }
    private void DeleteSelected_Click(object sender, RoutedEventArgs e) { e.Handled = true; DeleteSelection(); }
    private void PinRow_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (IsBusy || (sender as FrameworkElement)?.DataContext is not PickerRow row) return;
        try
        {
            _togglePin(row.Item);
            _model.ReorderPins();
            ApplySearch();
            var liveRow = _rows.FirstOrDefault(x => x.Item.Id == row.Item.Id);
            if (liveRow != null) Rows.ScrollIntoView(liveRow);
            Status.Text = row.Item.IsPinned ? "📌 已釘選置頂，再按圖釘可取消。" : "已取消釘選，恢復最近使用排序。";
        }
        catch (Exception ex) { Status.Text = "釘選未保存：" + ex.Message; }
    }
    private void DeleteRow_Click(object sender, RoutedEventArgs e)
    { e.Handled = true; if ((sender as FrameworkElement)?.DataContext is PickerRow row) DeleteItems(new[] { row.Item }); }
    private void DeleteItems(IReadOnlyList<ClipItem> items)
    {
        if (items.Count == 0 || !_session.TryBeginPaste()) return;
        try { _delete(items); _model.Synchronize(_items()); ApplySearch(); Status.Text = $"已將 {items.Count} 筆移到最近刪除，可從完整管理恢復。"; }
        catch (Exception ex) { Status.Text = ex.Message; }
        finally { _session.EndPaste(); }
    }
    private void RefreshScopes()
    {
        var scopes = PickerScope.Build(_folders());
        var current = scopes.FirstOrDefault(s => s.Key == _model.Scope.Key) ?? scopes[0];
        _refreshingScopes = true;
        ScopeBox.ItemsSource = scopes; ScopeBox.SelectedItem = current; _model.Scope = current;
        _refreshingScopes = false;
    }
    private void Scope_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingScopes || _opening || ScopeBox.SelectedItem is not PickerScope scope || _session.Busy) return;
        _model.Scope = scope; _model.Open(_items()); Rows.SelectedItems.Clear(); ApplySearch();
    }
    private void KeepOpen_Click(object sender, RoutedEventArgs e)
    { _settings().PickerCloseOnDeactivate = KeepOpen.IsChecked != true; _save(); }
    private void Manage_Click(object sender, RoutedEventArgs e) { if (IsBusy) return; HidePicker(); _manage(); }
    private void RestorePlacement()
    {
        _restoringPlacement = true;
        var s = _settings(); Width = Finite(s.PickerWidth, 440, 340, 900); Height = Finite(s.PickerHeight, 570, 320, 1000);
        Left = s.HasPickerPlacement && double.IsFinite(s.PickerLeft) ? s.PickerLeft : SystemParameters.WorkArea.Right - Width - 20;
        Top = s.HasPickerPlacement && double.IsFinite(s.PickerTop) ? s.PickerTop : SystemParameters.WorkArea.Bottom - Height - 20;
        // Convert each monitor's physical working area into this HWND's WPF DIPs.
        var handle = new WindowInteropHelper(this).EnsureHandle();
        var transform = HwndSource.FromHwnd(handle)?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
        var proposed = new Rect(Left, Top, Width, Height);
        var areas = System.Windows.Forms.Screen.AllScreens.Select(screen =>
        { var a = screen.WorkingArea; return new Rect(transform.Transform(new Point(a.Left, a.Top)), transform.Transform(new Point(a.Right, a.Bottom))); }).ToList();
        var area = areas.FirstOrDefault(a => a.Contains(new Point(Left + 30, Top + 15)));
        if (area.IsEmpty || area.Width == 0) area = areas[0];
        Width = Math.Min(Width, area.Width); Height = Math.Min(Height, area.Height);
        Left = Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width));
        Top = Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height));
        _restoringPlacement = false;
    }
    private static double Finite(double value, double fallback, double min, double max) => double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
    private void PersistPlacement()
    {
        if (_restoringPlacement || !IsVisible || WindowState != WindowState.Normal) return;
        var s = _settings(); s.PickerWidth = ActualWidth; s.PickerHeight = ActualHeight;
        s.PickerLeft = Left; s.PickerTop = Top; s.HasPickerPlacement = true; _save();
    }
}

internal sealed class PickerRow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Summary)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Detail)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PinOpacity)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PinTip)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Preview)));
    }
    public ClipItem Item { get; }
    public PickerRow(ClipItem item) => Item = item;
    public string Glyph => Item.Kind == "Image" ? "▧" : Item.Kind == "Files" ? "▤" : "≡";
    public string Summary { get { var text = string.IsNullOrWhiteSpace(Item.Title) ? Item.DisplayText : Item.Title; return text[..Math.Min(text.Length, 300)].Replace('\r', ' ').Replace('\n', ' '); } }
    public string Detail => (Item.IsPinned ? "📌 已釘選 · " : "") + (Item.Kind == "Files" ? $"檔案 · {Item.Files.Count} 個" : Item.Kind == "Image" ? "圖片" : $"文字 · {Item.Text.Length:N0} 字 · {Item.SourceApp}");
    public double PinOpacity => Item.IsPinned ? 1.0 : 0.42;
    public string PinTip => Item.IsPinned ? "取消釘選，恢復最近使用排序" : "釘選置頂，重啟後仍保留";
    public string Preview => Item.DisplayText[..Math.Min(Item.DisplayText.Length, 2000)];
    private BitmapImage? _thumbnail;
    private bool _loaded;
    public BitmapImage? Thumbnail
    {
        get
        {
            if (_loaded) return _thumbnail; _loaded = true;
            if (Item.Kind != "Image" || !File.Exists(Item.ImagePath)) return null;
            try { var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 56; image.UriSource = new Uri(Item.ImagePath!, UriKind.Absolute); image.EndInit(); image.Freeze(); return _thumbnail = image; }
            catch { return null; }
        }
    }
}

internal static class PickerDiagnostics
{
    public static void Record(double elapsed)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipShelf", "diagnostics");
            Directory.CreateDirectory(dir); var path = Path.Combine(dir, "picker-wake.csv");
            if (File.Exists(path) && new FileInfo(path).Length > 256000) File.Move(path, path + ".previous", true);
            using var p = Process.GetCurrentProcess();
            File.AppendAllText(path, $"{DateTime.UtcNow:O},2.15.5,{Environment.ProcessId},{elapsed.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},{p.WorkingSet64},{p.PrivateMemorySize64},{p.TotalProcessorTime.TotalMilliseconds.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}\n");
        }
        catch { }
    }
}
