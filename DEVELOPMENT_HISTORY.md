# 2.15.0 — independent Quick Picker and resident WPF

Implemented from verified 2.14.2 source. Windows x64 build/package pass; 64 headless checks pass; 4 IPC tests blocked by host socket restrictions; native Windows UI, install/upgrade, paste, IME, DPI and resource/performance measurements remain UNVERIFIED. See validation/VERIFICATION.md. Core/WPF separation and fail-closed storage preserved.

# 2.14.2 — Editing / Focus / Scrolling Regression Repair (2026-10-09)

Baseline: delivered Drive v2.14.1 source and the user's 135.7-second Windows recording.
Body editing is explicit in the card header and can also begin from native modification input.
The original TextBox uses a direct read-only binding; selected Delete/Backspace, typing and paste enter drafts before native input handling.
Editing state and save/cancel appear above the body; title Enter/Esc and native undo remain.
Single-click copy no longer expands or rebuilds/reorders the current list. Copy history metadata still persists.
Visible editor focus no longer calls unconditional ScrollIntoView. Menus keep their own focus until closed.
Empty body ToolTip removed; folder/filter changes resolve drafts before replacing the editing surface.
Core, data format, data directory and installer behavior unchanged apart from version identity.
Verification: 40 headless model/service checks, Core/WPF win-x64 publish and offline NSIS package checks.
Native Windows interaction and upgrade remain UNVERIFIED. The supplied video validates the v2.14.1 failure, not the v2.14.2 result.
See validation/VERIFICATION.md; earlier evidence is historical under validation/baseline-2.14.1.

---

# 2.14.1 — Card Interaction Patch (2026-10-09)

Baseline: explicitly supplied Google Drive ClipShelf-2.14.0-Source.zip and specification v0.3.
A/B buttons reuse CompactView and update the original virtualized list without rebuilding it.
Text drafts edit in the original TextBox with checked save/cancel, native editing commands and IME guards.
Header rename reuses Title with Enter/Esc and automatic-heading fallback; body and metadata are separate.
Ordinary single click copies; drag/modifier/selection and inline drafts do not invoke that copy route.
Actual content edits recompute the hash, reset the old repeated-copy count and reject active collisions.
Core/WPF split, data path, backup/import and safe-delete retained; no OCR reintroduced.
Verification: Core/WPF win-x64 self-contained publish and 36 data/edit checks; NSIS package verification.
Native Windows installation/upgrade, Chinese IME, clipboard gestures, scroll and DPI remain unverified.
No Library CURRENT or old Drive release overwritten. See validation/VERIFICATION.md for evidence scope.

---

# 2.14.0 — Desktop Release (2026-10-03)

由 2.13.0 延伸，保留 Core/WPF 分離、資料位置、最近刪除及合併匯入。
新增 fail-closed 儲存、快照／自動備份、標題備註、全庫多詞搜尋與 MD/TXT 索引、批次整理與合併複製、範本填寫、閱讀與精簡模式、定時暫停。
來源出處不要求保留；既有相容欄位仍存在，但未增加出處保留機制。
新增每使用者 Windows x64 自含安裝包；更新不需先移除；解除安裝只清理程式檔，保留資料。
建置與資料測試結果見 validation；不得將交叉編譯／Wine 驗證等同 USER Windows 實機通過。

---

# v2.13.0 — Interaction Maturity Pass

**Status:** source-complete candidate / Windows build + runtime validation pending  
**Based on:** v2.12.0 Windows Multi-Select + Safe Rapid Delete

### Product intent
- User explicitly kept search as a secondary capability, kept all four per-card actions visible, deferred sidebar restructuring, and approved a product-maturity pass for multi-select, footer/status, header hierarchy and text-entry UX.
- The change is intentionally one cohesive interaction pass rather than several architecture changes.

### Changes
- Add Text / Edit Text: Enter commits immediately; Shift+Enter inserts a newline.
- Add/Save is the primary left button; Cancel moves to the right; the dialog explains the keyboard contract.
- Multi-select now swaps the normal toolbar for a contextual `已選 N 項` action bar with Select All, scoped bulk action, permanent delete where applicable, and Done. Ctrl/Shift blank-card selection still auto-enters the mode.
- Card actions remain always visible, but trash is visually neutral until hover, where a red destructive state appears. Rapid-delete safety from v2.12.0 remains unchanged.
- Footer is reduced to user-facing shortcut / autosave / local-save status; Core/UI process diagnostics are no longer permanent visual noise.
- Header action hierarchy is unified: Add Text is primary, Pause/Backup are secondary, icon controls remain tertiary. Search is retained but visually de-emphasized.

### Invariants kept
- No persistence/schema migration or user-data reset.
- Safe Delete, Backup/Import Preview, content dedup, Core/UI split, warm standby and clipboard monitoring are unchanged.
- Sidebar structure is intentionally unchanged.
- OCR remains removed; no UIA/cloud replacement is added.

### Validation status
- Static source/XAML/reference/archive checks completed in the development environment.
- Windows 10 BUILD SUCCESS and runtime interaction feel remain `NOT YET VERIFIED` until user evidence.

# v2.12.0 — Windows Multi-Select + Safe Rapid Delete

**Status:** source-complete candidate / Windows build + runtime validation pending  
**Based on:** v2.11.0 OCR Removed + Fast Hotkey Wake

### User request / runtime evidence
- User wants Windows-standard multi-select from the card's right-side blank area while keeping the text body dedicated to native text selection.
- User-provided screen recording shows the intended blank-area target and reports that rapid trash clicks can become uncontrolled, hit copy/paste behaviour and close the main panel.

