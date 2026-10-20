using System;
using System.Collections.Generic;

namespace ClipShelf;

// The WPF WrapPanel does not virtualize. Never hand it an unbounded history list.
public static class CardGridPaging
{
    public const int PageSize = 24;
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
