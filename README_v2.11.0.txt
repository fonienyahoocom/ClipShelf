ClipShelf v2.11.0 — OCR Removed + Fast Hotkey Wake

本版直接基於 v2.10.1 Import Preview + Content Dedup + Local OCR Compile Fix 1。

【這版只做兩件事】
1. 移除目前不再需要的 Local Region OCR。
2. 改善 Ctrl+~ 快捷鍵反覆喚醒速度。

【OCR 已移除】
- 移除主視窗「框選 OCR」與語言選擇。
- 移除 Ctrl+Alt+O 全域快捷鍵。
- 移除 Core tray / bubble 的 OCR 入口。
- 移除 UI/Core OCR event bridge。
- 移除 ocr helper 與 NEXT_OCR_STRATEGY 打包路徑。
- 不加入任何替代 OCR、UIA、雲端 API 或付費服務。

【快捷鍵快速喚醒】
舊版 Ctrl+~ 隱藏時會立即關掉 ClipShelf.UI.exe，因此下一次 Ctrl+~ 必須 cold-start WPF。
v2.11.0 改成 shortcut-only bounded warm standby：
- Ctrl+~ 隱藏後，完整 UI 最多保留 5 分鐘但保持不可見。
- 5 分鐘內再次 Ctrl+~，直接喚醒同一個 UI process，不需要重新載入整個 WPF。
- 5 分鐘沒有再次喚醒，UI 自動真正退出，只留下輕量 ClipShelf.exe Core。
- 明確的真正隱藏 / Exit 路徑仍可立即關閉 UI，不把 WPF 變成永久常駐 Core。

【刻意沒有改】
- 不改 history.json schema。
- 不改 %LOCALAPPDATA%\ClipShelf。
- 不改 Safe Delete / Recently Deleted。
- 不改 Backup / Import Preview / Content Dedup。
- 不改 clipboard monitor。
- 不重寫 Core / UI 分離架構。

【實機驗收】
1. 完整離開舊版 ClipShelf。
2. 解壓本 source package。
3. 執行 BUILD_EXE.cmd，要求 BUILD SUCCESS。
4. 啟動 ClipShelf_READY\ClipShelf.exe。
5. 確認原 History / 分類 / 設定仍存在。
6. 確認主視窗、tray、bubble 都沒有 OCR；Ctrl+Alt+O 不再由 ClipShelf 接管。
7. Ctrl+~ 打開 ClipShelf，再 Ctrl+~ 隱藏，幾秒後再按 Ctrl+~；第二次喚醒應明顯比舊版 cold-start 快。
8. 確認 Backup / Import Preview 仍可開啟。

【驗證狀態】
本包已完成 source-level removal / reference scan / XML parse 等靜態檢查。
Windows 10 .NET 8/WPF 的真正 BUILD 與快捷鍵體感，仍以使用者實機為 authoritative runtime evidence。