### Source-level root cause
- `HistoryList_MouseDoubleClick` routes a genuine card double-click to `PasteItemAsync`, which copies/pastes and then calls `CloseForTrueHide`.
- The v2.11.0 handler excluded the selectable text box but did not exclude action `Button` / `CheckBox` origins.
- Rapid destructive clicks also rebuild/recycle rows inside the Windows double-click interval, so a short post-delete activation guard is warranted in addition to origin filtering.

### Changes
- Ctrl/Shift click on non-text card chrome/blank space now auto-enters multiselect; no prior click on the `多選` toolbar button is required.
- Ctrl toggles one item; Shift selects the anchored contiguous range; Ctrl+Shift adds a range.
- The selectable text `TextBox` remains excluded so native text highlighting is unchanged.
- Marked cards get a persistent selection highlight in addition to the checkbox.
- Action controls are excluded from ListBox double-click activation.
- Trash actions arm a 750 ms double-click activation suppression window while still allowing rapid repeated trash clicks themselves.
- During that guard window, adjacent copy/favorite/more actions are ignored so row reflow cannot turn a rapid trash sequence into another action.

### Invariants kept
- No persistence/schema migration or user-data reset.
- Safe Delete, Backup/Import Preview, content dedup, Core/UI split, v2.11 warm standby and clipboard monitoring are unchanged.
- OCR remains removed; no UIA/cloud replacement is added.

### Validation status
- Source/reference/XAML/XML/archive checks completed in the development environment.
- Windows 10 BUILD SUCCESS, selection feel and rapid-delete runtime behaviour remain `NOT YET VERIFIED` until user evidence.

# v2.11.0 — OCR Removed + Fast Hotkey Wake

**Status:** source-complete candidate / Windows build + runtime validation pending  
**Based on:** v2.10.1 Import Preview + Content Dedup + Local OCR Compile Fix 1

### User request
- OCR is no longer needed and should be removed.
- Ctrl+~ wake-up currently feels slow and should remain fast.

### Evidence / cause
- In v2.10.1, the Core hotkey route closes `ClipShelf.UI.exe` on shortcut hide.
- Therefore the next Ctrl+~ must cold-start WPF and reload state.

### Changes
- Removed active Local Region OCR UI, Ctrl+Alt+O registration, Core/UI OCR bridge, OCR helper files and OCR packaging.
- Added a dedicated `QuickToggleUI` Core→UI event used only by Ctrl+~.
- Ctrl+~ now hides the existing UI into a bounded 5-minute warm standby; another Ctrl+~ wakes the same process directly.
- If not reopened within 5 minutes, the WPF UI exits and the lightweight Core remains.
- Existing true-hide / exit semantics remain separate from the shortcut warm-toggle route.

### Invariants kept
- No persistence/schema migration or user-data reset.
- Safe Delete, Backup/Import Preview, content dedup, Core/UI split and clipboard monitoring are unchanged.
- No UIA restoration and no cloud OCR/API replacement.

### Validation status
- Source/reference/XML/package checks completed in the development environment.
- Windows 10 BUILD SUCCESS and hotkey latency remain `NOT YET VERIFIED` until user runtime evidence.

# v2.10.0 — Full Backup Export / Safe Merge Import

**Status:** source-complete candidate / Windows build + round-trip test pending  
**Based on:** v2.9.0 Local Region OCR

### Goal
Add a real user-facing backup path that can carry ClipShelf history records together with copied text, images, files, folders and recursive subfolder contents, while keeping `%LOCALAPPDATA%\ClipShelf` as the live data store rather than treating a backup as a raw folder replacement.

### Changes
- Added header `⇅ 備份` menu with `匯出完整備份…` and `匯入備份（合併，不覆蓋現有資料）…`.
- Added `BackupService` and `.clipshelfbackup` archive format with manifest + logical state + payload entries.
- Export preserves ClipItem metadata and folder/sidebar structure; image payloads are embedded; Files payloads embed regular files and recursively embed directory contents.
- Export reads live data but does not mutate `history.json`, `%LOCALAPPDATA%\ClipShelf`, or the configured storage.
- If the output archive is saved inside a folder being backed up, the in-progress archive itself is excluded to prevent self-recursion.
- Import validates the archive first, restores physical payloads into a new `storage\imports\...` directory, then merges new IDs into current data. Existing IDs are skipped rather than overwritten.
- Import does not clear History, does not replace `history.json` with backup `state.json`, and does not silently overwrite existing items.
- Added checked persistence for import commit: success is not reported unless the merged pending state can be atomically written to `history.json`. Before commit failure, in-memory state is rolled back and the newly-created import payload directory is discarded. Existing best-effort background saves retain their previous behavior.
- Imported pending-purge items are converted to ordinary Recently Deleted so an old grace-period deadline cannot immediately destroy restored data.
- Managed Files purge now handles imported managed directories recursively as well as files.
- v2.9 local OCR route and Core/UI architecture are otherwise retained.

### Data-safety boundary
- Program-version folder remains separate from user data.
- Export is read-only with respect to live ClipShelf data.
- Import is additive/merge-oriented and only writes through the normal ClipShelf persistence path after payload staging.
- No raw `%LOCALAPPDATA%\ClipShelf` folder overwrite is used.

### Validation status
Static source/package checks only in the development environment. Windows .NET 8/WPF build and real export → import round-trip remain the authoritative validation gate.

# v2.9.0 — Integrated local Region OCR

User authorized direct product integration. Main button, language selector, bubble/tray menu, Ctrl+Alt+O route to isolated PowerShell helper. Region snapshot, OCR, editable WPF preview, explicit AddOrTouchItem save. No UIA reinstatement or alternate-engine research. Uses the PS/WinRT host actually validated twice on user machine; final region UI and integration are not yet Windows-tested. No persistence schema changes.

# v2.8.2 — Pointer Capture Removed

