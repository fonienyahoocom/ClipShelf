ClipShelf v2.12.0 — Windows Multi-Select + Safe Rapid Delete

本版直接基於 v2.11.0 OCR Removed + Fast Hotkey Wake。

【這版只做兩件事】
1. 讓 History 卡片空白區支援 Windows 常見的 Ctrl / Shift 多選手感，不搶走文字區原生反白。
2. 修正連續快速點垃圾桶時，可能誤觸 ListBox 雙擊貼上而複製內容、貼到前景程式並關閉 ClipShelf 的失控路徑。

【Windows 式多選】
- 文字內容區仍是原生唯讀 TextBox：直接拖曳、Shift/Ctrl 在文字內仍屬於文字選取，不會變成卡片多選。
- 在卡片右側空白／卡片 chrome 區：
  - Ctrl + 左鍵：單筆加入／取消選取。
  - Shift + 左鍵：從目前 anchor 到目標做連續區間選取。
  - Ctrl + Shift + 左鍵：把區間加入既有選取。
- 不需要先按「多選」；第一次 Ctrl/Shift 卡片點擊會自動進入多選模式並顯示勾選框。
- 已標記的多筆卡片都有選取底色，不只最後一筆有視覺提示。
- 原本「多選／完成」「全選」與批量移除／復原／永久刪除功能保留。

【連續垃圾桶防失控】
實際 source 顯示：HistoryList 有 MouseDoubleClick → PasteItemAsync → CloseForTrueHide 路徑；舊 handler 只排除文字 TextBox，沒有排除卡片上的 Button。
因此快速連點垃圾桶時，Button 的連續 click 可能同時形成 ListBox double-click，進而被誤判成「雙擊卡片貼上」，造成複製／貼上並退出主面板。

v2.12.0 改成：
- Button / CheckBox 永遠不是卡片 double-click activation surface。
- 每次垃圾桶動作後短暫 disarm History double-click activation，保護 row refresh / virtualization recycling 的瞬間。
- 同一個短暫安全窗內，若版面 reflow 讓游標意外落到「複製／收藏／更多」，這些鄰近 action 會被忽略，不會誤操作。
- 不對垃圾桶本身加 cooldown；垃圾桶仍可快速連續刪除下一筆。

【刻意沒有改】
- 不改 history.json schema。
- 不改 %LOCALAPPDATA%\ClipShelf。
- 不改 Safe Delete / Recently Deleted 語義。
- 不改 Backup / Import Preview / Content Dedup。
- 不改 v2.11.0 的 Ctrl+~ 5 分鐘 warm standby。
- 不恢復 OCR / UIA / 雲端 API。
- 不合併 Core / UI。

【實機驗收】
1. 完整離開舊版 ClipShelf。
2. 解壓本 source package。
3. 執行 BUILD_EXE.cmd，要求 BUILD SUCCESS。
4. 啟動 ClipShelf_READY\ClipShelf.exe。
5. 在文字內容本體拖曳反白：應仍能正常選字，不進入卡片多選。
6. 先點一筆卡片空白區作為目前項目，再 Shift+點另一筆右側空白：中間區間應全部被選取。
7. Ctrl+點任一筆空白：只切換該筆；再 Ctrl+點另一筆可跳著選。
8. 不先按「多選」，直接 Ctrl/Shift 點卡片空白，也應自動進入多選。
9. 在 History 連續快速點多筆垃圾桶：可以連續移除，但不得誤觸複製、貼上或讓主面板消失。
10. Ctrl+~ warm wake、Backup / Import Preview、Safe Delete 仍正常。

【驗證狀態】
本包已完成 source-level patch、XAML/XML parse、reference scan、archive integrity 與靜態 regression 檢查。
Windows 10 .NET 8/WPF 的真正 BUILD 與互動手感仍以使用者實機為 authoritative runtime evidence。
