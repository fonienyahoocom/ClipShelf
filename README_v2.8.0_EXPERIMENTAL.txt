ClipShelf v2.8.0 EXPERIMENTAL — POINTER TEXT CAPTURE
=====================================================

STATUS
------
Experimental integration candidate.
Based on ClipShelf v2.7.0 Safe Delete.
The lightweight ClipShelf Core source is intentionally unchanged from v2.7.0.

WHAT THIS VERSION ADDS
----------------------
1. Settings -> 實驗功能 -> 實驗性：滑鼠位置文字擷取.
2. When enabled, the header shows: β 指向擷取.
3. Clicking β 指向擷取 temporarily hides ClipShelf and launches an isolated helper process.
4. The helper waits for one left click on target text and suppresses that click so the target program is not invoked.
5. The helper uses only Windows local UI Automation:
     AutomationElement.FromPoint
     TextPattern.RangeFromPoint
     ExpandToEnclosingUnit(Character / Word / Line / Paragraph)
6. Result returns to ClipShelf as JSON. No clipboard transfer is used for capture.
7. A preview dialog lets the user choose 段落 / 單行 / 字詞 / 字元, edit the text, then 加入歷史.
8. If the target program does not expose usable TextPattern, this single capture reports unavailable. No fallback is silently activated.

ISOLATION / FAILURE BOUNDARY
----------------------------
- UI Automation and global mouse/keyboard hooks live in ClipShelf.ExperimentalCapture.exe, NOT in ClipShelf.Core.exe.
- The experimental helper is built to ClipShelf_READY\experimental.
- Helper build is optional in BUILD HARNESS 2. A helper build failure does NOT fail the required Core/UI build.
- Closing the Experimental switch hides the feature and leaves ordinary ClipShelf behavior unchanged.
- No IA2/MSAA fallback.
- No OCR.
- No cloud API.
- No API key.
- No account requirement.
- No token/usage fee.
- No program-specific compatibility patch.

LOCAL-FIRST PRODUCT BOUNDARY
----------------------------
Allowed: Windows/.NET local system APIs such as UI Automation.
Not allowed by default: any remote service that needs API keys, accounts, cloud upload, per-call/token cost, or third-party service availability.

BUILD
-----
1. Exit older ClipShelf processes.
2. Run BUILD_EXE.cmd.
3. Required success output remains:
     ClipShelf_READY\ClipShelf.exe
     ClipShelf_READY\ui\ClipShelf.UI.exe
4. Experimental helper, when available:
     ClipShelf_READY\experimental\ClipShelf.ExperimentalCapture.exe
5. On required Core/UI build failure, send:
     BUILD失敗_把這個傳給ChatGPT.txt

USE
---
1. Start ClipShelf_READY\ClipShelf.exe.
2. Open Settings.
3. Enable 實驗性：滑鼠位置文字擷取 and Save.
4. Click β 指向擷取.
5. Move the mouse over visible text in another application and left-click once.
6. Review the detected text, choose the desired range, optionally edit it, then press 加入歷史.
7. Esc cancels capture.

EVIDENCE BASIS
--------------
This integration uses the exact RangeFromPoint -> ExpandToEnclosingUnit route that produced two real Windows 10 + Edge + ChatGPT target-resolution successes in TextProbe 0.2A.
It does NOT claim universal application compatibility.
Real everyday use of this Experimental version is the next evidence source.