User explicitly requested removal of current pointer text capture; OCR deferred. This supersedes earlier plans to retain UIA in the active build.
Removed UI/settings/bubble entry points, capture-only Core/UI events, preview classes, ExperimentalCapture and TextProbe projects, and optional capture publish steps. Core changes are limited to removing that feature routing; persistence, clipboard monitoring, Safe Delete and UI lifetime logic are not redesigned. Dormant AppSettings flag retained byte-identically for compatibility. Build harness diagnostics and CMD encoding preserved. Static verification only; Windows build/runtime pending.

## v2.8.0 Experimental - Capture Route Fix 1

### INCIDENT
- The Experimental helper built successfully and reported `AVAILABLE`.
- Invoking the floating bubble command `擷取文字` still attempted to start the retired development probe at `ClipShelf_READY\tools\ClipShelf.TextProbe.exe`.
- Build Harness 3 correctly publishes the real helper to `ClipShelf_READY\experimental\ClipShelf.ExperimentalCapture.exe`, so the user received a misleading missing-component message after a successful build.

### Cause and fix
- A stale user-facing route from the TextProbe research phase survived the product integration.
- The bubble command is now `β 指向擷取（Experimental）` and sends a named one-shot request to the WPF UI.
- The WPF UI reuses the same Experimental workflow as the header β button: helper launch, result preview, and History insertion.
- Core does not execute UI Automation and no longer contains a runtime dependency on `tools\ClipShelf.TextProbe.exe`. TextProbe source remains only as historical evidence.

### Scope
- No UIA target-resolution algorithm change.
- No OCR, IA2/MSAA, cloud API, geometry, or compatibility research added.
- Build Harness 3 and Experimental Compile Fix 1 are preserved.

# ClipShelf Development History

This file is intentionally cumulative. Every new ClipShelf package should carry the complete history forward so a future ChatGPT / coding assistant can understand what changed, what failed, and which design decisions are still active even after older version folders are deleted.

## Maintenance rule for future AI

When creating a new version:

1. Do **not** erase older entries.
2. Add a new entry at the top of the version log with:
   - version and date
   - status: stable / candidate / probe / failed-build / replaced
   - based-on version
   - goal
   - actual source changes
   - user-observed result if known
   - failed attempts / detours if relevant
   - invariants deliberately kept unchanged
3. Never rewrite a failed attempt as if it succeeded. Keep negative evidence.
4. If a later version replaces an earlier approach, mark the earlier entry as replaced rather than deleting it.
5. Copy this file into `ClipShelf_READY` on every successful build.
6. Build-failure handoff filename is a permanent UX invariant: every future version must use `BUILD失敗_把這個傳給ChatGPT.txt` (package folder first, then Desktop/TEMP fallback). Do not rename it in later versions unless the user explicitly asks.
7. When the user declares the app finished, this history may be compacted into final release notes or deleted at the user's request. It is development memory, not required user data.

## Current state

- Current package: **v2.13.0 Interaction Maturity Pass**.
- Current application base: v2.11 fast hotkey warm standby + v2.10.1 backup import preview/content dedup + v2.7 safe-delete lifecycle + v2.5 safe recent-history model + v2.x lightweight Core/UI split.
- Local Region OCR and Ctrl+Alt+O remain intentionally removed from the active product line.
- Ctrl+~ retains bounded 5-minute UI warm standby for faster repeat wake; full WPF UI still exits after the standby window.
- Card blank/chrome supports Windows-style Ctrl/Shift multiselect while text remains natively selectable; selection actions now use a contextual toolbar.
- Rapid trash clicks are isolated from the card double-click paste/hide route.
- No cloud API, API key, token billing, UIA reinstatement, background screen scan, schema migration, or raw user-data-folder replacement is introduced by v2.13.0.

---

## Version log

### v2.8.0 Experimental — Experimental Capture Compile Fix 1
**Status:** compile hotfix / awaiting Windows Experimental publish validation  
**Based on:** v2.8.0 Experimental Pointer Text Capture — Build Harness 3

**User-observed build evidence**
- Build Harness 3 itself now runs correctly and produces the required Core/UI artifacts.
- Main ClipShelf build reached `BUILD SUCCESS`.
- Optional Experimental Capture publish failed with `CS0123` at `ClipShelf.ExperimentalCapture\Program.cs(66,9)`.
- Because the experimental helper is isolated, this failure did not block the required Core/UI build.

**Cause**
- `CaptureHintForm.CancelRequested` is an `Action` event (zero parameters).
- The subscription used `CancelAndExit` directly, but that method has one optional `string status` parameter. Optional parameters do not change the method signature for delegate conversion, so the method group cannot be converted to `Action`.

**Change**
- Changed only the event subscription from `CancelRequested += CancelAndExit` to `CancelRequested += () => CancelAndExit()`.
- No UIA capture logic, Stable Core logic, data model, settings, Safe Delete behavior, or fallback behavior was changed.

**Engineering classification**
- Changelog-only compile defect under the current Experience Extraction protocol. It is a local delegate-signature mismatch and does not, by itself, justify a cross-project engineering Case.

### v2.8.0 Experimental — Build Harness 3 launcher hotfix
**Status:** build-launcher hotfix / Windows launcher validation PASSED; Experimental helper compile failed separately  
**Based on:** v2.8.0 Experimental Pointer Text Capture

**Incident evidence**
- User ran `BUILD_EXE.cmd` on Windows 10 and CMD interpreted the first line as `嚜濃echo off`, then parsed many later lines as truncated/corrupted commands.
- The failing package launcher was UTF-8 BOM + LF-only.
- The earlier user-verified v2.6.6 Build Harness 2 launcher had CRLF line endings.

**Change**
- Replaced only the CMD bootstrap with an ASCII-only, BOM-free, CRLF launcher.
- Kept Unicode messages and the normal `BUILD失敗_把這個傳給ChatGPT.txt` handoff inside the BOM-marked PowerShell harness.
- Product source and Experimental Pointer Text Capture source are unchanged.

