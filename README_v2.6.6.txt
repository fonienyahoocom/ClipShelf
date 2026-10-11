ClipShelf v2.6.6 - STARTUP CRASH HANDOFF
=========================================

This version does not change the text-capture product behavior.
It adds startup crash diagnostics after v2.6.5 was observed to exit immediately on the user's Windows system.

Runtime crash handoff
---------------------
If ClipShelf.Core encounters an unhandled startup/runtime exception, it creates:

  程式閃退_把這個傳給ChatGPT.txt

Preferred location: beside ClipShelf.exe in ClipShelf_READY.
Fallback locations: Desktop, then Windows TEMP.
The report is opened in Notepad and its contents are also sent to the Windows clipboard when possible.

Build failure handoff remains permanently named:

  BUILD失敗_把這個傳給ChatGPT.txt

No manual log search should be required.

Folder-lock robustness
----------------------
BUILD_EXE.cmd changes its working directory to %TEMP% before exiting so the build console does not keep the version folder as its current directory.
