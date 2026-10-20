using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace ClipShelf;
public partial class MainWindow
{
    private DispatcherTimer? _searchDelay;
    private DispatcherTimer? _productTimer;
    private bool _attachmentIndexBusy;
    private bool _saveErrorShown;
    private readonly Dictionary<string, (DateTime Modified, long Length, string Text)> _textCache = new(StringComparer.OrdinalIgnoreCase);
    private void InitializeProductFeatures()
    {
        _store.SaveFailed += message => Dispatcher.BeginInvoke(new Action(() => {
            CaptureStateText.Text = "保存失敗 · 請檢查磁碟 / 權限";
            if (!_saveErrorShown) { _saveErrorShown = true; MessageBox.Show(this, "本次資料尚未成功保存。\n" + message, "ClipShelf", MessageBoxButton.OK, MessageBoxImage.Error); }
        }));
        _searchDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _searchDelay.Tick += (_, _) => { _searchDelay.Stop(); ClearMarkedItems(); RefreshVisibleItems(); };
        _productTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _productTimer.Tick += async (_, _) => {
            if (_capturePaused && _settings.CaptureResumeAt.HasValue && _settings.CaptureResumeAt <= DateTime.Now) {
                _settings.CaptureResumeAt = null; ToggleCapturePause();
            }
            if (IsVisible) await IndexAttachmentsAsync();
        };
        _productTimer.Start();
        Closed += (_, _) => { _productTimer.Stop(); _searchDelay.Stop(); };
        _ = IndexAttachmentsAsync();
    }
    private static string SearchText(ClipItem item) => string.Join("\n", item.Title, item.Note, item.Text,
        string.Join("\n", item.Files), item.AttachmentText, item.Category, item.FavoriteFolder, item.TemporaryFolder);
    private static string BuildExcerpt(ClipItem item, string query)
    {
        var content = SearchText(item);
        var token = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (token == null) return item.Note;
        var at = content.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        var start = Math.Max(0, at - 45);
        return (start > 0 ? "…" : "") + content.Substring(start, Math.Min(200, content.Length - start));
    }
    private async Task IndexAttachmentsAsync()
    {
        if (_attachmentIndexBusy) return;
        _attachmentIndexBusy = true;
        try {
            var jobs = _allItems.Where(x => x.Kind == "Files" && !x.IsDeleted).Select(x => (Item: x, Paths: x.Files.ToArray())).ToArray();
            var results = await Task.Run(() => jobs.Select(job => {
                var texts = new List<string>();
                foreach (var path in job.Paths) {
                    try {
                        if (!new[] { ".md", ".txt" }.Contains(Path.GetExtension(path).ToLowerInvariant())) continue;
                        var info = new FileInfo(path);
                        if (!info.Exists || info.Length > 16 * 1024 * 1024) continue;
                        if (!_textCache.TryGetValue(path, out var cached) || cached.Modified != info.LastWriteTimeUtc || cached.Length != info.Length) {
                            cached = (info.LastWriteTimeUtc, info.Length, File.ReadAllText(path)); _textCache[path] = cached;
                        }
                        texts.Add(cached.Text);
                    } catch { }
                }
                return (job.Item, Text: string.Join("\n\n", texts));
            }).ToArray());
            var changed = false;
            foreach (var result in results) { if (result.Item.AttachmentText != result.Text) changed = true; result.Item.AttachmentText = result.Text; }
            if (changed && !string.IsNullOrWhiteSpace(SearchBox.Text)) RefreshVisibleItems();
        } finally { _attachmentIndexBusy = false; }
    }
    private void ProductMenu_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        void Add(string text, Action action) { var m = new MenuItem { Header = text }; m.Click += (_, _) => action(); menu.Items.Add(m); }
        Add((_settings.GlobalSearch ? "✓ " : "") + "搜尋全部內容", () => { _settings.GlobalSearch = true; RefreshVisibleItems(); QueueSave(); });
        Add((!_settings.GlobalSearch ? "✓ " : "") + "只搜尋目前資料夾", () => { _settings.GlobalSearch = false; RefreshVisibleItems(); QueueSave(); });
        menu.Items.Add(new Separator());
        Add(_settings.CompactView ? "切換卡片檢視" : "切換精簡列表", () => SetCompactView(!_settings.CompactView));
        Add("閱讀所選內容…", () => { if (HistoryList.SelectedItem is ClipItem item) ReadItem(item); });
        Add("暫停擷取 10 分鐘", () => {
            if (!_capturePaused) ToggleCapturePause();
            _settings.CaptureResumeAt = DateTime.Now.AddMinutes(10); QueueSave(); _store.SaveNow(); _coreBridge.NotifyStateChanged();
            CaptureStateText.Text = "暫停至 " + _settings.CaptureResumeAt.Value.ToString("HH:mm");
        });
        Add("打開自動備份", () => {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipShelf", "auto-backups");
            Directory.CreateDirectory(dir); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dir) { UseShellExecute = true });
        });
        Add("關於 ClipShelf 2.15.9", () => MessageBox.Show(this, "ClipShelf 2.15.9\n本機剪貼庫 · 搜尋 · 整理 · 文字組合\n\nCtrl+~ 開啟 / 隱藏\nCtrl+F 搜尋，方向鍵選擇，Enter 貼上\n卡片原地編輯：Enter 換行，Ctrl+Enter 儲存，Esc 取消\n標題改名：Enter 儲存，Esc 取消\n文字詳細設定：Enter 儲存，Shift+Enter 換行\n模板欄位：{{欄位名稱}}\n\n自動備份：每小時檢查，保留最近 14 份。\n資料位置：%LOCALAPPDATA%\\ClipShelf", "關於 ClipShelf"));
        ShowProductMenu(menu, (FrameworkElement)sender);
    }
    private void ShowProductMenu(ContextMenu menu, FrameworkElement owner)
    {
        _suspendAutoHide++; menu.Closed += (_, _) => _suspendAutoHide = Math.Max(0, _suspendAutoHide - 1);
        menu.PlacementTarget = owner; menu.IsOpen = true;
    }
    private void BatchMenu_Click(object sender, RoutedEventArgs e)
    {
        var selected = _visibleItems.Where(x => x.IsMarked && !x.IsDeleted && !x.IsPendingPurge).ToList();
        var menu = new ContextMenu();
        var compose = new MenuItem { Header = "組合複製…", IsEnabled = selected.Count > 0 && selected.All(x => x.Kind == "Text") };
        compose.Click += (_, _) => {
            _suspendAutoHide++;
            try { new ComposeDialog(selected) { Owner = this }.ShowDialog(); }
            finally { _suspendAutoHide--; }
        }; menu.Items.Add(compose);
        var favorite = new MenuItem { Header = "全部加入收藏", IsEnabled = selected.Count > 0 };
        favorite.Click += (_, _) => { foreach (var item in selected) item.Favorite = true; RefreshVisibleItems(); QueueSave(); }; menu.Items.Add(favorite);
        var move = new MenuItem { Header = "移動到分類", IsEnabled = selected.Count > 0 };
        foreach (var category in GetAssignableCategories().Distinct()) {
            var target = category;
            var entry = new MenuItem { Header = target.Replace("/", " › ") };
            entry.Click += (_, _) => {
                foreach (var item in selected) { item.Category = target; if (CurrentSection().Kind == "History") item.HistoryVisible = false; }
                ClearMarkedItems(); RefreshVisibleItems(); QueueSave();
            }; move.Items.Add(entry);
        }
        menu.Items.Add(move); ShowProductMenu(menu, (FrameworkElement)sender);
    }
    private void ReadItem(ClipItem item)
    {
        var content = item.Kind == "Text" ? item.Text : item.AttachmentText;
        if (string.IsNullOrEmpty(content)) {
            MessageBox.Show(this, "這筆內容沒有可閱讀的文字。MD / TXT 會在背景載入；PDF 與圖片仍以檔案方式開啟。", "ClipShelf"); return;
        }
        _suspendAutoHide++;
        try {
            var dialog = new ReaderDialog(item.Title, content, SearchBox.Text, _settings.ReadingFontSize) { Owner = this };
            dialog.ShowDialog(); _settings.ReadingFontSize = dialog.ReadingSize; QueueSave();
        } finally { _suspendAutoHide--; }
    }
}