**Engineering classification**
- This qualifies as a build-infrastructure incident because the harness itself failed before it could perform its diagnostic role.
- Preserve the evidence; do not generalize one incident into a universal rule for every script type.
- ClipShelf packaging invariant from this incident: keep the tiny `.cmd` bootstrap ASCII/BOM-free/CRLF and put Unicode logic in PowerShell.

### v2.8.0 Experimental — Isolated Pointer Text Capture
**Status:** experimental integration candidate / real-use evidence stage  
**Based on:** v2.7.0 Safe Delete

**Evidence basis**
- Two real Windows 10 + Edge + ChatGPT TextProbe 0.2A runs established target resolution for the tested provider: different cursor positions produced different Character results while Line / Paragraph matched the visible target text.
- The experiment no longer treats TextPatternRange geometry as a prerequisite for the current product goal. Geometry remains unexplained but is not a blocker because precise overlay/highlight is not currently required.

**Goal**
- Stop expanding the probe/research project and put the already-supported capability into a usable ClipShelf Experimental version.
- Let ordinary use generate future application-coverage evidence instead of pre-building a large compatibility research matrix.

**Architecture**
- `ClipShelf.Core` is deliberately unchanged from the v2.7.0 base.
- UI Automation and low-level capture hooks are isolated in a separate optional process: `ClipShelf.ExperimentalCapture.exe`.
- The main UI launches the helper for one capture only, receives a temporary JSON result, then disposes the helper.
- Helper restore/publish is non-blocking in Build Harness 2. Required Core/UI delivery remains authoritative.

**User-facing behavior**
- Settings adds `實驗性：滑鼠位置文字擷取`, default OFF.
- When enabled the header exposes `β 指向擷取`.
- ClipShelf temporarily hides, the helper waits for one target click, suppresses that click, and uses local Windows UI Automation only.
- The result preview offers Paragraph / Line / Word / Character choices; the user can edit before `加入歷史`.
- Unsupported / unavailable target positions fail only that capture. No automatic fallback is activated.

**Explicitly not added**
- IA2 / MSAA fallback
- OCR
- remote/cloud API, API key, account, token/usage billing
- Control Tree nearest-node research
- VisibleRanges geometry
- per-application special patches
- background accessibility watcher

**Product boundary**
- Local Windows/.NET APIs are allowed.
- Remote services requiring keys/accounts/cloud transport/usage fees must be separately proposed and approved; they may never be silently added.

**Validation status**
- Source/package structure checked in the development environment.
- Windows Build Harness + runtime use remains the real validation gate.

---

### v2.7.0 — Safe Delete / Recently Deleted lifecycle
**Status:** candidate — source-complete, awaiting Windows build/runtime validation  
**Based on:** v2.6.6 Build Harness 2 (user-verified BUILD SUCCESS baseline)

**Goal**
- Productize deletion so ordinary delete is recoverable without weakening the meaning of true permanent deletion.
- Keep the existing low-resource Core/UI architecture and the v2.5 recent-history ownership semantics unchanged.

**Changes**
- Added a fixed `最近刪除` sidebar surface.
- Added persisted soft-delete state (`IsDeleted`, `DeletedAt`). Deleted records keep their existing folder / favorite / temporary / History placement metadata so restore does not require duplicating the source object.
- A standalone History deletion now goes to `最近刪除`; organized History entries are still only removed from History and keep their source data.
- Removing from 收藏 / 暫存 / a folder remains a placement operation, not true deletion.
- Normal views expose `移到最近刪除` instead of irreversible permanent deletion.
- `最近刪除` supports per-item restore, permanent delete, multi-select restore/permanent delete, empty-trash, and drag-to-trash from active sections.
- Permanent delete is deferred for 8 seconds after confirmation. Pending purge is persisted so closing the UI cannot accidentally turn the grace period into data loss or leave an ambiguous state.
- Managed image/file payload is retained during soft-delete and physically removed only by final purge.
- Added trash retention setting: 7 / 30 / 90 days / never auto-clear; default 30. Cleanup runs on UI startup / settings change, not as a permanent background polling loop.

**Validation status**
- Source structure and XAML well-formedness checked in the development environment.
- Windows .NET 8/WPF compilation is unavailable in this environment, so this entry must not be upgraded to runtime-verified until the user reports Windows BUILD SUCCESS and basic delete/restore regression tests.

**Experience extraction status**
- This is primarily a product/data-lifecycle feature, not evidence of a cross-project engineering incident by itself. No universal Case/Rule is promoted from this change yet.

---

### v2.6.5 — Legacy bridge compile fix + permanent Chinese build-error handoff name
**Status:** current candidate / Probe 1 build repair  
**Based on:** v2.6.4

**Goal**
- Fix the user's real v2.6.4 TextProbe compile failure caused by the unavailable managed `LegacyIAccessiblePattern` type.
- Permanently standardize the user-facing build failure handoff as `BUILD失敗_把這個傳給ChatGPT.txt` for this and all future versions.

**Actual user evidence leading to this version**
- v2.6.4 successfully restored and published Core and the WPF UI.
- TextProbe then failed specifically on compile-time references to `LegacyIAccessiblePattern` (`CS0103`, `CS0246`, and follow-on member errors).
- This proves the user's .NET 8 WindowsDesktop managed reference set does not expose the wrapper type the probe source had assumed.

