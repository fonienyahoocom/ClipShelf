using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClipShelfCore;

internal sealed class CoreStateStore
{
    private readonly object _sync = new();
    private readonly string _dataDir;
    private readonly string _historyPath;
    private JsonObject _state = new();

    public CoreStateStore(string? dataDirectory = null)
    {
        _dataDir = dataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipShelf");
        _historyPath = Path.Combine(_dataDir, "history.json");
        Directory.CreateDirectory(_dataDir);
        Reload();

    }

    public string DataDirectory => _dataDir;

    public void Reload()
    {
        lock (_sync)
        {
            _state = LoadStateFromDisk();
            EnsureShape(_state);
            if (MigrateHistoryModelLocked()) SaveLocked();
        }
    }

    public bool CapturePaused
    {
        get { lock (_sync) {
            var until = GetDateTime(Settings, "CaptureResumeAt");
            if (GetBool(Settings, "CapturePaused", false) && until != DateTime.MinValue && until <= DateTime.Now) {
                Settings["CapturePaused"] = false; Settings["CaptureResumeAt"] = null; SaveLocked();
            }
            return GetBool(Settings, "CapturePaused", false);
        } }
    }

    public (uint Modifiers, uint Key) PickerHotkey
    {
        get { lock (_sync) {
            var m = (uint)GetDouble(Settings, "PickerHotkeyModifiers", 2);
            var k = (uint)GetDouble(Settings, "PickerHotkeyKey", 0xC0);
            return (m, k) is (2, 0xC0) or (3, 0x56) or (3, 0x51) ? (m, k) : (2u, 0xC0u);
        } }
    }

    public bool CaptureText
    {
        get { lock (_sync) return GetBool(Settings, "CaptureText", true); }
    }

    public bool CaptureImages
    {
        get { lock (_sync) return GetBool(Settings, "CaptureImages", true); }
    }

    public bool CaptureFiles
    {
        get { lock (_sync) return GetBool(Settings, "CaptureFiles", true); }
    }

    public bool FloatingIconEnabled { get { lock (_sync) return GetBool(Settings, "FloatingIconEnabled", true); } }
    public void SetFloatingIconEnabled(bool value)
    { lock (_sync) { Settings["FloatingIconEnabled"] = value; SaveLocked(); } }

    public bool PreferQuickPicker
    {
        get { lock (_sync) return GetBool(Settings, "PreferQuickPicker", true); }
    }

    public bool SwitchNonInterferenceMode
    {
        get { lock (_sync) return GetBool(Settings, "SwitchNonInterferenceMode", false); }
    }

    public void SetSwitchNonInterferenceMode(bool value)
    {
        lock (_sync)
        {
            Settings["SwitchNonInterferenceMode"] = value;
            SaveLocked();
        }
    }

    public void SetCapturePaused(bool value)
    {
        lock (_sync)
        {
            Settings["CapturePaused"] = value;
            SaveLocked();
        }
    }

    public (bool Has, double Left, double Top) GetBubblePlacement()
    {
        lock (_sync)
        {
            return (
                GetBool(Settings, "HasMiniPlacement", false),
                GetDouble(Settings, "MiniLeft", 0),
                GetDouble(Settings, "MiniTop", 0));
        }
    }

    public void SetBubblePlacement(double left, double top)
    {
        lock (_sync)
        {
            Settings["MiniLeft"] = left;
            Settings["MiniTop"] = top;
            Settings["HasMiniPlacement"] = true;
            SaveLocked();
        }
    }

    public string GetStorageRoot()
    {
        lock (_sync)
        {
            var configured = GetString(Settings, "StorageDirectory", "").Trim();
            var root = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(_dataDir, "storage")
                : configured;
            Directory.CreateDirectory(root);
            return root;
        }
    }

    public string GetImageStorageDirectory()
    {
        var path = Path.Combine(GetStorageRoot(), "images");
        Directory.CreateDirectory(path);
        return path;
    }

    public void AddOrTouchText(string text, string sourceApp, string sourceTitle)
        => AddOrTouch("Text", Sha256("T|" + text), sourceApp, sourceTitle, o => o["Text"] = text);

    public void AddOrTouchFiles(IReadOnlyList<string> files, string sourceApp, string sourceTitle)
    {
        var key = "F|" + string.Join("|", files);
        AddOrTouch("Files", Sha256(key), sourceApp, sourceTitle, o =>
        {
            var arr = new JsonArray();
            foreach (var file in files) arr.Add(file);
            o["Files"] = arr;
        });
    }

