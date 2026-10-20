using System.Collections.Generic;

namespace ClipShelf;

public sealed class AppSettings
{
    public System.DateTime? CaptureResumeAt { get; set; }
    public bool CompactView { get; set; }
    // Independent manager arrangement; legacy settings default to one vertical column.
    public bool GridLayout { get; set; } = false;
    public double ReadingFontSize { get; set; } = 16;
    public bool GlobalSearch { get; set; } = true;
    public bool CaptureText { get; set; } = true;
    public bool CaptureImages { get; set; } = true;
    public bool CaptureFiles { get; set; } = true;
    public bool CapturePaused { get; set; } = false;

    // Window-switching behavior. false = normal taskbar / Alt+Tab mode; true = non-interference bubble mode.
    public bool SwitchNonInterferenceMode { get; set; } = false;

    // v2.5 data-semantic migration. 0 = legacy History=inbox model, 1 = independent recent-history model.
    public int HistoryModelVersion { get; set; } = 0;

    // Order of top-level sidebar sections after History. Tokens are sys:<Kind> or folder:<path>.
    public List<string> SidebarRootOrder { get; set; } = new();

    // v2.5.1: nodes the user collapsed in the sidebar. Keys are sys:<Kind> or folder:<path>.
    // This is purely UI state; it never changes or removes folder data.
    public List<string> CollapsedSidebarNodes { get; set; } = new();

    // Folder used for managed attachments and captured images.
    // Empty means %LOCALAPPDATA%\ClipShelf\storage.
    public string StorageDirectory { get; set; } = "";

    // 0 = unlimited. The limit only applies to ordinary history items.
    // Favorites, temporary items and categorized items are protected.
    public int MaxRegularItems { get; set; } = 5000;

    // v2.7: soft-deleted items stay in 最近刪除 for this many days.
    // 0 = keep until the user permanently deletes them.
    public int TrashRetentionDays { get; set; } = 30;

    // v2.8 Experimental: local-only pointer text capture via Windows UI Automation.
    // This switch only exposes the isolated helper; the stable Core does not depend on it.
    public bool ExperimentalPointerTextCapture { get; set; } = false;

    // Main window layout.  These values make a manually resized ClipShelf
    // come back at the same practical size on the next launch.
    public double WindowWidth { get; set; } = 930;
    public double WindowHeight { get; set; } = 620;
    public double WindowLeft { get; set; }
    public double WindowTop { get; set; }
    public bool HasWindowPlacement { get; set; }
    public double SidebarWidth { get; set; } = 176;

    // Position of the draggable desktop mini bubble shown by normal minimize.
    public double MiniLeft { get; set; }
    public double MiniTop { get; set; }
    public bool HasMiniPlacement { get; set; }

    public bool FloatingIconEnabled { get; set; } = true;
    // Legacy settings keep Quick Picker as the Ctrl+~ default.
    public bool PreferQuickPicker { get; set; } = true;
    public bool PickerCloseOnDeactivate { get; set; } = true;
    public double PickerWidth { get; set; } = 440;
    public double PickerHeight { get; set; } = 570;
    public double PickerLeft { get; set; }
    public double PickerTop { get; set; }
    public bool HasPickerPlacement { get; set; }
    public int PickerHotkeyModifiers { get; set; } = 2;
    public int PickerHotkeyKey { get; set; } = 0xC0;

    public AppSettings Clone() => new()
    {
        FloatingIconEnabled = FloatingIconEnabled,
        PreferQuickPicker = PreferQuickPicker,
        PickerCloseOnDeactivate = PickerCloseOnDeactivate,
        PickerWidth = PickerWidth, PickerHeight = PickerHeight,
        PickerLeft = PickerLeft, PickerTop = PickerTop, HasPickerPlacement = HasPickerPlacement,
        PickerHotkeyModifiers = PickerHotkeyModifiers, PickerHotkeyKey = PickerHotkeyKey,
        CaptureResumeAt = CaptureResumeAt,
        CompactView = CompactView,
        GridLayout = GridLayout,
        ReadingFontSize = ReadingFontSize,
        GlobalSearch = GlobalSearch,
        CaptureText = CaptureText,
        CaptureImages = CaptureImages,
        CaptureFiles = CaptureFiles,
        CapturePaused = CapturePaused,
        SwitchNonInterferenceMode = SwitchNonInterferenceMode,
        HistoryModelVersion = HistoryModelVersion,
        SidebarRootOrder = new List<string>(SidebarRootOrder ?? new()),
        CollapsedSidebarNodes = new List<string>(CollapsedSidebarNodes ?? new()),
        StorageDirectory = StorageDirectory,
        MaxRegularItems = MaxRegularItems,
        TrashRetentionDays = TrashRetentionDays,
        ExperimentalPointerTextCapture = ExperimentalPointerTextCapture,
        WindowWidth = WindowWidth,
        WindowHeight = WindowHeight,
        WindowLeft = WindowLeft,
        WindowTop = WindowTop,
        HasWindowPlacement = HasWindowPlacement,
        SidebarWidth = SidebarWidth,
        MiniLeft = MiniLeft,
        MiniTop = MiniTop,
        HasMiniPlacement = HasMiniPlacement
    };
}
