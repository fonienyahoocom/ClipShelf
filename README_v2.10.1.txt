ClipShelf v2.10.1 — 備份匯入預掃描／內容去重 + 本機框選 OCR

本版基於 v2.10.0。匯出格式與 v2.10.0 的 *.clipshelfbackup 相容；主要改動集中在「匯入前先掃描、先去重，再由使用者確認」。

【為什麼改】
v2.10.0 主要以 Guid ID 防重複。這只能保證同一筆原始紀錄不重複；若內容相同但 ID 不同，仍可能被再次加入。
v2.10.1 不再只靠 ID。

【新的匯入流程】
1. 選擇 .clipshelfbackup。
2. ClipShelf 先做唯讀預掃描；此階段不改 history.json，也不把 payload 解到 storage。
3. 預覽視窗顯示：
   - 確定是新內容
   - 同一筆 ID 已存在
   - 內容相同（預設跳過）
   - 無法安全判定是否重複
   - 新增資料夾／子資料夾
4. 預設只匯入「確定是新內容」。
5. 「無法安全判定」預設不匯入；使用者可明確勾選後才一起匯入。
6. 真正確認後，才開始把核准項目的實體內容解到 storage\imports\...，再合併 History／資料夾。
7. history.json 的 atomic commit 成功後才回報完成；提交前失敗會回復記憶體狀態並移除本次 staging payload。

【內容去重判斷】
- 文字：依完整文字內容建立 SHA-256 指紋。
- 圖片：依實際圖片檔 bytes 的 SHA-256 判斷；不依舊絕對路徑。
- 單一／多個檔案：依檔名與實際檔案 bytes 判斷。
- 資料夾：依子資料夾結構、相對路徑與檔案內容 hash 建立 deterministic fingerprint。
- 同一份備份內若本身就有內容重複，也只把第一個確定新品列入預設匯入。

這套 dedupe 對應 ClipShelf 現有模型：同一文字／圖片內容本來就以 AddOrTouchItem / UseCount 合併，而不是刻意保存多份完全相同物件。

【無法安全判定】
例如目前某個舊 Image / Files 項目的實體檔已遺失、無權限讀取，或備份 payload 本身不完整，就不能可靠算出內容指紋。
這些項目不會被偷偷當成新品；預設跳過並列入「無法安全判定」。若使用者確定需要，才可勾選「也匯入無法安全判定的項目（可能產生重複）」。

【資料安全】
- 預掃描是 read-only，不修改 %LOCALAPPDATA%\ClipShelf。
- 匯出仍然是 read-only，不修改 history.json。
- 匯入仍然不會先清空 History，也不會把 backup state.json 直接覆蓋現有 history.json。
- 重複 ID / 重複內容在預設路徑中不會被解壓到 storage，因此不會先製造一批無用的重複 payload。
- Stable Core runtime 行為未因去重功能改寫；本版只更新版本號。

【OCR】
v2.9 起的本機框選 OCR 保留：Ctrl+Alt+O、主視窗「框選 OCR」、浮動圖示／通知區入口照舊。
不加入雲端 API、API Key、token 費用或背景畫面掃描。

【更新／測試】
1. 完整離開舊版 ClipShelf。
2. 解壓本 source package。
3. 執行 BUILD_EXE.cmd。
4. 要求 BUILD SUCCESS。
5. 啟動 ClipShelf_READY\ClipShelf.exe，先確認舊 History 仍存在。
6. 匯出一份備份，再立刻匯入同一份：預覽應主要顯示同 ID／內容重複，而不是新增大量項目。
7. 再用另一份含部分新品、部分重複的備份測試；確認預覽數量合理後才按匯入。

【驗證狀態】
目前開發環境沒有 Windows .NET 8/WPF build/runtime，因此不宣稱 Windows 實機 BUILD SUCCESS。
本包已做 source/XAML/XML/event-handler/package 靜態檢查；真正 build 與 round-trip 以使用者 Windows 實機為準。