    public bool HasItem(string kind, string hash)
    {
        lock (_sync)
        {
            return Items.OfType<JsonObject>().Any(x =>
                !GetBool(x, "IsDeleted", false) && !GetBool(x, "IsPendingPurge", false) &&
                string.Equals(GetString(x, "Kind", ""), kind, StringComparison.Ordinal) &&
                string.Equals(GetString(x, "ContentHash", ""), hash, StringComparison.Ordinal));
        }
    }

    public void AddOrTouchImage(string imagePath, string hash, string sourceApp, string sourceTitle)
        => AddOrTouch("Image", hash, sourceApp, sourceTitle, o =>
        {
            o["ImagePath"] = imagePath;
            o["ManagedByClipShelf"] = true;
        });

    public void TouchExisting(string kind, string hash, string sourceApp, string sourceTitle)
    {
        lock (_sync)
        {
            var item = Items.OfType<JsonObject>().FirstOrDefault(x =>
                !GetBool(x, "IsDeleted", false) && !GetBool(x, "IsPendingPurge", false) &&
                string.Equals(GetString(x, "Kind", ""), kind, StringComparison.Ordinal) &&
                string.Equals(GetString(x, "ContentHash", ""), hash, StringComparison.Ordinal));
            if (item == null) return;
            TouchLocked(item, sourceApp, sourceTitle);
            SaveLocked();
        }
    }

    private void AddOrTouch(string kind, string hash, string sourceApp, string sourceTitle, Action<JsonObject> populate)
    {
        lock (_sync)
        {
            var existing = Items.OfType<JsonObject>().FirstOrDefault(x =>
                !GetBool(x, "IsDeleted", false) && !GetBool(x, "IsPendingPurge", false) &&
                string.Equals(GetString(x, "Kind", ""), kind, StringComparison.Ordinal) &&
                string.Equals(GetString(x, "ContentHash", ""), hash, StringComparison.Ordinal));

            if (existing != null)
            {
                TouchLocked(existing, sourceApp, sourceTitle);
            }
            else
            {
                var now = DateTime.Now;
                var item = new JsonObject
                {
                    ["Id"] = Guid.NewGuid().ToString(),
                    ["Kind"] = kind,
                    ["Text"] = "",
                    ["Files"] = new JsonArray(),
                    ["ImagePath"] = null,
                    ["ManagedByClipShelf"] = false,
                    ["ContentHash"] = hash,
                    ["CreatedAt"] = now.ToString("O"),
                    ["LastUsedAt"] = now.ToString("O"),
                    ["UseCount"] = 1,
                    ["SourceApp"] = sourceApp,
                    ["SourceTitle"] = sourceTitle,
                    ["Favorite"] = false,
                    ["IsTemporary"] = false,
                    ["Category"] = "",
                    ["FavoriteFolder"] = "",
                    ["TemporaryFolder"] = "",
                    ["HistoryVisible"] = true
                };
                populate(item);
                Items.Insert(0, item);
            }

            TrimLocked();
            SaveLocked();
        }
    }

    private void TouchLocked(JsonObject item, string sourceApp, string sourceTitle)
    {
        item["LastUsedAt"] = DateTime.Now.ToString("O");
        item["HistoryVisible"] = true;
        item["UseCount"] = GetInt(item, "UseCount", 1) + 1;
        item["SourceApp"] = sourceApp;
        item["SourceTitle"] = sourceTitle;

        var index = Items.IndexOf(item);
        if (index > 0)
        {
            Items.RemoveAt(index);
            Items.Insert(0, item);
        }
    }

    private void TrimLocked()
    {
        var max = GetInt(Settings, "MaxRegularItems", 5000);
        if (max <= 0) return;

        var history = Items.OfType<JsonObject>()
            .Where(x => !GetBool(x, "IsDeleted", false) && !GetBool(x, "IsPendingPurge", false) && GetBool(x, "HistoryVisible", false) && GetDateTime(x, "PinnedAt") == DateTime.MinValue)
            .OrderByDescending(x => GetDateTime(x, "LastUsedAt"))
            .ToList();

        foreach (var item in history.Skip(max).ToList())
        {
            if (HasPersistentPlacement(item))
            {
                item["HistoryVisible"] = false;
            }
            else
            {
                item["IsDeleted"] = true;
                item["DeletedAt"] = DateTime.Now.ToString("O");
            }
        }
    }

