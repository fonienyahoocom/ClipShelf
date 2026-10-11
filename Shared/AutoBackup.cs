using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace ClipShelfShared;
internal static class AutoBackup
{
    public static void Run(string dataDir)
    {
        var history = Path.Combine(dataDir, "history.json");
        if (!File.Exists(history)) return;
        var dir = Path.Combine(dataDir, "auto-backups"); Directory.CreateDirectory(dir);
        using var mutex = new System.Threading.Mutex(false, "Local\\ClipShelf.AutoBackup");
        if (!mutex.WaitOne(0)) return;
        string? temp = null;
        try {
            var latest = Directory.GetFiles(dir, "ClipShelf-auto-*.zip").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if (latest != null && DateTime.UtcNow - File.GetLastWriteTimeUtc(latest) < TimeSpan.FromHours(1)) return;
            var state = JsonNode.Parse(SafeState.Read(history))!.AsObject();
            var path = Path.Combine(dir, "ClipShelf-auto-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip"); temp = path + ".tmp";
            var items = state["Items"]!.AsArray().DeepClone().AsArray();
            var warnings = new System.Collections.Generic.List<string>(); int payloads = 0;
            using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create)) {
                foreach (var node in items) {
                    var item = node!.AsObject(); var id = Guid.Parse(item["Id"]!.GetValue<string>()).ToString("N");
                    var kind = item["Kind"]?.GetValue<string>();
                    JsonObject? Copy(string source, string entry) {
                        if (!File.Exists(source)) { warnings.Add("原檔不存在或為資料夾：" + source); return null; }
                        try { zip.CreateEntryFromFile(source, entry, CompressionLevel.Optimal); payloads++; return new JsonObject { ["ArchivePath"] = entry, ["OriginalName"] = Path.GetFileName(source), ["IsDirectory"] = false }; }
                        catch (Exception ex) { warnings.Add(source + ": " + ex.Message); return null; }
                    }
                    if (kind == "Image" && item["ImagePath"] is JsonValue image) {
                        var source = image.GetValue<string>(); item["ImagePayload"] = Copy(source, "payload/" + id + "/image/" + Path.GetFileName(source));
                    }
                    if (kind == "Files" && item["Files"] is JsonArray files) {
                        var refs = new JsonArray(); int i = 0;
                        foreach (var file in files) { var source = file!.GetValue<string>(); var payload = Copy(source, "payload/"+id+"/files/"+(++i).ToString("D4")+"/"+Path.GetFileName(source)); if (payload != null) refs.Add(payload); }
                        item["FilePayloads"] = refs;
                    }
                }
                void Write(string name, object value) { using var stream = zip.CreateEntry(name).Open(); JsonSerializer.Serialize(stream, value); }
                Write("state.json", new JsonObject { ["FormatVersion"] = 1, ["CreatedAtUtc"] = DateTime.UtcNow, ["SourceVersion"] = "2.14.0", ["FolderPaths"] = state["Categories"]!.DeepClone(), ["SidebarRootOrder"] = state["Settings"]?["SidebarRootOrder"]?.DeepClone() ?? new JsonArray(), ["Items"] = items });
                Write("manifest.json", new { Format = "ClipShelfBackup", FormatVersion = 1, CreatedAtUtc = DateTime.UtcNow, SourceVersion = "2.14.0", ItemCount = items.Count, PayloadCount = payloads, WarningCount = warnings.Count });
                Write("settings.json", state["Settings"]!);
                Write("warnings.json", warnings);
            }
            using (var verify = ZipFile.OpenRead(temp)) {
                foreach (var entry in verify.Entries) { using var stream = entry.Open(); stream.CopyTo(Stream.Null); }
                if (verify.GetEntry("state.json") == null) throw new IOException("備份驗證失敗");
            }
            File.Move(temp, path);
            File.WriteAllText(Path.Combine(dir, "STATUS.txt"), DateTime.Now.ToString("O") + "\n" + (warnings.Count == 0 ? "備份成功" : "備份有缺件，請檢查原檔。\n" + string.Join("\n",warnings)));
            foreach (var old in Directory.GetFiles(dir,"ClipShelf-auto-*.zip").OrderByDescending(File.GetLastWriteTimeUtc).Skip(14)) File.Delete(old);
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(dir,"STATUS.txt"), "備份失敗：" + ex.Message); }
        finally { if (temp != null && File.Exists(temp)) File.Delete(temp); mutex.ReleaseMutex(); }
    }
}
