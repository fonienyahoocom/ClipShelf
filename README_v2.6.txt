ClipShelf v2.6.1 - TEXT CAPTURE PROBE INTEGRATION
===============================================

Purpose
-------
This build keeps the v2.5.4 clipboard manager behavior unchanged and adds one
user-facing entry point for the approved one-shot Windows UI Automation probe.
It is NOT yet the final semantic text-capture product feature.

How to start the probe
----------------------
1. Run ClipShelf normally.
2. Use the desktop floating ClipShelf icon.
3. Right-click the floating icon.
4. Choose: 擷取文字
5. Move the mouse to the target visible text block and left-click once.
6. The click is suppressed: the probe reads the point without clicking the web page.
7. The probe exits after one capture. Press Esc to cancel. It also times out after 20 seconds.

What the probe DOES NOT do
--------------------------
- no OCR
- no browser extension
- no CDP / WebDriver
- no direct IA2 traversal
- no Edge special flags
- no elevated privilege
- no background accessibility watcher
- no clipboard modification
- no ClipShelf History modification
- no Invoke/click action on the target control

Output
------
Reports are written to:
  %LOCALAPPDATA%\ClipShelf\TextCaptureReports

After capture, ClipShelf opens Explorer with the report selected. The user does
not need to interpret the report; send the .txt file back to ChatGPT for review.

Evidence included
-----------------
- cursor coordinates and target window/process
- AutomationElement.FromPoint properties
- bounded RAW ancestor chain
- supported patterns
- TextPattern evidence when exposed
- DocumentRange raw + escaped text
- selection / visible ranges / RangeFromPoint evidence
- ValuePattern evidence
- LegacyIAccessiblePattern evidence exposed through UIA only
- bounded Control View subtree
- bounded Raw View subtree

Resource behavior
-----------------
ClipShelf.Core remains event-driven. UI Automation is not monitored in the
background. ClipShelf.TextProbe.exe exists only while the user explicitly runs
one capture and exits immediately afterwards.
