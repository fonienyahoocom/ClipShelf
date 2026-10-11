using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace ClipShelf;

public sealed class HistoryStore : IDisposable
{
    private readonly string _dataDir;
    private readonly string _jsonPath;
    private readonly System.Threading.Timer _saveTimer;
    private readonly object _sync = new();
    private IReadOnlyCollection<ClipItem> _pendingItems = Array.Empty<ClipItem>();
    private IReadOnlyCollection<string> _pendingCategories = Array.Empty<string>();
    private AppSettings _pendingSettings = new();

    public event Action<string>? SaveFailed;
    private string? _pendingJson;
    private readonly object _writeSync = new();
    public string ImageDirectory { get; }

    public HistoryStore()
    {
        _dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipShelf");
        ImageDirectory = Path.Combine(_dataDir, "images");
        _jsonPath = Path.Combine(_dataDir, "history.json");
        Directory.CreateDirectory(_dataDir);
        Directory.CreateDirectory(ImageDirectory);
        _saveTimer = new System.Threading.Timer(_ => SaveNow(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public (List<ClipItem> Items, List<string> Categories, AppSettings Settings) Load()
    {
        try
        {
            if (!File.Exists(_jsonPath)) return (new(), new(), new AppSettings());
            var json = ClipShelfShared.SafeState.Read(_jsonPath);
            var state = JsonSerializer.Deserialize<SavedState>(json) ?? new SavedState();
            return (state.Items ?? new(), state.Categories ?? new(), state.Settings ?? new AppSettings());
        }
        catch
        {
            throw new InvalidDataException("無法讀取剪貼庫資料，已保留原檔。請從備份恢復後再開啟。位置：" + _dataDir);
        }
    }

    public void QueueSave(IReadOnlyCollection<ClipItem> items, IReadOnlyCollection<string> categories, AppSettings settings)
    {
        lock (_sync)
        {
            _pendingItems = items.ToList();
            _pendingCategories = categories.ToList();
            _pendingSettings = settings.Clone();
            _pendingJson = JsonSerializer.Serialize(new SavedState { Items = _pendingItems.ToList(), Categories = _pendingCategories.ToList(), Settings = _pendingSettings });
            _saveTimer.Change(350, Timeout.Infinite);
        }
    }

    public void SaveNow()
    {
        try { SaveNowChecked(); }
        catch (Exception ex) { SaveFailed?.Invoke(ex.Message); }
    }

    // Used by operations such as backup import where success must not be reported
    // unless the merged state actually reached history.json. Existing background
    // saves keep the historical best-effort behavior through SaveNow().
    public void SaveNowChecked()
    {
        lock (_writeSync)
        {
            string? json;
            lock (_sync) { json = _pendingJson; _saveTimer.Change(Timeout.Infinite, Timeout.Infinite); }
            if (json == null) return;
            ClipShelfShared.SafeState.Write(_jsonPath, json);
        }
    }

    public void Dispose()
    {
        SaveNow();
        _saveTimer.Dispose();
    }

    private sealed class SavedState
    {
        public List<ClipItem>? Items { get; set; } = new();
        public List<string>? Categories { get; set; } = new();
        public AppSettings? Settings { get; set; } = new();
    }
}
