using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
#if !HEADLESS_TEST
using System.Windows.Media.Imaging;
#endif

namespace ClipShelf;

public sealed class ClipItem : INotifyPropertyChanged
{
    private bool _favorite;
    private bool _isTemporary;
    private string _category = "";
    private string _favoriteFolder = "";
    private string _temporaryFolder = "";
    private int _useCount = 1;
    private DateTime _lastUsedAt = DateTime.Now;
    private bool _isMarked;
    private bool _showSelector;
    private string _text = "";
    private bool _historyVisible;
    private DateTime? _pinnedAt;
    private bool _isContentExpanded;
    private bool _isDeleted;
    private DateTime? _deletedAt;
    private bool _isPendingPurge;
    private DateTime? _pendingPurgeAt;

    private string _title = "";
    private string _note = "";
    private bool _compact;
    private bool _isEditingText;
    private bool _isEditingTitle;
    private string _draftText = "";
    private string _draftTitle = "";
    public string Title { get => _title; set { _title = value ?? ""; OnPropertyChanged(); OnPropertyChanged(nameof(CardHeading)); } }
    public string Note { get => _note; set { _note = value ?? ""; OnPropertyChanged(); } }
    [JsonIgnore] public string CardHeading
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Title)) return Title;
            var summary = DisplayText.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return summary.Length > 48 ? summary[..48] + "…" : summary;
        }
    }
    [JsonIgnore] public bool Compact
    {
        get => _compact;
        set { if (_compact == value) return; _compact = value; OnPropertyChanged(); OnPropertyChanged(nameof(ContentMaxHeight)); }
    }
    // Drafts are presentation state only. Typing, undo, cancelling and background
    // settings saves must never write an unfinished edit to history.json.
    [JsonIgnore] public bool IsEditingText
    {
        get => _isEditingText;
        set { if (_isEditingText == value) return; _isEditingText = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsTextReadOnly)); OnPropertyChanged(nameof(IsInlineEditing)); OnPropertyChanged(nameof(EditorText)); OnPropertyChanged(nameof(ContentMaxHeight)); }
    }
    [JsonIgnore] public bool IsEditingTitle
    {
        get => _isEditingTitle;
        set { _isEditingTitle = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsInlineEditing)); }
    }
    [JsonIgnore] public bool IsInlineEditing => IsEditingText || IsEditingTitle;
    [JsonIgnore] public bool IsTextReadOnly => !IsEditingText;
    [JsonIgnore] public string DraftText
    {
        get => _draftText;
        set { var text = value ?? ""; if (_draftText == text) return; _draftText = text; OnPropertyChanged(); OnPropertyChanged(nameof(EditorText)); }
    }
    [JsonIgnore] public string DraftTitle
    {
        get => _draftTitle;
        set { _draftTitle = value ?? ""; OnPropertyChanged(); }
    }
    [JsonIgnore] public string EditorText
    {
        get => IsEditingText ? DraftText : DisplayText;
        set { if (IsEditingText) DraftText = value; }
    }
    [JsonIgnore] public string SearchExcerpt { get; set; } = "";
    [JsonIgnore] public string AttachmentText { get; set; } = "";
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Kind { get; set; } = "Text"; // Text | Image | Files
    public string Text
    {
        get => _text;
        set
        {
            _text = value ?? "";
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayText));
            OnPropertyChanged(nameof(CardHeading));
            if (!IsEditingText) OnPropertyChanged(nameof(EditorText));
        }
    }
    public List<string> Files { get; set; } = new();
    public string? ImagePath { get; set; }
    public bool ManagedByClipShelf { get; set; }
    public string ContentHash { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // v2.5+: History is a recent-use surface, independent from folder ownership.
    // A categorized/favorited item can therefore appear in History without becoming
    // a duplicate object. Removing that History entry only clears this flag.
    public bool HistoryVisible
    {
        get => _historyVisible;
        set { _historyVisible = value; OnPropertyChanged(); }
    }


    // Quick-picker pin is independent from Favorite and Category; null for old records.
    public DateTime? PinnedAt
    {
        get => _pinnedAt;
        set { if (_pinnedAt == value) return; _pinnedAt = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsPinned)); }
    }
    [JsonIgnore] public bool IsPinned => PinnedAt.HasValue;

    // v2.7: safe-delete lifecycle. Soft-deleted items remain serialized and keep
    // their folder/favorite/history placement metadata until restore or purge.
    public bool IsDeleted
    {
        get => _isDeleted;
        set
        {
            _isDeleted = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DeletedAtText));
        }
    }

    public DateTime? DeletedAt
    {
        get => _deletedAt;
        set
        {
            _deletedAt = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DeletedAtText));
        }
    }

    // Permanent deletion gets a short grace period. While pending, the item is hidden
    // from Recently Deleted but its payload is not physically removed yet.
    public bool IsPendingPurge
    {
        get => _isPendingPurge;
        set
        {
            _isPendingPurge = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DeletedAtText));
        }
    }

    public DateTime? PendingPurgeAt
    {
        get => _pendingPurgeAt;
        set { _pendingPurgeAt = value; OnPropertyChanged(); }
    }

    public DateTime LastUsedAt
    {
        get => _lastUsedAt;
        set { _lastUsedAt = value; OnPropertyChanged(); OnPropertyChanged(nameof(TimeText)); }
    }

    public int UseCount
    {
        get => _useCount;
        set { _useCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(UsageText)); }
    }

    public string SourceApp { get; set; } = "";
    public string SourceTitle { get; set; } = "";

    public bool Favorite
    {
        get => _favorite;
        set { _favorite = value; OnPropertyChanged(); OnPropertyChanged(nameof(FavoriteGlyph)); }
    }

    public bool IsTemporary
    {
        get => _isTemporary;
        set { _isTemporary = value; OnPropertyChanged(); }
    }

    public string Category
    {
        get => _category;
        set { _category = value ?? ""; OnPropertyChanged(); OnPropertyChanged(nameof(CategoryText)); }
    }

    // 收藏與暫存是獨立維度，不能佔用 Category，否則從收藏分類後會破壞原本分類。
    // 這兩個欄位只記錄各自根節點底下的子資料夾相對路徑。
    public string FavoriteFolder
    {
        get => _favoriteFolder;
        set { _favoriteFolder = value ?? ""; OnPropertyChanged(); }
    }

    public string TemporaryFolder
    {
        get => _temporaryFolder;
        set { _temporaryFolder = value ?? ""; OnPropertyChanged(); }
    }


    [JsonIgnore]
    public bool IsContentExpanded
    {
        get => _isContentExpanded;
        set
        {
            _isContentExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ContentMaxHeight));
        }
    }

    [JsonIgnore]
    public double ContentMaxHeight => IsContentExpanded || IsEditingText ? 480 : Compact ? 38 : 112;

    [JsonIgnore]
    public bool IsMarked
    {
        get => _isMarked;
        set { _isMarked = value; OnPropertyChanged(); }
    }

    [JsonIgnore]
    public bool ShowSelector
    {
        get => _showSelector;
        set { _showSelector = value; OnPropertyChanged(); }
    }

    [JsonIgnore]
    public string FavoriteGlyph => Favorite ? "★" : "☆";

    [JsonIgnore]
    public string UsageText => UseCount <= 1 ? "" : $"×{UseCount}";

    [JsonIgnore]
    public string TimeText => LastUsedAt.ToString("yyyy/MM/dd HH:mm:ss");

    [JsonIgnore]
    public string DeletedAtText => IsPendingPurge
        ? "永久刪除倒數中 · 仍可復原"
        : IsDeleted && DeletedAt.HasValue
            ? $"刪除於 {DeletedAt.Value:yyyy/MM/dd HH:mm}"
            : "";

    [JsonIgnore]
    public string SourceText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SourceApp)) return "未知來源";
            if (string.IsNullOrWhiteSpace(SourceTitle)) return SourceApp;
            var title = SourceTitle.Length > 36 ? SourceTitle[..36] + "…" : SourceTitle;
            return $"{SourceApp} · {title}";
        }
    }

    [JsonIgnore]
    public string CategoryText => string.IsNullOrWhiteSpace(Category) ? "" : $"#{Category}";

    [JsonIgnore]
    public string KindLabel => Kind switch
    {
        "Image" => "圖片",
        "Files" => "檔案",
        _ => "文字"
    };

    [JsonIgnore]
    public string DisplayText
    {
        get
        {
            return Kind switch
            {
                "Image" => "[圖片]",
                "Files" => Files.Count == 0 ? "[檔案]" : string.Join(Environment.NewLine, Files.Take(4).Select(p => $"📄 {Path.GetFileName(p)}")) + (Files.Count > 4 ? $"\n…另有 {Files.Count - 4} 個" : ""),
                _ => Text
            };
        }
    }

#if !HEADLESS_TEST
    [JsonIgnore]
    public BitmapImage? PreviewImage
    {
        get
        {
            if (Kind != "Image" || string.IsNullOrWhiteSpace(ImagePath) || !File.Exists(ImagePath)) return null;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(ImagePath, UriKind.Absolute);
                bmp.DecodePixelWidth = 240;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }
    }

#endif
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
