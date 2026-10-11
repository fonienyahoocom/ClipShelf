ClipShelf v2.1

Fixes:
1. Desktop bubble no longer disappears after the full UI closes.
   The Core now remembers a pending bubble request until ClipShelf.UI.exe has really exited.
2. Full manager is Alt+Tab reachable while it is open.
3. Alt+Tab / Ctrl+Alt+Tab / Win+Tab away from ClipShelf keeps the full manager alive so you can switch straight back.
4. Mouse-clicking outside ClipShelf still collapses it to the desktop bubble.
5. Minus/minimize still collapses to the desktop bubble.
6. Ctrl+~ remains true hide.
7. When collapsed/hidden, ClipShelf.UI.exe exits, so it does not occupy Alt+Tab when you are not using the full manager.

User data remains under %LOCALAPPDATA%\ClipShelf and is not stored in the version folder.