internal sealed class ReaderDialog : Window
{
    public double ReadingSize { get; private set; }
    private readonly RichTextBox _reader = new() { IsReadOnly = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, BorderThickness = new Thickness(0), Padding = new Thickness(20) };
    private string _text;
    private readonly string _query;
    public ReaderDialog(string title, string text, string query, double size)
    {
        Title = string.IsNullOrWhiteSpace(title) ? "閱讀內容" : title;
        Width = 850; Height = 650; MinWidth = 480; MinHeight = 360; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ReadingSize = Math.Clamp(size, 12, 32); _text = text; _query = query;
        var root = new DockPanel(); var bar = new WrapPanel { Margin = new Thickness(12) }; DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar);
        void Button(string label, Action action) { var b = new Button { Content = label, Padding = new Thickness(12,6,12,6), Margin = new Thickness(4) }; b.Click += (_, _) => action(); bar.Children.Add(b); }
        Button("字小", () => { ReadingSize = Math.Max(12, ReadingSize - 2); Render(); });
        Button("字大", () => { ReadingSize = Math.Min(32, ReadingSize + 2); Render(); });
        Button("複製全文", () => { try { Clipboard.SetText(_text); } catch (Exception ex) { MessageBox.Show(this, ex.Message); } });
        Button("填寫模板…", FillTemplate);
        Button("匯出 TXT / MD…", () => {
            var save = new Microsoft.Win32.SaveFileDialog { Filter = "文字檔|*.txt|Markdown|*.md", FileName = "ClipShelf文字" };
            if (save.ShowDialog(this) == true) try { File.WriteAllText(save.FileName, _text); } catch (Exception ex) { MessageBox.Show(this, ex.Message); }
        });
        root.Children.Add(_reader); Content = root; Render();
    }
    private void Render()
    {
        var paragraph = new Paragraph { LineHeight = ReadingSize * 1.7 };
        var tokens = _query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) paragraph.Inlines.Add(new Run(_text));
        else {
            var regex = new Regex(string.Join("|", tokens.Select(Regex.Escape)), RegexOptions.IgnoreCase);
            int pos = 0;
            foreach (Match match in regex.Matches(_text)) {
                paragraph.Inlines.Add(new Run(_text.Substring(pos, match.Index-pos)));
                paragraph.Inlines.Add(new Run(match.Value) { Background = Brushes.PaleGoldenrod }); pos = match.Index + match.Length;
            }
            paragraph.Inlines.Add(new Run(_text[pos..]));
        }
        _reader.Document = new FlowDocument(paragraph) { FontSize = ReadingSize, PagePadding = new Thickness(10), FontFamily = new FontFamily("Microsoft JhengHei UI") };
    }
    private void FillTemplate()
    {
        var fields = Regex.Matches(_text, @"\{\{([^{}\r\n]+)\}\}").Select(x => x.Groups[1].Value).Distinct().ToList();
        if (fields.Count == 0) { MessageBox.Show(this, "用 {{欄位名稱}} 標記要填寫的地方，例如 {{語言}}。原本收藏的模板不會被修改。", "填空模板"); return; }
        var dialog = new Window { Title = "填寫模板", Width = 540, Height = 460, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var root = new DockPanel { Margin = new Thickness(16) }; var fieldsPanel = new StackPanel(); var boxes = new Dictionary<string,TextBox>();
        var done = new Button { Content = "套用到閱讀預覽", Padding = new Thickness(12), Margin = new Thickness(0,10,0,0) }; DockPanel.SetDock(done,Dock.Bottom); root.Children.Add(done);
        foreach (var field in fields) { fieldsPanel.Children.Add(new TextBlock { Text = field, Margin = new Thickness(0,8,0,4) }); var box = new TextBox { AcceptsReturn = true, MinHeight = 36, MaxHeight = 140, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, TextWrapping = TextWrapping.Wrap }; fieldsPanel.Children.Add(box); boxes[field] = box; }
        root.Children.Add(new ScrollViewer { Content = fieldsPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); dialog.Content = root;
        done.Click += (_,_) => { _text = Regex.Replace(_text, @"\{\{([^{}\r\n]+)\}\}", m => boxes[m.Groups[1].Value].Text); dialog.DialogResult = true; };
        if (dialog.ShowDialog() == true) Render();
    }
}
internal sealed class ComposeDialog : Window
{
    public ComposeDialog(List<ClipItem> items)
    {
        Title = "組合複製"; Width = 850; Height = 620; MinWidth = 550; MinHeight = 400; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var ordered = items.ToList(); var root = new DockPanel { Margin = new Thickness(16) };
        var bar = new WrapPanel(); DockPanel.SetDock(bar,Dock.Top); root.Children.Add(bar);
        var list = new ListBox { Width = 230, Margin = new Thickness(0,10,12,0), DisplayMemberPath = "Label" }; DockPanel.SetDock(list,Dock.Left); root.Children.Add(list);
        var preview = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 15, Padding = new Thickness(12), Margin = new Thickness(0,10,0,0) }; root.Children.Add(preview);
        var separator = new ComboBox { Width = 110, Margin = new Thickness(4), ItemsSource = new[] { "空行分隔", "分隔線", "換行分隔" }, SelectedIndex = 0 }; bar.Children.Add(separator);
        var warning = new TextBlock { Text = "調整順序或分隔會重建預覽；原文保持不變", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8) };
        void Refresh() { var index = list.SelectedIndex; list.ItemsSource = ordered.Select(x => new { Label = string.IsNullOrWhiteSpace(x.Title) ? x.Text.Replace("\n"," ")[..Math.Min(60,x.Text.Length)] : x.Title }).ToList(); list.SelectedIndex = Math.Clamp(index,0,ordered.Count-1); preview.Text = string.Join(separator.SelectedIndex == 1 ? "\n\n---\n\n" : separator.SelectedIndex == 2 ? "\n" : "\n\n", ordered.Select(x=>x.Text)); }
        void Button(string label, Action action) { var b = new Button { Content = label, Padding = new Thickness(10,6,10,6), Margin = new Thickness(4) }; b.Click += (_,_) => action(); bar.Children.Add(b); }
        void Move(int delta) { var i = list.SelectedIndex; var j = i + delta; if (i<0 || j<0 || j>=ordered.Count) return; (ordered[i],ordered[j])=(ordered[j],ordered[i]); Refresh(); list.SelectedIndex=j; }
        Button("上移",()=>Move(-1)); Button("下移",()=>Move(1));
        Button("複製組合",()=> { try { if(!string.IsNullOrEmpty(preview.Text)) Clipboard.SetText(preview.Text); Title = "組合複製 · 已複製"; } catch(Exception ex) { MessageBox.Show(this,ex.Message); } });
        bar.Children.Add(warning); separator.SelectionChanged += (_,_)=>Refresh(); Content=root; Refresh();
    }
}
