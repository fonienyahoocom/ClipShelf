ClipShelf v2.0.1 - wake-up reliability fix

This build keeps the v2.0 light-core architecture and the same user data path.
Fixes:
- Full UI no longer immediately closes during the tray-menu/Explorer focus hand-off.
- Core grants the UI foreground permission and re-signals ShowUI when the WPF HWND is ready.
- UI retries foreground activation once after startup without permanent polling.
- Existing history/favorites/folders/settings remain in %LOCALAPPDATA%\ClipShelf.

Build with BUILD_EXE.cmd and run only ClipShelf_READY\ClipShelf.exe.
