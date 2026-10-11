namespace ClipShelfCore;

internal static class EventNames
{
    public const string ToggleUi = @"Local\ClipShelf.Core.ToggleUI";
    public const string QuickToggleUi = @"Local\ClipShelf.Core.QuickToggleUI";
    public const string ShowUi = @"Local\ClipShelf.Core.ShowUI";
    public const string ShowBubble = @"Local\ClipShelf.Core.ShowBubble";
    public const string UiClosed = @"Local\ClipShelf.Core.UiClosed";
    public const string OpenUiRequest = @"Local\ClipShelf.Core.OpenUIRequest";
    public const string ToggleCapture = @"Local\ClipShelf.Core.ToggleCapture";
    public const string StateChanged = @"Local\ClipShelf.Core.StateChanged";

    // Commands received by the lightweight Core (tray / Jump List / second instance).
    public const string SetNonInterferenceModeRequest = @"Local\ClipShelf.Core.SetNonInterferenceMode";
    public const string SetNormalModeRequest = @"Local\ClipShelf.Core.SetNormalMode";

    // Commands sent from Core to a currently running full UI.
    public const string UiApplyNonInterferenceMode = @"Local\ClipShelf.Core.UiApplyNonInterferenceMode";
    public const string UiApplyNormalMode = @"Local\ClipShelf.Core.UiApplyNormalMode";
}
