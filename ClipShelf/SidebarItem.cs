using System;
#if !HEADLESS_TEST
using System.Windows;
#endif

namespace ClipShelf;

public sealed class SidebarItem
{
    public const string FavoritePrefix = "::favorite::";
    public const string TemporaryPrefix = "::temporary::";
    public const string AiPrefix = "::ai::";
    public const string QuickPrefix = "::quick::";

    public string Kind { get; init; } = "Folder"; // History | Favorite | Temporary | AI | Quick | Trash | Folder
    public string Path { get; init; } = "";        // Internal folder key. Special roots use ::favorite::/...
    public string Name { get; init; } = "";
    public string Icon { get; init; } = "📁";
    public int Depth { get; init; }
    public string Scope { get; init; } = "Custom"; // Custom | Favorite | Temporary | AI | Quick
    public bool HasChildren { get; init; }
    public bool IsExpanded { get; init; } = true;

    public bool IsCustomFolder => string.Equals(Kind, "Folder", StringComparison.Ordinal);
    public bool IsFolderSection => IsCustomFolder || Kind is "Favorite" or "Temporary" or "AI" or "Quick";
    public bool CanDragSidebar => IsCustomFolder || Kind is "Favorite" or "Temporary" or "AI" or "Quick";
    public bool IsSystemFolderRoot => !IsCustomFolder && Kind is "Favorite" or "Temporary" or "AI" or "Quick";
    public bool IsTopLevelCustomFolder => IsCustomFolder && Scope == "Custom" && Depth == 0;
    public bool IsFavoriteFolder => IsCustomFolder && Scope == "Favorite";
    public bool IsTemporaryFolder => IsCustomFolder && Scope == "Temporary";
    public bool IsAiFolder => IsCustomFolder && Scope == "AI";
    public bool IsQuickFolder => IsCustomFolder && Scope == "Quick";
    public bool IsClassificationFolder => IsCustomFolder && Scope is "Custom" or "AI" or "Quick";
#if !HEADLESS_TEST
    public Thickness Indent => new(Math.Max(0, Depth) * 14, 0, 0, 0);
#endif
    public string DisplayName => Name;
    public string ExpansionGlyph => HasChildren ? (IsExpanded ? "▾" : "▸") : "";

    public override string ToString() => IsCustomFolder ? Path : Kind;

    public static SidebarItem System(string kind, string icon, string name, bool hasChildren = false, bool isExpanded = true)
        => new() { Kind = kind, Scope = kind, Icon = icon, Name = name, Depth = 0, HasChildren = hasChildren, IsExpanded = isExpanded };

    public static SidebarItem Folder(string path, bool hasChildren = false, bool isExpanded = true)
    {
        path = NormalizePath(path);
        var scope = GetScope(path);
        var relative = GetRelativePath(path);
        var parts = string.IsNullOrWhiteSpace(relative)
            ? Array.Empty<string>()
            : relative.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var name = parts.Length == 0 ? "" : parts[^1];

        // Custom root folders are depth 0. Folders under built-in roots are depth 1,
        // because the built-in root itself is already visible in the sidebar.
        var depth = scope == "Custom" ? Math.Max(0, parts.Length - 1) : Math.Max(1, parts.Length);
        var icon = depth > 0 ? "↳ 📁" : "📁";
        return new SidebarItem { Kind = "Folder", Scope = scope, Path = path, Name = name, Icon = icon, Depth = depth, HasChildren = hasChildren, IsExpanded = isExpanded };
    }

    public static string RootKeyForKind(string kind) => kind switch
    {
        "Favorite" => FavoritePrefix,
        "Temporary" => TemporaryPrefix,
        "AI" => AiPrefix,
        "Quick" => QuickPrefix,
        _ => ""
    };

    public static string GetScope(string? path)
    {
        path = NormalizePath(path);
        if (path.Equals(FavoritePrefix, StringComparison.OrdinalIgnoreCase) || path.StartsWith(FavoritePrefix + "/", StringComparison.OrdinalIgnoreCase)) return "Favorite";
        if (path.Equals(TemporaryPrefix, StringComparison.OrdinalIgnoreCase) || path.StartsWith(TemporaryPrefix + "/", StringComparison.OrdinalIgnoreCase)) return "Temporary";
        if (path.Equals(AiPrefix, StringComparison.OrdinalIgnoreCase) || path.StartsWith(AiPrefix + "/", StringComparison.OrdinalIgnoreCase)) return "AI";
        if (path.Equals(QuickPrefix, StringComparison.OrdinalIgnoreCase) || path.StartsWith(QuickPrefix + "/", StringComparison.OrdinalIgnoreCase)) return "Quick";
        return "Custom";
    }

    public static string GetScopeRoot(string? path)
    {
        return GetScope(path) switch
        {
            "Favorite" => FavoritePrefix,
            "Temporary" => TemporaryPrefix,
            "AI" => AiPrefix,
            "Quick" => QuickPrefix,
            _ => ""
        };
    }

    public static string GetRelativePath(string? path)
    {
        path = NormalizePath(path);
        var root = GetScopeRoot(path);
        if (string.IsNullOrWhiteSpace(root)) return path;
        if (path.Equals(root, StringComparison.OrdinalIgnoreCase)) return "";
        return path[(root.Length + 1)..];
    }

    public static string MakeScopedPath(string kind, string relative)
    {
        var root = RootKeyForKind(kind);
        relative = NormalizePath(relative);
        return string.IsNullOrWhiteSpace(root) ? relative : string.IsNullOrWhiteSpace(relative) ? root : $"{root}/{relative}";
    }

    public static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join("/", parts);
    }
}
