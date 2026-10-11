ClipShelf v2.6.4 - RESILIENT BUILD + TEXT CAPTURE PROBE
=========================================================

Based on v2.6.3. Normal ClipShelf behavior is intentionally unchanged.

WHAT THIS VERSION FIXES
-----------------------
1. v2.6.3 could enter BUILD FAILED and then ask Explorer to select
   SEND_TO_CHATGPT_BUILD_ERROR.txt even though that file had not been
   reliably created on the user's Windows machine.

2. v2.6.4 no longer depends on Explorer selection for error handoff.
   Build output is captured in TEMP first. On failure the package tries
   to create SEND_TO_CHATGPT_BUILD_ERROR.txt in:
     a) this package folder
     b) Desktop
     c) Windows TEMP
   The path is verified before it is used.

3. The failure report is opened directly in Notepad and its full contents
   are copied to the clipboard. If the handoff file cannot be created at
   all, the raw TEMP build log is opened and copied instead.

4. Probe source no longer explicitly depends on the
   System.Windows.Automation.Text namespace or TextPatternRangeEndpoint.
   RangeFromPoint remains diagnostic evidence only.

HOW TO BUILD
------------
1. Fully exit any older ClipShelf.
2. Run BUILD_EXE.cmd.
3. If BUILD SUCCESS appears, start ONLY:
     ClipShelf_READY\ClipShelf.exe
4. If BUILD FAILED appears, do not search folders. Notepad will open the
   diagnostic file automatically, and the same text will already be in
   your clipboard. Return to ChatGPT and press Ctrl+V, or drag the opened
   file into chat.

DEVELOPMENT MEMORY
------------------
DEVELOPMENT_HISTORY.md is cumulative and is copied into ClipShelf_READY on
a successful build. Keep the newest package and the history of earlier
versions remains available even after old extracted version folders are deleted.

IMPORTANT
---------
This environment cannot execute a Windows WPF/UIAutomation build. The
source/package has been checked, but actual Windows compilation remains the
final validation. This version improves failure recovery rather than claiming
that a build can never fail.