**Changes**
- Removed every compile-time dependency on the managed `LegacyIAccessiblePattern` wrapper from Probe 1.
- Probe still records the full UIA `SupportedPatterns` list. It additionally detects pattern names containing `LegacyIAccessible` as diagnostic bridge evidence without trying to instantiate an unavailable managed wrapper.
- Detailed raw MSAA/IA2 access remains deliberately deferred to Probe 2, exactly as the earlier technical review required. This is a scope correction, not evidence loss disguised as success.
- Core/UI version advanced to 2.6.5; TextProbe advanced to 0.1.5.
- `BUILD_EXE.cmd` now uses `BUILD失敗_把這個傳給ChatGPT.txt` for package/Desktop/TEMP failure handoff and switches CMD to UTF-8 code page for the Chinese filename.
- Cumulative maintenance rules now explicitly require all future versions to keep this same build-failure filename.

**Kept unchanged**
- Normal ClipShelf behavior remains based on v2.5.4.
- Probe remains pure UIA, read-only, one-shot, normal Edge, no special browser flags, no OCR, no extension, no CDP/WebDriver, no direct IA2 client, no elevation, and no background accessibility watcher.
- Formal semantic text-capture product behavior is still not approved until real Probe A/B/C evidence is reviewed.

---

### v2.6.4 — Resilient failure handoff + narrower UIA source dependency
**Status:** failed-build / replaced by v2.6.5  
**Based on:** v2.6.3

**Goal**
- Fix the v2.6.3 failure-reporting bug where Explorer was launched for `SEND_TO_CHATGPT_BUILD_ERROR.txt` but Windows reported that the file did not exist.
- Make build failures hand themselves back to ChatGPT without requiring Explorer search or manual log hunting.
- Further reduce compile-fragile explicit UIA text-range type usage in Probe 1.

**Changes**
- Build output is first captured to a TEMP log, so diagnostics survive even if writing beside the package fails.
- On failure the script attempts to create `SEND_TO_CHATGPT_BUILD_ERROR.txt` in three locations, in order: package folder, Desktop, then TEMP. Each location is verified before use.
- The failure report is opened directly in Notepad and its full contents are copied to the Windows clipboard automatically. Explorer `/select` is no longer used.
- `build_log.txt` is still mirrored to the package folder when possible, but is no longer the only source of truth.
- Probe removed the explicit `System.Windows.Automation.Text` import and the `TextPatternRangeEndpoint` comparison. `RangeFromPoint` remains diagnostic evidence and its returned range text is read through the existing bounded reflection helper.
- Core/UI version advanced to 2.6.4; probe advanced to 0.1.4.

**Actual user evidence leading to this version**
- v2.6.3 reached its failure path, but Explorer displayed: Windows could not find `...\SEND_TO_CHATGPT_BUILD_ERROR.txt`.
- This proves the v2.6.3 error-handoff guarantee was false on the user's actual Windows machine. The build's underlying compiler error was therefore not reliably handed back.

**Kept unchanged**
- Normal ClipShelf behavior remains based on v2.5.4.
- Probe remains UIA-only, read-only, one-shot and launched from the floating ClipShelf icon right-click menu.
- No OCR, extension, CDP/WebDriver, raw IA2 client, Edge flags, elevation, clipboard mutation by the probe, or background accessibility watcher.

---

### v2.6.3 — Build-safe probe + automatic error handoff
**Status:** failed-build-path / replaced by v2.6.4  
**Based on:** v2.6.2

**Goal**
- Continue Probe 1 without changing normal ClipShelf behavior.
- Remove another compile-fragile explicit `TextPatternRange` type reference from the probe helper.
- Make every future Windows build failure produce one obvious file the user can hand to ChatGPT without hunting through folders.

**Changes**
- Core/UI version advanced to 2.6.3; probe advanced to 0.1.3.
- `GetBoundedText` now accepts `object` and invokes `GetText(Int32)` reflectively, avoiding an explicit `TextPatternRange` helper signature while preserving read-only evidence collection.
- Rebuilt `BUILD_EXE.cmd` failure handling. On failure it now removes partial `ClipShelf_READY`, creates `SEND_TO_CHATGPT_BUILD_ERROR.txt`, embeds the full build log, opens the file in Notepad, and selects it in Explorer.
- Fixed the build script's previous .NET-detection/failure-label logic so missing SDK and missing history are reported through distinct, reliable paths.
- Successful builds still verify Core, UI, TextProbe and cumulative history before declaring success.

**Actual user evidence leading to this version**
- v2.6.2 failed to build on the user's Windows machine.
- The user's generated folder listing contained source files and `DEVELOPMENT_HISTORY.md`, but no `build_log.txt` and no completed `ClipShelf_READY`, so the exact v2.6.2 compiler failure was not preserved in the available artifact.
- This version therefore treats discoverable failure reporting as part of the product-development UX rather than asking the user to manually locate logs.

**Validation state**
- Source/package checks completed.
- Windows .NET 8/WPF/UIAutomation build still requires the user's machine for final validation; no model-side environment can honestly guarantee a Windows build will never fail before that test.

**Kept unchanged**
- v2.5.4 normal ClipShelf behavior.
- Probe remains UIA-only, read-only, one-shot and opened from the floating ClipShelf icon right-click menu.
- No OCR, extension, CDP/WebDriver, raw IA2 client, Edge flags, elevation, clipboard mutation or background accessibility watcher.
- Probe report retention remains newest 20 / maximum 30 days.

---

### v2.6.2 — Probe build fix + cumulative development memory
**Status:** failed-build / replaced by v2.6.3  
**Based on:** v2.6.1

**Goal**
- Fix the v2.6.1 compile error where `TextPatternRange` could not be resolved.
- Introduce a cumulative version-history file that survives deletion of older version folders as long as the newest package is kept.

**Changes**
- Added `using System.Windows.Automation.Text;` to the TextProbe source. `TextPatternRange` and related text-range types live in this namespace.
- Core/UI version advanced to 2.6.2; probe advanced to 0.1.2.
- Added this `DEVELOPMENT_HISTORY.md` file.
- Build script now copies the cumulative history into `ClipShelf_READY` on a successful build.
- Build script verifies all three executables before declaring success.
- Failed builds clean up the partial `ClipShelf_READY` output so an incomplete build is never mistaken for a usable installation.

