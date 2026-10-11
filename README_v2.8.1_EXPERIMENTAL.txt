ClipShelf v2.8.1 EXPERIMENTAL — POINTER CONTEXT CAPTURE
=====================================================

This package keeps the successful v2.8.0 local UI Automation route and makes the useful capture range larger without adding OCR.

WHAT CHANGED
------------
- Current paragraph remains available.
- New: context with 1 paragraph before/after the pointer paragraph.
- New: context with 2 paragraphs before/after.
- New: larger semantic block with up to 5 paragraphs before/after, capped at about 8000 characters.
- The preview defaults to the ±1 paragraph context when the provider supports it.
- The preview shows the current character count.

The larger ranges are best-effort UI Automation only. If a provider cannot move paragraph endpoints, ClipShelf simply omits that larger choice and keeps the smaller proven choices.

QUICK BUILD
-----------
1. Exit older ClipShelf.
2. Run BUILD_EXE.cmd.
3. Confirm both BUILD SUCCESS and Experimental pointer text capture: AVAILABLE.
4. Start ClipShelf_READY\ClipShelf.exe.
5. Enable Settings -> 實驗性：滑鼠位置文字擷取.
6. Use β 指向擷取, click visible text, then choose the desired semantic range.

LOCAL-FIRST BOUNDARY
--------------------
No OCR is active in this version.
No cloud API, API key, account, token fee, or background screen scanner is added.

NEXT VERSION HANDOFF
--------------------
NEXT_OCR_STRATEGY.md is deliberately included in both the source package and ClipShelf_READY.
It records the approved next-stage OCR architecture: local, on-demand helper process, no always-on OCR engine, no background screen scanning, and immediate process exit after each capture.

Stable Core remains independent from UI Automation/OCR implementation.
