ClipShelf v2.6.5 - PROBE BUILD REPAIR + PERMANENT ERROR HANDOFF
================================================================

Based on v2.6.4. Normal ClipShelf behavior is intentionally unchanged.

WHAT THIS VERSION FIXES
-----------------------
1. The user's real v2.6.4 build proved that Core and UI compile, but TextProbe
   failed because the managed LegacyIAccessiblePattern type is not available in
   the .NET 8 WindowsDesktop reference set on that machine.

2. Probe 1 no longer names or instantiates LegacyIAccessiblePattern. It still
   records every supported UIA pattern and explicitly reports whether a pattern
   name containing LegacyIAccessible is exposed. Raw MSAA/IA2 traversal remains
   deferred to Probe 2 by design.

3. Every future build failure is now standardized to the same handoff filename:
     BUILD失敗_把這個傳給ChatGPT.txt
   This filename is also recorded as a permanent rule in DEVELOPMENT_HISTORY.md.

HOW TO BUILD
------------
1. Fully exit older ClipShelf versions.
2. Run BUILD_EXE.cmd.
3. On BUILD SUCCESS, start ONLY:
     ClipShelf_READY\ClipShelf.exe
4. On BUILD FAILED, Notepad will open:
     BUILD失敗_把這個傳給ChatGPT.txt
   Its contents are also copied to the clipboard. Send that file or Ctrl+V into
   the main ChatGPT development chat.

TEXT PROBE
----------
After a successful build:
  desktop floating ClipShelf icon -> right-click -> 擷取文字

Probe remains read-only and one-shot. It does not modify History or clipboard.

IMPORTANT
---------
This package has been source-checked, but this environment cannot execute a
Windows .NET 8/WPF/UIAutomation build. The user's Windows build remains the
final compiler validation.
