using System;
using System.Collections.Generic;
using System.Linq;

namespace ClipShelf;

// Independent selection/search projection: never writes card presentation flags,
// drafts, manager selection or the underlying history ordering.
internal sealed class QuickPickerModel
{
    private List<ClipItem> _snapshot = new();
    public void Open(IEnumerable<ClipItem> items) => _snapshot = items
        .Where(Eligible).OrderByDescending(x => x.LastUsedAt).ToList();
    public void Synchronize(IEnumerable<ClipItem> items)
    {
        var live = items.Where(Eligible).ToDictionary(x => x.Id);
        _snapshot = _snapshot.Where(x => live.ContainsKey(x.Id)).Select(x => live[x.Id]).ToList();
        var ids = _snapshot.Select(x => x.Id).ToHashSet();
        _snapshot.AddRange(live.Values.Where(x => !ids.Contains(x.Id)).OrderByDescending(x => x.LastUsedAt));
    }
    public IReadOnlyList<ClipItem> Search(string query)
    {
        var words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return _snapshot.Where(x => words.All(w => Contains(x.Text, w) || Contains(x.Title, w)
            || Contains(x.Note, w) || Contains(x.SourceApp, w) || x.Files.Any(f => Contains(f, w))))
            .Take(1000).ToList();
    }
    private static bool Contains(string? text, string word) => (text ?? "").Contains(word, StringComparison.OrdinalIgnoreCase);
    public PickerScope Scope { get; set; } = new("History", "", "歷史");
    private bool Eligible(ClipItem item) => !item.IsDeleted && !item.IsPendingPurge && Scope.Matches(item);
}

internal sealed class PickerSession
{
    public bool Visible { get; private set; }
    public bool Busy { get; private set; }
    public long Target { get; private set; }
    public int TargetPid { get; private set; }
    public void Open(long target, int pid) { if (Busy) return; Visible = true; Target = target; TargetPid = pid; }
    public void Hide() => Visible = false;
    public bool TryBeginPaste() { if (!Visible || Busy) return false; Busy = true; return true; }
    public void EndPaste() => Busy = false;
}

internal sealed record PickerScope(string Kind, string Path, string Label)
{
    public string Key => Kind + ":" + Path;
    public bool Matches(ClipItem item)
    {
        if (Kind == "History") return item.HistoryVisible;
        if (Kind == "Favorite") return item.Favorite;
        if (Kind == "Temporary") return item.IsTemporary;
        if (Kind is "AI" or "Quick") {
            var root = Kind == "AI" ? "AI 用語" : "快速用語";
            return item.Category == root || item.Category.StartsWith(root + "/", StringComparison.Ordinal);
        }
        var relative = SidebarItem.GetRelativePath(Path);
        return SidebarItem.GetScope(Path) switch {
            "Favorite" => item.Favorite && SidebarItem.NormalizePath(item.FavoriteFolder) == relative,
            "Temporary" => item.IsTemporary && SidebarItem.NormalizePath(item.TemporaryFolder) == relative,
            "AI" => item.Category == "AI 用語/" + relative,
            "Quick" => item.Category == "快速用語/" + relative,
            _ => item.Category == Path
        };
    }
    public static IReadOnlyList<PickerScope> Build(IEnumerable<string> folders)
    {
        var result = new List<PickerScope> {
            new("History", "", "歷史"), new("Favorite", "", "收藏"),
            new("Temporary", "", "暫存"), new("AI", "", "AI 用語"), new("Quick", "", "快速用語")
        };
        foreach (var path in folders.Select(SidebarItem.NormalizePath).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)) {
            var prefix = SidebarItem.GetScope(path) switch {
                "Favorite" => "收藏 / ", "Temporary" => "暫存 / ", "AI" => "AI 用語 / ", "Quick" => "快速用語 / ", _ => ""
            };
            result.Add(new("Folder", path, prefix + SidebarItem.GetRelativePath(path)));
        }
        return result;
    }
}

internal static class PickerSelection
{
    public static string CombineText(IReadOnlyList<ClipItem> items)
    {
        if (items.Count == 0 || items.Any(x => x.IsDeleted || x.IsPendingPurge))
            throw new InvalidOperationException("選取內容已移除，請重新選取。");
        if (items.Any(x => x.Kind != "Text"))
            throw new InvalidOperationException("多選複製／貼上只支援文字；圖片或檔案請單選。");
        return string.Join(Environment.NewLine, items.Select(x => x.Text));
    }
}

internal static class PickerDismissal
{
    public static bool ShouldDismiss(bool visible, bool active, bool busy, bool ownedInteraction, bool closeOnDeactivate)
        => visible && !active && !busy && !ownedInteraction && closeOnDeactivate;
}
