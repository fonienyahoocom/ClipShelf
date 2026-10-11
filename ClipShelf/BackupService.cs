using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClipShelf;

public static class BackupService
{
    public const int CurrentFormatVersion = 1;

    public sealed class ExportResult
    {
        public int ItemCount { get; init; }
        public int PayloadCount { get; init; }
        public List<string> Warnings { get; init; } = new();
    }

    public sealed class ImportResult
    {
        public List<ClipItem> Items { get; init; } = new();
        public List<string> FolderPaths { get; init; } = new();
        public List<string> SidebarRootOrder { get; init; } = new();
        public int SkippedExisting { get; set; }
        public string ImportRoot { get; init; } = "";
        public List<string> Warnings { get; init; } = new();
    }

    public sealed class ImportPreviewResult
    {
        public int TotalItems { get; init; }
        public int NewItemCount => CertainNewItemIds.Count;
        public int DuplicateIdCount { get; set; }
        public int DuplicateContentCount { get; set; }
        public int UncertainCount => UncertainItemIds.Count;
        public int NewFolderCount { get; init; }
        public DateTime BackupCreatedAtUtc { get; init; }
        public string SourceVersion { get; init; } = "";
        public List<Guid> CertainNewItemIds { get; init; } = new();
        public List<Guid> UncertainItemIds { get; init; } = new();
        public List<string> Warnings { get; init; } = new();
    }

    public static ExportResult Export(
        string archivePath,
        IReadOnlyCollection<ClipItem> items,
        IReadOnlyCollection<string> folderPaths,
        AppSettings settings)
    {
        var warnings = new List<string>();
        var payloadCount = 0;
        var state = new BackupState
        {
            FormatVersion = CurrentFormatVersion,
            CreatedAtUtc = DateTime.UtcNow,
            SourceVersion = typeof(BackupService).Assembly.GetName().Version?.ToString() ?? "",
            FolderPaths = folderPaths.ToList(),
            SidebarRootOrder = (settings.SidebarRootOrder ?? new List<string>()).ToList()
        };

        var parent = Path.GetDirectoryName(archivePath);
        if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
        if (File.Exists(archivePath)) File.Delete(archivePath);
        var archiveFullPath = Path.GetFullPath(archivePath);

        try
        {
            using var zip = ZipFile.Open(archivePath, ZipArchiveMode.Create);

            foreach (var item in items)
            {
                var backupItem = BackupItem.FromClipItem(item);

                if (item.Kind == "Image" && !string.IsNullOrWhiteSpace(item.ImagePath))
                {
                    if (File.Exists(item.ImagePath))
                    {
                        var fileName = SafeLeaf(Path.GetFileName(item.ImagePath), "image.png");
                        var archiveEntry = $"payload/{item.Id:N}/image/{fileName}";
                        try
                        {
                            AddFile(zip, item.ImagePath, archiveEntry);
                            backupItem.ImagePayload = new PayloadRef
                            {
                                ArchivePath = archiveEntry,
                                OriginalName = fileName,
                                IsDirectory = false
                            };
                            payloadCount++;
                        }
                        catch (Exception ex)
                        {
                            warnings.Add($"圖片未能備份：{item.ImagePath} ({ex.Message})");
                        }
                    }
                    else
                    {
                        warnings.Add($"圖片原檔不存在：{item.ImagePath}");
                    }
                }

                if (item.Kind == "Files")
                {
                    var index = 0;
                    foreach (var source in item.Files ?? new List<string>())
                    {
                        index++;
                        if (File.Exists(source))
                        {
                            var name = SafeLeaf(Path.GetFileName(source), $"file_{index}");
                            var archiveEntry = $"payload/{item.Id:N}/files/{index:D4}/{name}";
                            try
                            {
                                AddFile(zip, source, archiveEntry);
                                backupItem.FilePayloads.Add(new PayloadRef
                                {
                                    ArchivePath = archiveEntry,
                                    OriginalName = name,
                                    IsDirectory = false
                                });
                                payloadCount++;
                            }
                            catch (Exception ex)
                            {
                                warnings.Add($"檔案未能備份：{source} ({ex.Message})");
                            }
                        }
                        else if (Directory.Exists(source))
                        {
                            var name = SafeLeaf(new DirectoryInfo(source).Name, $"folder_{index}");
                            var archiveRoot = $"payload/{item.Id:N}/files/{index:D4}/{name}";
                            try
                            {
                                AddDirectory(zip, source, archiveRoot, archiveFullPath);
                                backupItem.FilePayloads.Add(new PayloadRef
                                {
                                    ArchivePath = archiveRoot,
                                    OriginalName = name,
                                    IsDirectory = true
                                });
                                payloadCount++;
                            }
                            catch (Exception ex)
                            {
                                warnings.Add($"資料夾未能完整備份：{source} ({ex.Message})");
                            }
                        }
                        else
                        {
                            warnings.Add($"檔案／資料夾原路徑不存在：{source}");
                        }
                    }
                }

                state.Items.Add(backupItem);
            }

            WriteJsonEntry(zip, "state.json", state);
            WriteJsonEntry(zip, "manifest.json", new BackupManifest
            {
                Format = "ClipShelfBackup",
                FormatVersion = CurrentFormatVersion,
                CreatedAtUtc = state.CreatedAtUtc,
                SourceVersion = state.SourceVersion,
                ItemCount = state.Items.Count,
                PayloadCount = payloadCount,
                WarningCount = warnings.Count
            });
        }
        catch
        {
            try { if (File.Exists(archivePath)) File.Delete(archivePath); } catch { }
            throw;
        }

        return new ExportResult { ItemCount = state.Items.Count, PayloadCount = payloadCount, Warnings = warnings };
    }

