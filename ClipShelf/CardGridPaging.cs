using System;
using System.Collections.Generic;

namespace ClipShelf;

// The WPF WrapPanel does not virtualize. Never hand it an unbounded history list.
public static class CardGridPaging
{
    public const int PageSize = 24;
    // Actual WPF ListBox width (DIPs), not physical pixels or Window.Width.
    // In the user's 1132px recording, a 150% UI scale may leave only ~470
    // DIPs for the list. The old 580-DIP cutoff forced a single column.
    // Use a minimum ~200-DIP card width, while retaining one column when
    // the list is genuinely too narrow.
    public const double TwoColumnAvailableThreshold = 420d;
    public static double CardWidth(double listWidth, bool full)
    {
        var width = double.IsFinite(listWidth) ? listWidth : 500d;
        var available = Math.Max(180d, width - 34d);
        return full || available < TwoColumnAvailableThreshold
            ? Math.Max(160d, available - 8d)
            : Math.Max(120d, (available - 16d) / 2d);
    }
    public static int PageCount(int count) => Math.Max(1, (Math.Max(0, count) + PageSize - 1) / PageSize);
    public static int ClampPage(int page, int count) => Math.Clamp(page, 0, PageCount(count) - 1);
    public static int PageForIndex(int index, int count)
        => ClampPage(Math.Max(0, index) / PageSize, count);
    public static List<T> TakePage<T>(IReadOnlyList<T> items, int page)
    {
        var start = ClampPage(page, items.Count) * PageSize;
        var result = new List<T>(Math.Min(PageSize, Math.Max(0, items.Count - start)));
        for (var i = start; i < items.Count && i - start < PageSize; i++) result.Add(items[i]);
        return result;
    }
}
