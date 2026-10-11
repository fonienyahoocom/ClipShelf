using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ClipShelf;

// Shared by the WPF edit handlers and headless tests. Persistence is supplied by
// the existing checked HistoryStore write, so a failed write rolls back the model.
internal static class ClipEditing
{
    public static void SaveText(ClipItem item, string text, IEnumerable<ClipItem> items, Action persist)
    {
        RequireActive(item);
        if (item.Kind != "Text") throw new InvalidOperationException("這筆紀錄不是純文字。");
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("請保留至少一個文字；移除整筆紀錄請使用原有移除功能。");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("T|" + text)));
        if (items.Any(x => x.Id != item.Id && !x.IsDeleted && !x.IsPendingPurge && x.Kind == "Text" && x.ContentHash == hash))
            throw new InvalidOperationException("已有另一筆相同文字。請修改內容後再儲存；兩筆原紀錄都會保留。");

        var oldText = item.Text;
        var oldHash = item.ContentHash;
        var oldCount = item.UseCount;
        try
        {
            item.Text = text;
            item.ContentHash = hash;
            // The old repeated-copy count describes the old content. An actual
            // content change starts a new count without changing identity or dates.
            if (!string.Equals(oldText, text, StringComparison.Ordinal)) item.UseCount = 1;
            persist();
        }
        catch
        {
            item.Text = oldText;
            item.ContentHash = oldHash;
            item.UseCount = oldCount;
            throw;
        }
    }

    public static void SaveTitle(ClipItem item, string title, Action persist)
    {
        RequireActive(item);
        var oldTitle = item.Title;
        try { item.Title = title.Replace('\r', ' ').Replace('\n', ' ').Trim(); persist(); }
        catch { item.Title = oldTitle; throw; }
    }

    private static void RequireActive(ClipItem item)
    {
        if (item.IsDeleted || item.IsPendingPurge) throw new InvalidOperationException("請先復原這筆紀錄再編輯。");
    }
}