    private static bool HasPersistentPlacement(JsonObject item)
        => GetBool(item, "Favorite", false)
           || GetDateTime(item, "PinnedAt") != DateTime.MinValue
           || GetBool(item, "IsTemporary", false)
           || !string.IsNullOrWhiteSpace(GetString(item, "Category", ""))
           || !string.IsNullOrWhiteSpace(GetString(item, "FavoriteFolder", ""))
           || !string.IsNullOrWhiteSpace(GetString(item, "TemporaryFolder", ""));

    private bool MigrateHistoryModelLocked()
    {
        var version = GetInt(Settings, "HistoryModelVersion", 0);
        if (version >= 1) return false;

        // Preserve the exact legacy v2.4 History surface on first upgrade: items
        // without a classification category were visible in History, classified
        // items were not. From this point forward HistoryVisible is independent.
        foreach (var item in Items.OfType<JsonObject>())
            item["HistoryVisible"] = string.IsNullOrWhiteSpace(GetString(item, "Category", ""));

        Settings["HistoryModelVersion"] = 1;
        return true;
    }

    private static void DeleteManagedPayload(JsonObject item)
    {
        if (!GetBool(item, "ManagedByClipShelf", false)) return;
        try
        {
            var kind = GetString(item, "Kind", "");
            if (kind == "Image")
            {
                var path = GetString(item, "ImagePath", "");
                if (File.Exists(path)) File.Delete(path);
            }
            else if (kind == "Files" && item["Files"] is JsonArray files)
            {
                foreach (var node in files)
                {
                    var path = node?.GetValue<string>() ?? "";
                    if (File.Exists(path)) File.Delete(path);
                }
            }
        }
        catch { }
    }

    private JsonObject LoadStateFromDisk()
    {
        try
        {
            if (!File.Exists(_historyPath)) return NewState();
            var parsed = JsonNode.Parse(ClipShelfShared.SafeState.Read(_historyPath)) as JsonObject;
            return parsed ?? NewState();
        }
        catch
        {
            throw new InvalidDataException("無法讀取剪貼庫資料；原檔已保留，請先恢復備份。" );
        }
    }

    private static JsonObject NewState() => new()
    {
        ["Items"] = new JsonArray(),
        ["Categories"] = new JsonArray(),
        ["Settings"] = new JsonObject()
    };

    private static void EnsureShape(JsonObject state)
    {
        if (state["Items"] is not JsonArray) state["Items"] = new JsonArray();
        if (state["Categories"] is not JsonArray) state["Categories"] = new JsonArray();
        if (state["Settings"] is not JsonObject) state["Settings"] = new JsonObject();
    }

    private JsonArray Items => (JsonArray)_state["Items"]!;
    private JsonObject Settings => (JsonObject)_state["Settings"]!;

    private void SaveLocked()
    {
        ClipShelfShared.SafeState.Write(_historyPath, _state.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
    }

    private void CreateDailyBackup()
    {
        try
        {
            if (!File.Exists(_historyPath)) return;
            var dir = Path.Combine(_dataDir, "backups");
            Directory.CreateDirectory(dir);
            var prefix = $"history_{DateTime.Now:yyyyMMdd}_";
            if (Directory.EnumerateFiles(dir, prefix + "*.json").Any()) return;
            var destination = Path.Combine(dir, $"{prefix}{DateTime.Now:HHmmss}.json");
            File.Copy(_historyPath, destination, false);
        }
        catch { }
    }

    public static string Sha256(string value) => Sha256(Encoding.UTF8.GetBytes(value));
    public static string Sha256(byte[] value) => Convert.ToHexString(SHA256.HashData(value));

    private static string GetString(JsonObject obj, string name, string fallback)
    {
        try { return obj[name]?.GetValue<string>() ?? fallback; }
        catch { return fallback; }
    }

    private static bool GetBool(JsonObject obj, string name, bool fallback)
    {
        try { return obj[name]?.GetValue<bool>() ?? fallback; }
        catch { return fallback; }
    }

    private static int GetInt(JsonObject obj, string name, int fallback)
    {
        try { return obj[name]?.GetValue<int>() ?? fallback; }
        catch
        {
            try { return (int)(obj[name]?.GetValue<long>() ?? fallback); }
            catch { return fallback; }
        }
    }

    private static double GetDouble(JsonObject obj, string name, double fallback)
    {
        try { return obj[name]?.GetValue<double>() ?? fallback; }
        catch { return fallback; }
    }

    private static DateTime GetDateTime(JsonObject obj, string name)
    {
        try { return obj[name]?.GetValue<DateTime>() ?? DateTime.MinValue; }
        catch
        {
            var text = GetString(obj, name, "");
            return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value)
                ? value : DateTime.MinValue;
        }
    }
}