    public static ImportPreviewResult AnalyzeImport(
        string archivePath,
        IReadOnlyCollection<ClipItem> existingItems,
        IReadOnlyCollection<string> existingFolderPaths)
    {
        using var zip = ZipFile.OpenRead(archivePath);
        var (manifest, state) = ReadValidatedBackup(zip);

        var existingIds = existingItems.Select(x => x.Id).ToHashSet();
        var knownFingerprints = new HashSet<string>(StringComparer.Ordinal);
        var existingWeakHashes = existingItems
            .Where(x => CanUseWeakContentHash(x.Kind, x.ContentHash))
            .Select(x => $"{x.Kind}|{x.ContentHash}")
            .ToHashSet(StringComparer.Ordinal);
        var warnings = new List<string>();

        foreach (var item in existingItems)
        {
            try
            {
                var fingerprint = FingerprintExistingItem(item);
                if (!string.IsNullOrWhiteSpace(fingerprint))
                    knownFingerprints.Add(fingerprint);
            }
            catch (Exception ex)
            {
                warnings.Add($"現有項目 {item.Id} 無法建立去重指紋：{ex.Message}");
            }
        }

        var backupFingerprintsSeen = new HashSet<string>(knownFingerprints, StringComparer.Ordinal);
        var certainNew = new List<Guid>();
        var uncertain = new List<Guid>();
        var duplicateId = 0;
        var duplicateContent = 0;

        foreach (var backupItem in state.Items ?? new())
        {
            if (existingIds.Contains(backupItem.Id))
            {
                duplicateId++;
                continue;
            }

            var weakHash = CanUseWeakContentHash(backupItem.Kind, backupItem.ContentHash)
                ? $"{backupItem.Kind}|{backupItem.ContentHash}"
                : "";
            if (!string.IsNullOrWhiteSpace(weakHash) && existingWeakHashes.Contains(weakHash))
            {
                duplicateContent++;
                continue;
            }

            try
            {
                var fingerprint = FingerprintBackupItem(zip, backupItem);
                if (string.IsNullOrWhiteSpace(fingerprint))
                {
                    uncertain.Add(backupItem.Id);
                    continue;
                }

                if (!backupFingerprintsSeen.Add(fingerprint))
                {
                    duplicateContent++;
                    continue;
                }

                certainNew.Add(backupItem.Id);
            }
            catch (Exception ex)
            {
                uncertain.Add(backupItem.Id);
                warnings.Add($"備份項目 {backupItem.Id} 無法安全判定是否重複：{ex.Message}");
            }
        }

        var folderSet = existingFolderPaths
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newFolderCount = (state.FolderPaths ?? new())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count(x => !folderSet.Contains(x));

        return new ImportPreviewResult
        {
            TotalItems = (state.Items ?? new()).Count,
            DuplicateIdCount = duplicateId,
            DuplicateContentCount = duplicateContent,
            NewFolderCount = newFolderCount,
            BackupCreatedAtUtc = manifest.CreatedAtUtc,
            SourceVersion = manifest.SourceVersion ?? "",
            CertainNewItemIds = certainNew,
            UncertainItemIds = uncertain,
            Warnings = warnings
        };
    }

