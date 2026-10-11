ClipShelf v2.2

This version keeps the v2.x lightweight Core + on-demand full UI architecture.

Changes in v2.2:
- Folder dragging now starts from a dedicated drag grip, instead of the whole row.
- Clearer reorder feedback: top/bottom insertion line; center dwell to nest.
- All custom folders use the same drag path, including upper/middle rows.
- Clip records now have a dedicated drag grip.
- Visible text is selectable like normal Windows text.
- Ctrl+C copies only the selected text without creating a duplicate ClipShelf history item.
- Right-click selected text offers Copy selected / Copy whole / Edit.
- Double-click inside selectable text selects text and does not paste the whole record.

Build:
1. Exit older ClipShelf from the tray.
2. Run BUILD_EXE.cmd.
3. Start only ClipShelf_READY\ClipShelf.exe.

User history remains outside the program folder under %LOCALAPPDATA%\ClipShelf.