**Known validation state**
- Source-level fix applied.
- Must still be compiled on the user's Windows/.NET 8 environment; this environment cannot run Windows WPF/UIA builds.

**Kept unchanged**
- v2.5.4 app behavior.
- Probe remains read-only, one-shot, on-demand from the desktop floating ClipShelf icon right-click menu.
- Report retention policy: newest 20 reports, and reports older than 30 days are removed on capture.

---

### v2.6.1 — UIAutomation reference repair attempt
**Status:** failed-build / replaced by v2.6.2  
**Based on:** v2.6

**Goal**
- Repair v2.6 build failures where `UIAutomationClient`, `UIAutomationTypes`, `WindowsBase`, and `System.Windows.Automation` were not resolved.

**Changes**
- TextProbe project was changed to use the .NET 8 Windows Desktop/WPF framework reference rather than manual bare assembly references.
- Diagnostic report cleanup added: keep at most 20 reports; delete reports older than 30 days when a capture completes.

**Actual user result**
- Core and full UI published successfully.
- TextProbe still failed to compile with:
  `CS0246: TextPatternRange could not be found`.
- Root cause: missing `System.Windows.Automation.Text` namespace import.

**Negative evidence preserved**
- Merely enabling WPF/WindowsDesktop references was not sufficient; the source also needed the correct Text namespace.

---

### v2.6 — First integrated semantic-text Probe 1
**Status:** failed-build / replaced by v2.6.1  
**Based on:** v2.5.4

**Goal**
- Add the approved one-shot Windows Accessibility diagnostic without yet implementing a formal semantic-text-capture feature.

**User-facing design**
- Entry point: desktop floating ClipShelf icon → right-click → `擷取文字`.
- No keyboard shortcut yet.
- One capture only; Esc cancels; timeout exits automatically.
- Probe click is suppressed so the webpage/control is not invoked.

**Probe scope**
- Windows UI Automation only.
- FromPoint evidence, ancestor chain, bounded Control View and Raw View subtrees, supported patterns, TextPattern raw evidence, Value/Name, LegacyIAccessiblePattern evidence exposed through UIA.
- No OCR, extension, CDP, WebDriver, direct IA2 traversal, elevation, Edge flags, or background watcher.

**Actual user result**
- Build failed because the probe project could not resolve UIAutomationClient/UIAutomationTypes/WindowsBase and therefore System.Windows.Automation types.

**Detour learned**
- UIA support must be referenced through the proper .NET Windows Desktop/WPF framework rather than assumed available from a plain WinForms net8.0-windows project.

---

### v2.5.4 — Native full-text selection
**Status:** retained base / user continued from this version  
**Based on:** v2.5.3

**Goal**
- Remove the unstable custom text-selection auto-scroll approach.

**Changes**
- First click expands a compact text card but does not simultaneously start selection.
- Expanded text gets its own bounded TextBox viewport with native WPF selection/autoscroll.
- Removed custom selection timer and programmatic `Select()` during drag.
- Mouse wheel inside expanded text scrolls that text viewport slowly.
- Ctrl+C / selected-text context menu still feeds selected text into History.

**Reason for redesign**
- v2.5.2 and v2.5.3 were inherently unstable because WPF native selection and a second programmatic selection loop were fighting over the same range.

---

### v2.5.3 — Attempted stabilized custom selection autoscroll
**Status:** unsuccessful experiment / replaced by v2.5.4  
**Based on:** v2.5.2

**Goal**
- Stabilize long mouse-only selection while the outer list auto-scrolled.

**Changes attempted**
- Fixed selection anchor.
- Re-applied highlighted range before/after scroll ticks.
- Monotonic endpoint logic.
- Smaller edge zone, 120 ms dwell, slower scroll steps.
- Inactive selection highlight kept visible.

**Actual user result**
- Still unstable. Existing highlighted text could still behave unpredictably while scrolling.

**Conclusion**
- Do not revive this architecture unless there is a completely different technical reason. It was replaced, not merely retuned.

---

### v2.5.2 — Mouse-only selection autoscroll experiment
**Status:** unsuccessful experiment / replaced  
**Based on:** v2.5.1

**Goal**
- Allow the user to drag-select long text using only the mouse while the page auto-scrolls.

**Changes**
- Edge-driven outer-list auto-scroll while dragging a selection.
- Click blank area on the right to collapse expanded cards.
- Slower wheel scrolling while cards are expanded.

**Actual user result**
- Auto-scroll worked, but selection was unstable: previously selected text could disappear or change unexpectedly while the list moved.

---

### v2.5.1 — Collapsible sidebar + full-text expansion
**Status:** retained behavior  
**Based on:** v2.5

**Changes**
- Parent folders can collapse/expand child folders using left click.
- Collapse state is stored in settings.
- Text cards can expand from compact preview to full text.
- Expanded full text remains selectable; selected text can be copied into History.
- Collapse button provided.

---

### v2.5 — Mature UI + safe recent-history model
**Status:** retained core product behavior  
**Based on:** v2.4

**UI changes**
- Cleaner grouped top actions.
- Main filters: all / text / image / file.
- More mature card hierarchy, spacing, type labels, and status bar.
- Existing parent/child folder data model kept; only visual cleanup.

**History model change**
- History became recent-copy/recent-capture visibility rather than the sole owner of data.
- Copying an item from a folder/favorite/etc. makes it visible in History again without duplicating the underlying item.
- Removing a History entry does not delete the original item if that item still belongs to a folder/favorite/temporary location.
- Pure History-only items can still be permanently deleted.
- Selected text copied from inside a card becomes a new recent History item while the original full item remains unchanged.

