using System;
using System.IO;
using System.Linq;
using System.Text.Json;
namespace ClipShelfShared;
internal static class SafeState
{
    public static string Read(string path)
    {
        if (!File.Exists(path)) return "{\"Items\":[],\"Categories\":[],\"Settings\":{}}";
        var json = File.ReadAllText(path);
        Validate(json);
        return json;
    }
    public static void Validate(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("Items", out var items) || items.ValueKind != JsonValueKind.Array ||
            !root.TryGetProperty("Categories", out var categories) || categories.ValueKind != JsonValueKind.Array ||
            !root.TryGetProperty("Settings", out var settings) || settings.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("資料格式不完整；已停止寫入，保留原始檔案。");
    }
    public static void Write(string path, string json)
    {
        Validate(json);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var mutex = new System.Threading.Mutex(false, "Local\\ClipShelf.StateWrite");
        if (!mutex.WaitOne(TimeSpan.FromSeconds(10))) throw new IOException("資料正在使用，請稍後重試。");
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (File.Exists(path))
            {
                Read(path); // Never replace an unreadable current state.
                var backups = Path.Combine(Path.GetDirectoryName(path)!, "backups");
                Directory.CreateDirectory(backups);
                var snapshot = Path.Combine(backups, "history_" + DateTime.Now.ToString("yyyyMMdd_HH") + ".json");
                if (!File.Exists(snapshot)) File.Copy(path, snapshot);
                foreach (var old in Directory.GetFiles(backups, "history_*.json").OrderByDescending(File.GetLastWriteTimeUtc).Skip(72)) File.Delete(old);
            }
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream)) { writer.Write(json); writer.Flush(); stream.Flush(true); }
            if (File.Exists(path)) File.Replace(temp, path, path + ".previous", true);
            else File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); mutex.ReleaseMutex(); }
    }
}