    public static ImportResult Import(
        string archivePath,
        string storageRoot,
        IReadOnlyCollection<Guid> selectedItemIds,
        IReadOnlyCollection<Guid> existingIds)
    {
        using var zip = ZipFile.OpenRead(archivePath);
        var (_, state) = ReadValidatedBackup(zip);

        var selectedSet = selectedItemIds.ToHashSet();
        var idSet = existingIds.ToHashSet();
        var importRoot = Path.Combine(storageRoot, "imports", $"backup_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}"[..37]);
        var result = new ImportResult
        {
            FolderPaths = (state.FolderPaths ?? new()).ToList(),
            SidebarRootOrder = (state.SidebarRootOrder ?? new()).ToList(),
            ImportRoot = importRoot
        };
        var createdImportRoot = false;

        try
        {
            foreach (var backupItem in state.Items ?? new())
            {
                if (!selectedSet.Contains(backupItem.Id))
                    continue;

                if (idSet.Contains(backupItem.Id))
                {
                    result.SkippedExisting++;
                    continue;
                }

                var item = backupItem.ToClipItem();

                if (item.Kind == "Image")
                {
                    if (backupItem.ImagePayload == null)
                    {
                        result.Warnings.Add($"略過圖片項目 {item.Id}：備份中沒有圖片內容。");
                        continue;
                    }
                    createdImportRoot = true;
                    var destination = Path.Combine(importRoot, "images", item.Id.ToString("N"), SafeLeaf(backupItem.ImagePayload.OriginalName, "image.png"));
                    ExtractFile(zip, backupItem.ImagePayload.ArchivePath, destination);
                    item.ImagePath = destination;
                    item.ManagedByClipShelf = true;
                }
                else if (item.Kind == "Files")
                {
                    var restored = new List<string>();
                    var payloadIndex = 0;
                    foreach (var payload in backupItem.FilePayloads ?? new())
                    {
                        payloadIndex++;
                        createdImportRoot = true;
                        var baseDir = Path.Combine(importRoot, "files", item.Id.ToString("N"), payloadIndex.ToString("D4"));
                        var leaf = SafeLeaf(payload.OriginalName, payload.IsDirectory ? $"folder_{payloadIndex}" : $"file_{payloadIndex}");
                        var destination = Path.Combine(baseDir, leaf);
                        if (payload.IsDirectory)
                            ExtractDirectory(zip, payload.ArchivePath, destination);
                        else
                            ExtractFile(zip, payload.ArchivePath, destination);
                        restored.Add(destination);
                    }

                    if (restored.Count == 0)
                    {
                        result.Warnings.Add($"略過檔案項目 {item.Id}：備份中沒有可還原的檔案內容。");
                        continue;
                    }
                    item.Files = restored;
                    item.ManagedByClipShelf = true;
                }

                // A backup may have been taken during the 8-second permanent-delete grace period.
                // Importing must never immediately destroy restored data, so restore that state to
                // ordinary Recently Deleted instead of restarting an expired purge deadline.
                if (item.IsPendingPurge)
                {
                    item.IsPendingPurge = false;
                    item.PendingPurgeAt = null;
                    item.IsDeleted = true;
                    item.DeletedAt ??= DateTime.Now;
                }

                result.Items.Add(item);
                idSet.Add(item.Id);
            }

            if (createdImportRoot && result.Items.All(x => x.Kind == "Text"))
            {
                try { if (Directory.Exists(importRoot)) Directory.Delete(importRoot, true); } catch { }
            }
            return result;
        }
        catch
        {
            try { if (createdImportRoot && Directory.Exists(importRoot)) Directory.Delete(importRoot, true); } catch { }
            throw;
        }
    }

