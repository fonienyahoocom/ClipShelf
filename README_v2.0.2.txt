ClipShelf v2.0.2 - lifecycle / clean-exit fix

What was fixed
---------------
1. The full WPF UI previously used ShutdownMode=OnExplicitShutdown.
   Closing MainWindow therefore could leave ClipShelf.UI.exe alive invisibly.
   That hidden process could keep ClipShelf.UI.dll locked.

2. The UI now uses ShutdownMode=OnMainWindowClose.
   Hide/minimize/true-hide closes the disposable full UI process; the lightweight
   ClipShelf.exe core remains responsible for background capture and Ctrl+~.

3. "Exit ClipShelf" now means a real application exit.
   The core asks ClipShelf.UI.exe to save and close, waits for it, then tries a
   normal WM_CLOSE. Only if an unresponsive UI still refuses to end does the core
   terminate it as a last resort.

Expected states
---------------
- Normal full UI: ClipShelf.exe + ui\ClipShelf.UI.exe
- UI hidden/minimized to bubble: only ClipShelf.exe should remain
- True hide (Ctrl+~): only ClipShelf.exe should remain
- Exit ClipShelf: neither ClipShelf.exe nor ClipShelf.UI.exe should remain

Data location is unchanged:
%LOCALAPPDATA%\ClipShelf
Deleting the extracted program-version folder does not delete that data folder.

Build
-----
Run BUILD_EXE.cmd, then start ONLY ClipShelf_READY\ClipShelf.exe.
