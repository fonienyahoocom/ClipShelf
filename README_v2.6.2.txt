ClipShelf v2.6.2 - TEXT PROBE BUILD FIX + CUMULATIVE DEVELOPMENT HISTORY
=======================================================================

This build is based on v2.6.1 and does not change normal ClipShelf behavior.

Fixes
-----
1. TextProbe compile fix
   - Adds the missing System.Windows.Automation.Text namespace.
   - TextPatternRange is defined in that namespace.

2. Cumulative development memory
   - Adds DEVELOPMENT_HISTORY.md.
   - Every future package should carry the complete file forward and append the new version.
   - Older version folders can therefore be deleted without losing the development trail, as long as the newest package/history file is kept.
   - A successful build also copies DEVELOPMENT_HISTORY.md into ClipShelf_READY.

3. Safer failed-build behavior
   - BUILD_EXE.cmd removes partial ClipShelf_READY output if any of Core/UI/TextProbe fails.
   - A failed build therefore intentionally does NOT leave a partial installation that looks usable.
   - On success, the script verifies ClipShelf.exe, ClipShelf.UI.exe and ClipShelf.TextProbe.exe before declaring BUILD SUCCESS.

Probe report cleanup remains unchanged:
- newest 20 reports retained
- reports older than 30 days removed during a capture
- no background cleanup task or timer

Build
-----
1. Exit the older ClipShelf completely.
2. Run BUILD_EXE.cmd.
3. Only after BUILD SUCCESS, start ClipShelf_READY\ClipShelf.exe.
