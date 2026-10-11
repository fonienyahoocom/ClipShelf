using System;
using System.Threading;

namespace ClipShelf;

internal sealed class CoreBridge : IDisposable
{
    private const string ToggleUiName = @"Local\ClipShelf.Core.ToggleUI";
    private const string ShowUiName = @"Local\ClipShelf.Core.ShowUI";
    private const string QuickToggleUiName = @"Local\ClipShelf.Core.QuickToggleUI";
    private const string ShowBubbleName = @"Local\ClipShelf.Core.ShowBubble";
    private const string UiClosedName = @"Local\ClipShelf.Core.UiClosed";
    private const string ToggleCaptureName = @"Local\ClipShelf.Core.ToggleCapture";
    private const string StateChangedName = @"Local\ClipShelf.Core.StateChanged";
    private const string UiApplyNonInterferenceName = @"Local\ClipShelf.Core.UiApplyNonInterferenceMode";
    private const string UiApplyNormalName = @"Local\ClipShelf.Core.UiApplyNormalMode";

    private readonly EventWaitHandle _toggleUi;
    private readonly EventWaitHandle _quickToggleUi;
    private readonly EventWaitHandle _showUi;
    private readonly EventWaitHandle _showBubble;
    private readonly EventWaitHandle _uiClosed;
    private readonly EventWaitHandle _toggleCapture;
    private readonly EventWaitHandle _stateChanged;
    private readonly EventWaitHandle _applyNonInterference;
    private readonly EventWaitHandle _applyNormal;
    private readonly RegisteredWaitHandle _toggleWait;
    private readonly RegisteredWaitHandle _quickToggleWait;
    private readonly RegisteredWaitHandle _showWait;
    private readonly RegisteredWaitHandle _toggleCaptureWait;
    private readonly RegisteredWaitHandle _applyNonInterferenceWait;
    private readonly RegisteredWaitHandle _applyNormalWait;

    public CoreBridge(
        Action toggleRequested,
        Action quickToggleRequested,
        Action showRequested,
        Action toggleCaptureRequested,
        Action nonInterferenceRequested,
        Action normalModeRequested)
    {
        _toggleUi = new EventWaitHandle(false, EventResetMode.AutoReset, ToggleUiName);
        _quickToggleUi = new EventWaitHandle(false, EventResetMode.AutoReset, QuickToggleUiName);
        _showUi = new EventWaitHandle(false, EventResetMode.AutoReset, ShowUiName);
        _showBubble = new EventWaitHandle(false, EventResetMode.AutoReset, ShowBubbleName);
        _uiClosed = new EventWaitHandle(false, EventResetMode.AutoReset, UiClosedName);
        _toggleCapture = new EventWaitHandle(false, EventResetMode.AutoReset, ToggleCaptureName);
        _stateChanged = new EventWaitHandle(false, EventResetMode.AutoReset, StateChangedName);
        _applyNonInterference = new EventWaitHandle(false, EventResetMode.AutoReset, UiApplyNonInterferenceName);
        _applyNormal = new EventWaitHandle(false, EventResetMode.AutoReset, UiApplyNormalName);

        _toggleWait = ThreadPool.RegisterWaitForSingleObject(_toggleUi, (_, _) => toggleRequested(), null, Timeout.Infinite, false);
        _quickToggleWait = ThreadPool.RegisterWaitForSingleObject(_quickToggleUi, (_, _) => quickToggleRequested(), null, Timeout.Infinite, false);
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showUi, (_, _) => showRequested(), null, Timeout.Infinite, false);
        _toggleCaptureWait = ThreadPool.RegisterWaitForSingleObject(_toggleCapture, (_, _) => toggleCaptureRequested(), null, Timeout.Infinite, false);
        _applyNonInterferenceWait = ThreadPool.RegisterWaitForSingleObject(_applyNonInterference, (_, _) => nonInterferenceRequested(), null, Timeout.Infinite, false);
        _applyNormalWait = ThreadPool.RegisterWaitForSingleObject(_applyNormal, (_, _) => normalModeRequested(), null, Timeout.Infinite, false);
    }

    public void ShowBubble() => _showBubble.Set();
    public void NotifyUiClosed() => _uiClosed.Set();
    public void NotifyStateChanged() => _stateChanged.Set();

    public void Dispose()
    {
        try { _toggleWait.Unregister(null); } catch { }
        try { _quickToggleWait.Unregister(null); } catch { }
        try { _showWait.Unregister(null); } catch { }
        try { _toggleCaptureWait.Unregister(null); } catch { }
        try { _applyNonInterferenceWait.Unregister(null); } catch { }
        try { _applyNormalWait.Unregister(null); } catch { }
        _toggleUi.Dispose();
        _quickToggleUi.Dispose();
        _showUi.Dispose();
        _showBubble.Dispose();
        _uiClosed.Dispose();
        _toggleCapture.Dispose();
        _stateChanged.Dispose();
        _applyNonInterference.Dispose();
        _applyNormal.Dispose();
    }
}