**Invariant**
- Core/UI lightweight architecture, warm standby, and local data path remain unchanged.

---

### v2.4 — Sidebar root drag grips + persistent window mode
**Status:** predecessor to v2.5

**Changes**
- Fixed root nodes (favorites / temporary / AI phrases / quick phrases) gained drag grips and root ordering.
- Existing custom folders retained drag grips and old folder keys could be recovered from existing data.
- General mode vs non-interference mode became persistent state.
- Both desktop bubble right-click and tray right-click can switch modes bidirectionally.
- Taskbar Jump List can enter/restore modes.

**Historical context**
- This version followed a v2.3 attempt where the user reported two remaining issues: old folders still lacked drag grips, and non-interference mode had no intuitive way back to normal mode.

---

### v2.3 — Pre-v2.4 integration attempt
**Status:** known-problem build / replaced by v2.4

**Intended changes**
- True-move History semantics and cleanup safety.
- Remove `複製並隱藏`; add open-saved-location behavior.
- Restore convenient normal taskbar/Alt+Tab behavior while keeping an explicit non-interference mode.

**User-reported problems**
- Existing old folders still did not receive the expected `⠿` drag grip.
- Once in non-interference mode, returning to normal mode was not intuitive.

**Resolution**
- v2.4 specifically addressed these two issues.

---

### v2.2 — Dedicated drag grips + selectable text
**Status:** architectural behavior retained

**Changes**
- Folder drag begins from a dedicated drag grip instead of the whole row.
- Clip records gained their own drag grip.
- Visible card text became normal selectable text.
- Ctrl+C copied only selected text and, at that historical stage, intentionally did not create a duplicate History item.
- Context menu supported copy selected / copy whole / edit.

**Later change**
- v2.5 intentionally changed the History behavior so selected-text copying does create a recent History item.

---

### v2.1 — Warm UI + folder drag reliability
**Status:** architecture retained

**Changes**
- Lightweight Core remains always on.
- Full WPF UI stays warm for about 45 seconds after minimize/click-outside, allowing fast reopen.
- After the warm window expires, the UI process exits and only Core remains.
- True hide exits the UI immediately.
- Folder drag reliability improved while preserving deliberate drag/nesting behavior.

**Design reason**
- Core/UI split reduced idle resource use but introduced noticeable cold-start delay; warm standby was the compromise.

---

### Transitional v2.0.3 package note
**Status:** historical transition

The packaged file named `README_v2.0.3.txt` labels itself `ClipShelf v2.1`. It documents a transition where:
- desktop bubble survival after UI close was repaired,
- full manager became Alt+Tab reachable while open,
- switching away via Alt+Tab kept the full manager alive,
- click-outside/minimize still collapsed to bubble,
- hidden/collapsed states removed the UI from Alt+Tab.

This is preserved as historical evidence rather than silently renumbered.

---

### v2.0.2 — Lifecycle / clean-exit fix
**Status:** architectural fix retained

**Problem**
- WPF UI could appear closed while `ClipShelf.UI.exe` stayed alive and locked its DLL because ShutdownMode used explicit shutdown.

**Fix**
- UI lifecycle was corrected so hide/minimize can dispose the UI while Core remains.
- Exit ClipShelf means both Core and UI should terminate.
- Forced termination remains last resort only for an unresponsive UI.

---

### v2.0.1 — Wake-up reliability fix
**Status:** historical fix retained

**Changes**
- Prevented the full UI from immediately closing during tray/Explorer focus handoff.
- Core granted foreground permission and re-signaled ShowUI as the WPF window became ready.
- UI retried foreground activation once without permanent polling.

---

### v2.0 — Core/UI architecture split
**Status:** foundational architecture still active

**Major design**
- `ClipShelf.exe`: lightweight background Core for clipboard event listener, global hotkey, tray, bubble, and background history handling.
- `ui\ClipShelf.UI.exe`: full WPF manager for history/search/folders/images/files/multiselect/etc.

**Data safety**
- User data moved/remained outside the program-version folder under `%LOCALAPPDATA%\ClipShelf`.
- Deleting an extracted program-version folder must not delete History/favorites/folders/settings.

**Design objective**
- Keep idle background resource use close to a small event-driven utility while loading the full manager only when needed.

---

## Finalization note

This file intentionally records failed experiments and detours because they prevent future AI sessions from repeating already-rejected approaches. Once the user considers ClipShelf finished, an AI may:

- preserve only final architecture + important rationale in a compact final handoff,
- remove obsolete per-version detail,
- or delete this development-history file entirely if the user no longer needs development provenance.

Deleting this file does **not** affect ClipShelf user data or runtime behavior.

## v2.6.6 — 啟動閃退自動交接 / Startup crash handoff

### 實機證據
- 使用者在 v2.6.5 啟動後觀察到程式立即消失。
- 執行 `Get-Process ClipShelf,ClipShelf.UI` 後完全沒有結果，因此這不是「Core 還活著但 UI 沒叫出來」，而是 Core 沒有維持運行。
- 當下尚無 runtime crash report，因此無法只靠推理確定例外來源。

### 本版改動
- Core 加入 top-level startup exception capture 與 WinForms/AppDomain/Task exception handoff。
- 啟動階段會記錄 checkpoint，例如 mutex、WinForms 初始化、CoreContext 建構、message loop；若失敗，報告會指出最後階段。
- runtime crash 固定輸出 `程式閃退_把這個傳給ChatGPT.txt`，優先放在 `ClipShelf_READY`，失敗時依序 fallback 到桌面與 TEMP。
- crash report 會自動嘗試用記事本開啟，並將內容送到 Windows clipboard；不再要求使用者人工搜尋 log。
- `BUILD失敗_把這個傳給ChatGPT.txt` 維持為所有未來版本固定 Build failure 檔名，不得自行改名，除非使用者明確要求。
- Build script 在退出前切換到 `%TEMP%`，避免命令提示字元把版本根資料夾持有為 current directory，降低空資料夾刪不掉的情況。