    private static (BackupManifest Manifest, BackupState State) ReadValidatedBackup(ZipArchive zip)
    {
        var manifest = ReadJsonEntry<BackupManifest>(zip, "manifest.json")
            ?? throw new InvalidDataException("這不是有效的 ClipShelf 備份：缺少 manifest.json。");
        if (!string.Equals(manifest.Format, "ClipShelfBackup", StringComparison.Ordinal))
            throw new InvalidDataException("這個檔案不是 ClipShelf 備份格式。");
        if (manifest.FormatVersion != CurrentFormatVersion)
            throw new InvalidDataException($"不支援的備份格式版本：{manifest.FormatVersion}。");

        var state = ReadJsonEntry<BackupState>(zip, "state.json")
            ?? throw new InvalidDataException("備份缺少 state.json。");
        if (state.FormatVersion != CurrentFormatVersion)
            throw new InvalidDataException($"state.json 格式版本不支援：{state.FormatVersion}。");

        return (manifest, state);
    }

    private static string? FingerprintExistingItem(ClipItem item)
    {
        if (string.Equals(item.Kind, "Text", StringComparison.Ordinal))
            return "TEXT|" + HashUtf8(item.Text ?? "");

        if (string.Equals(item.Kind, "Image", StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(item.ImagePath) || !File.Exists(item.ImagePath))
                return null;
            return "IMAGE|" + HashFile(item.ImagePath);
        }

        if (string.Equals(item.Kind, "Files", StringComparison.Ordinal))
        {
            var components = new List<string>();
            foreach (var path in item.Files ?? new List<string>())
            {
                if (File.Exists(path))
                    components.Add("F|" + NormalizeLeafName(Path.GetFileName(path)) + "|" + HashFile(path));
                else if (Directory.Exists(path))
                    components.Add("D|" + NormalizeLeafName(new DirectoryInfo(path).Name) + "|" + FingerprintDirectory(path));
                else
                    return null;
            }

            if (components.Count == 0) return null;
            components.Sort(StringComparer.Ordinal);
            return "FILES|" + HashUtf8(string.Join("\n", components));
        }

        return null;
    }

    private static string? FingerprintBackupItem(ZipArchive zip, BackupItem item)
    {
        if (string.Equals(item.Kind, "Text", StringComparison.Ordinal))
            return "TEXT|" + HashUtf8(item.Text ?? "");

        if (string.Equals(item.Kind, "Image", StringComparison.Ordinal))
        {
            if (item.ImagePayload == null) return null;
            var entry = zip.GetEntry(NormalizeArchivePath(item.ImagePayload.ArchivePath));
            if (entry == null) return null;
            return "IMAGE|" + HashZipEntry(entry);
        }

        if (string.Equals(item.Kind, "Files", StringComparison.Ordinal))
        {
            var components = new List<string>();
            foreach (var payload in item.FilePayloads ?? new())
            {
                if (payload.IsDirectory)
                {
                    var fingerprint = FingerprintZipDirectory(zip, payload.ArchivePath);
                    if (fingerprint == null) return null;
                    components.Add("D|" + NormalizeLeafName(payload.OriginalName) + "|" + fingerprint);
                }
                else
                {
                    var entry = zip.GetEntry(NormalizeArchivePath(payload.ArchivePath));
                    if (entry == null) return null;
                    components.Add("F|" + NormalizeLeafName(payload.OriginalName) + "|" + HashZipEntry(entry));
                }
            }

            if (components.Count == 0) return null;
            components.Sort(StringComparer.Ordinal);
            return "FILES|" + HashUtf8(string.Join("\n", components));
        }

        return null;
    }

    private static string FingerprintDirectory(string root)
    {
        var lines = new List<string>();
        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
        {
            var relative = NormalizeRelativePath(Path.GetRelativePath(root, directory));
            lines.Add("D|" + relative);
        }

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = NormalizeRelativePath(Path.GetRelativePath(root, file));
            lines.Add("F|" + relative + "|" + HashFile(file));
        }

