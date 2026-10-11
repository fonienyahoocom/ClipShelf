ClipShelf v2.0 - upstream architecture refactor

This version keeps the v1.1 full interface and data format, but splits the program into two processes:

1) ClipShelf.exe
   Lightweight background core.
   - Clipboard event listener
   - Ctrl+~ global hotkey
   - tray icon
   - draggable desktop bubble
   - automatic history capture/cleanup

2) ui\ClipShelf.UI.exe
   Full WPF interface.
   - history / search / folders / nested folders
   - favorites / temporary / AI / quick phrases
   - drag and drop / multi-select / imported files
   - settings and full management UI

Resource behavior:
- While the full UI is open, the UI owns clipboard capture and the core stays passive.
- Clicking outside, minimizing, or switching away closes the full UI process and leaves only the lightweight core + optional bubble.
- Ctrl+~ while the full UI is open truly hides it (the full UI process exits; no bubble).
- Ctrl+~ from another program launches the full UI again.
- ClipShelf is excluded from Alt+Tab / Ctrl+Alt+Tab; use Ctrl+~ to move between ClipShelf and the working app.

DATA SAFETY
The user data location is unchanged:
%LOCALAPPDATA%\ClipShelf\history.json

The program folder can still be deleted as a whole when upgrading. User history, favorites, folders, settings, and managed storage remain outside the ZIP/extracted program folder.

v2.0 also creates a daily safety backup when the lightweight core starts:
%LOCALAPPDATA%\ClipShelf\backups\history_YYYYMMDD_HHMMSS.json

BUILD
Run BUILD_EXE.cmd.
After a successful build, start only:
ClipShelf_READY\ClipShelf.exe

Do not manually start ui\ClipShelf.UI.exe unless troubleshooting; the core launches it automatically.