### 判讀規則
- v2.6.6 先用實機 crash report 找真正 root cause；不得因為 v2.6.5 的 Core source diff 很小，就臆測是哪一行造成閃退。
- 若 v2.6.6 正常啟動，表示新增診斷本身沒有造成 regression；之後再繼續 TextProbe 實驗。


---

## v2.8.1 Experimental — Pointer Context Capture

### 實機基準
- v2.8.0 Experimental helper 已在使用者 Windows 10 實機完成 build，狀態為 `Experimental Capture status: AVAILABLE`.
- 使用者實際回報：UI Automation 已能擷取許多原本不能複製的文字。新的主要問題不是「抓不到」，而是「一次取得的語義範圍太小」。

### 本版主線改動
- 不改用 OCR；延續已實證的 `RangeFromPoint -> ExpandToEnclosingUnit` UIA 主線。
- 保留單一段落，新增以游標所在 Paragraph 為中心的：前後各 1 段、前後各 2 段、前後各 5 段的大區塊。
- 大區塊有約 8000 字上限，避免 provider 把過大的 Document 內容一次灌回 UI。
- 較大範圍使用 TextPatternRange `MoveEndpointByUnit` 做 best-effort 擴展；若 provider 不支援，只省略該較大選項，不影響段落/行/字等較小範圍。
- 預覽在可用時預設選擇「前後各 1 段」並顯示字數。
- Stable Core 的 UI Automation 隔離邊界不變。

### 下一階段已凍結但尚未實作的 OCR 策略
- 詳見根目錄 `NEXT_OCR_STRATEGY.md`，Build 後也複製到 `ClipShelf_READY`。
- OCR 必須採 explicit/on-demand 的獨立 helper；平時不常駐 OCR process、engine/model，不掃螢幕、不輪詢。
- 使用者按「OCR 區域擷取」後才啟動、框選、做本機 OCR、回傳文字，完成後 helper 立即退出。
- 第一候選為目標 Windows 上可用的本機 Windows OCR 能力；若不可行，先停下提出 evidence，再評估本機 Tesseract 類方案。不得自動加入 cloud OCR。
- 第一版 OCR 不做 UIA -> OCR 自動 fallback；兩個入口明確分開，使資源、隱私、失敗行為可預測。

### Scope boundary
本版沒有加入 OCR、IA2/MSAA、雲端 API、背景畫面掃描、geometry research 或個別程式 patch。

---

## v2.10.1 — Backup import preview + content dedup

### Incident / product risk
v2.10.0 的匯入雖然不覆蓋既有資料，但主要以 Guid ID 跳過重複。使用者指出：相同內容若因不同來源／不同備份而具有不同 ID，合併匯入仍可能堆出大量重複內容。

### Change
- 匯入改成「預掃描 → 去重 → 預覽 → 使用者確認 → staging extraction → atomic commit」。
- 預掃描完全 read-only，不改 history.json、不解壓 payload 到 storage。
- ID 相同直接列為既有項目。
- ID 不同時，新增 portable content fingerprint：
  - Text：完整文字 SHA-256。
  - Image：實際 image bytes SHA-256。
  - Files：實際 file bytes；資料夾則納入相對結構、檔名與檔案 hash。
- 預設只匯入「確定新品」；內容相同自動跳過。
- 無法可靠建立 fingerprint 的項目列為 uncertain，預設不匯入；只有使用者明確勾選才可加入。
- 新增 ImportPreviewDialog 顯示新品、ID 重複、內容重複、uncertain、新資料夾數量。
- 實際匯入只解壓 preview 已批准的 item IDs，避免重複內容先污染 storage。

### Safety boundary
- 不改用 backup state 覆寫現有 state。
- 不清空現有 History。
- v2.10.0 的 staging + SaveNowChecked commit/rollback 邊界保留。
- Stable Core runtime source 不因本功能改寫；只更新 package/version metadata。

### Experience Extraction
這次先記為產品資料合併 Case 候選，不直接升格成跨專案普遍定律。待 Windows 實機 round-trip 驗證後，再判斷「ID equality 不等於內容 identity」是否要抽成正式可遷移 Rule。

## v2.10.1 Compile Fix 1 — 2026-09-14
- 實機 Build evidence：UI publish 在 `BackupService.cs` 422/424/459/465 因 `NormalizeLeafName` 未定義而出現 CS0103。
- 修正：補上 `NormalizeLeafName(string?)` helper；採 Unicode Form C + invariant upper-case，讓 Windows 檔名比較 fingerprint 維持 case-insensitive 且 deterministic。
- 本次只修 compile defect；匯入預掃描、去重、OCR、Stable Core 行為均不改。
- 依 Experience Extraction Protocol：這是單一遺漏 helper 的局部 compile defect，記 changelog，不升格為 Case。

## 2.15.1｜2026-10-11
基準為正式 Drive 2.15.0 Source，實際 SHA-256 505ec65ff91e4ab7da42b16ca36d0d8ba91249227adaf3364d129c16ceb6286a。
修補 quick-mode 浮動圖示、原生 Shift/Ctrl 多選、Safe Delete、folder scope、點外收合／保持開啟。新增無 Work 依賴可攜式完整建置路線。
Windows 原生互動與資源／喚醒數字未測，不能由 cross-build 推論通過。

## 2.15.2｜2026-10-11
最後交叉檢查修補 Quick Picker Safe Delete 與隱藏 manager projection 同步；前2.15.1完整候選已保存，未提升native PASS。當前全部重新build/package/hash驗證。
