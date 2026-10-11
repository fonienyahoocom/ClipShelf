ClipShelf v2.1 - Warm UI + folder drag reliability

1. Low-resource architecture is preserved:
   - ClipShelf.Core remains the always-on lightweight process.
   - The full WPF UI is not kept forever.

2. Faster reopen without returning to the old heavy architecture:
   - Minimize / click outside -> UI is hidden and kept warm for 45 seconds.
   - Reopen during those 45 seconds -> the existing UI is shown immediately.
   - After 45 seconds of no use -> ClipShelf.UI.exe really exits; only Core remains.
   - Ctrl+~ true hide -> full UI exits immediately; no warm grace period.

3. Folder dragging reliability:
   - Custom-folder mouse gesture is captured across rows.
   - PreviewMouseMove is used so child controls cannot steal the gesture.
   - Still requires a short hold + clear movement, preserving deliberate drag rhythm.
   - Center nesting dwell remains in place.

User data location and format are unchanged.