        lines.Sort(StringComparer.Ordinal);
        return HashUtf8(string.Join("\n", lines));
    }

    private static string? FingerprintZipDirectory(ZipArchive zip, string archiveRoot)
    {
        archiveRoot = NormalizeArchivePath(archiveRoot).TrimEnd('/');
        var prefix = archiveRoot + "/";
        var lines = new List<string>();
        var found = false;

        foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith(prefix, StringComparison.Ordinal)))
        {
            found = true;
            var relative = entry.FullName[prefix.Length..].TrimEnd('/');
            if (string.IsNullOrWhiteSpace(relative)) continue;
            relative = NormalizeRelativePath(relative);

            if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
                lines.Add("D|" + relative);
            else
                lines.Add("F|" + relative + "|" + HashZipEntry(entry));
        }

        if (!found) return null;
        lines.Sort(StringComparer.Ordinal);
        return HashUtf8(string.Join("\n", lines));
    }

    private static string NormalizeLeafName(string? name)
        => (name ?? "").Normalize(NormalizationForm.FormC).ToUpperInvariant();

    private static string NormalizeRelativePath(string path)
        => (path ?? "").Replace('\\', '/').Trim('/').ToUpperInvariant();

    private static bool CanUseWeakContentHash(string kind, string? contentHash)
    {
        if (string.IsNullOrWhiteSpace(contentHash)) return false;
        if (string.Equals(kind, "Text", StringComparison.Ordinal)) return true;
        if (string.Equals(kind, "Image", StringComparison.Ordinal)) return true;
        // Managed file imports use I|<file-bytes-hash>. Ordinary clipboard Files use a
        // path-derived hash instead, so those must never be treated as content identity.
        return string.Equals(kind, "Files", StringComparison.Ordinal) && contentHash.StartsWith("I|", StringComparison.Ordinal);
    }

    private static string HashUtf8(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? "")));

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string HashZipEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return Convert.ToHexString(SHA256.HashData(stream));
    }


    public static void DiscardImportedPayload(ImportResult? result)
    {
        if (result == null || string.IsNullOrWhiteSpace(result.ImportRoot)) return;
        try
        {
            if (Directory.Exists(result.ImportRoot))
                Directory.Delete(result.ImportRoot, recursive: true);
        }
        catch { }
    }

    private static void AddFile(ZipArchive zip, string sourcePath, string archiveEntry)
    {
        archiveEntry = NormalizeArchivePath(archiveEntry);
        zip.CreateEntryFromFile(sourcePath, archiveEntry, CompressionLevel.Optimal);
    }

    private static void AddDirectory(ZipArchive zip, string sourceDirectory, string archiveRoot, string excludedFilePath)
    {
        archiveRoot = NormalizeArchivePath(archiveRoot).TrimEnd('/');
        zip.CreateEntry(archiveRoot + "/");

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDirectory, directory).Replace('\\', '/');
            zip.CreateEntry($"{archiveRoot}/{relative.TrimEnd('/')}/");
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            // If the user saves the backup inside a folder that is itself being backed up,
            // never recurse the in-progress .clipshelfbackup archive into itself.
            if (string.Equals(Path.GetFullPath(file), excludedFilePath, StringComparison.OrdinalIgnoreCase))
                continue;

            var relative = Path.GetRelativePath(sourceDirectory, file).Replace('\\', '/');
            AddFile(zip, file, $"{archiveRoot}/{relative}");
        }
    }

    private static void ExtractFile(ZipArchive zip, string archiveEntry, string destination)
    {
        archiveEntry = NormalizeArchivePath(archiveEntry);
        var entry = zip.GetEntry(archiveEntry)
            ?? throw new InvalidDataException($"備份內容缺少：{archiveEntry}");
        var parent = Path.GetDirectoryName(destination);
        if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
        entry.ExtractToFile(destination, overwrite: false);
    }

    private static void ExtractDirectory(ZipArchive zip, string archiveRoot, string destinationRoot)
    {
        archiveRoot = NormalizeArchivePath(archiveRoot).TrimEnd('/');
        Directory.CreateDirectory(destinationRoot);
        var prefix = archiveRoot + "/";
        var found = false;
        foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith(prefix, StringComparison.Ordinal)))
        {
            found = true;
            var relative = entry.FullName[prefix.Length..];
            if (string.IsNullOrWhiteSpace(relative)) continue;
            var destination = SafeCombine(destinationRoot, relative);
            if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(destination);
            }
            else
            {
                var parent = Path.GetDirectoryName(destination);
                if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
                entry.ExtractToFile(destination, overwrite: false);
            }
        }
        if (!found)
            throw new InvalidDataException($"備份內容缺少資料夾：{archiveRoot}");
    }

    private static string SafeCombine(string root, string relativeArchivePath)
    {
        var relative = relativeArchivePath.Replace('/', Path.DirectorySeparatorChar);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("備份包含不安全的路徑。");
        return full;
    }

    private static string NormalizeArchivePath(string path)
    {
        path = (path ?? "").Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrWhiteSpace(path) || path.Split('/').Any(p => p == ".."))
            throw new InvalidDataException("備份包含不安全的 archive path。");
        return path;
    }

    private static string SafeLeaf(string? name, string fallback)
    {
        name = string.IsNullOrWhiteSpace(name) ? fallback : name;
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? fallback : name;
    }

    private static void WriteJsonEntry<T>(ZipArchive zip, string name, T value)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        JsonSerializer.Serialize(stream, value, new JsonSerializerOptions { WriteIndented = true });
    }

    private static T? ReadJsonEntry<T>(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name);
        if (entry == null) return default;
        using var stream = entry.Open();
        return JsonSerializer.Deserialize<T>(stream);
    }

    private sealed class BackupManifest
    {
        public string Format { get; set; } = "ClipShelfBackup";
        public int FormatVersion { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public string SourceVersion { get; set; } = "";
        public int ItemCount { get; set; }
        public int PayloadCount { get; set; }
        public int WarningCount { get; set; }
    }

    private sealed class BackupState
    {
        public int FormatVersion { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public string SourceVersion { get; set; } = "";
        public List<string> FolderPaths { get; set; } = new();
        public List<string> SidebarRootOrder { get; set; } = new();
        public List<BackupItem> Items { get; set; } = new();
    }

    private sealed class PayloadRef
    {
        public string ArchivePath { get; set; } = "";
        public string OriginalName { get; set; } = "";
        public bool IsDirectory { get; set; }
    }

    private sealed class BackupItem
    {
        public Guid Id { get; set; }
        public string Kind { get; set; } = "Text";
        public string Text { get; set; } = "";
        public bool ManagedByClipShelf { get; set; }
        public string ContentHash { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public bool HistoryVisible { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }
        public bool IsPendingPurge { get; set; }
        public DateTime? PendingPurgeAt { get; set; }
        public DateTime LastUsedAt { get; set; }
        public int UseCount { get; set; }
        public string SourceApp { get; set; } = "";
        public string SourceTitle { get; set; } = "";
        public string Title { get; set; } = "";
        public string Note { get; set; } = "";
        public bool Favorite { get; set; }
        public bool IsTemporary { get; set; }
        public string Category { get; set; } = "";
        public string FavoriteFolder { get; set; } = "";
        public string TemporaryFolder { get; set; } = "";
        public PayloadRef? ImagePayload { get; set; }
        public List<PayloadRef> FilePayloads { get; set; } = new();

        public static BackupItem FromClipItem(ClipItem item) => new()
        {
            Id = item.Id,
            Kind = item.Kind,
            Text = item.Text,
            ManagedByClipShelf = item.ManagedByClipShelf,
            ContentHash = item.ContentHash,
            CreatedAt = item.CreatedAt,
            HistoryVisible = item.HistoryVisible,
            IsDeleted = item.IsDeleted,
            DeletedAt = item.DeletedAt,
            IsPendingPurge = item.IsPendingPurge,
            PendingPurgeAt = item.PendingPurgeAt,
            LastUsedAt = item.LastUsedAt,
            UseCount = item.UseCount,
            SourceApp = item.SourceApp,
            SourceTitle = item.SourceTitle,
            Title = item.Title, Note = item.Note,
            Favorite = item.Favorite,
            IsTemporary = item.IsTemporary,
            Category = item.Category,
            FavoriteFolder = item.FavoriteFolder,
            TemporaryFolder = item.TemporaryFolder
        };

        public ClipItem ToClipItem() => new()
        {
            Id = Id,
            Kind = Kind,
            Text = Text,
            ManagedByClipShelf = ManagedByClipShelf,
            ContentHash = ContentHash,
            CreatedAt = CreatedAt,
            HistoryVisible = HistoryVisible,
            IsDeleted = IsDeleted,
            DeletedAt = DeletedAt,
            IsPendingPurge = IsPendingPurge,
            PendingPurgeAt = PendingPurgeAt,
            LastUsedAt = LastUsedAt,
            UseCount = UseCount,
            SourceApp = SourceApp,
            SourceTitle = SourceTitle,
            Title = Title, Note = Note,
            Favorite = Favorite,
            IsTemporary = IsTemporary,
            Category = Category,
            FavoriteFolder = FavoriteFolder,
            TemporaryFolder = TemporaryFolder,
            Files = new List<string>(),
            ImagePath = null
        };
    }
}
