ClipShelf v2.10.0 — 完整備份匯出／安全合併匯入 + 本機框選 OCR

本版基於 v2.9.0，不移除既有 OCR、剪貼簿、分類、最近刪除、Core/UI 分離等功能。

【完整備份】
主視窗上方新增「⇅ 備份」：
- 匯出完整備份…
- 匯入備份（合併，不覆蓋現有資料）…

匯出格式：*.clipshelfbackup

匯出包含：
- ClipShelf 目前保存的所有 ClipItem 記錄，包括文字內容、時間、來源、使用次數、History 顯示狀態、收藏、暫存、分類、最近刪除狀態等。
- 分類／資料夾與子資料夾結構。
- 圖片實體內容。
- Files 類型中的檔案實體內容。
- Files 類型若指向資料夾，會遞迴打包其子資料夾與檔案。
- 頂層側邊欄排序資訊。

匯出是唯讀式備份：
- 不會清空、搬移或改寫 %LOCALAPPDATA%\ClipShelf。
- 不會改寫目前 history.json。
- 只讀目前 ClipShelf 記錄與其引用的實體檔案／資料夾，再另外建立 .clipshelfbackup。
- 若某個原始檔案已不存在或無法讀取，備份仍可完成，但完成視窗會顯示警告數量。
- 若備份檔剛好存進一個正在被備份的資料夾，程式會略過正在產生的備份檔本身，避免把 archive 遞迴塞回自己。

【安全合併匯入】
匯入不是「整包覆蓋 %LOCALAPPDATA%\ClipShelf」。
流程是：
1. 先驗證 .clipshelfbackup 的 manifest / state。
2. 圖片、檔案、資料夾內容先解到目前設定的 ClipShelf storage\imports\... 新目錄。
3. 只有驗證與實體內容還原成功後，才把新項目與資料夾結構合併到目前資料。
4. 同一 Guid ID 已存在時跳過，不覆寫既有項目。
5. 寫入 history.json 必須成功，才宣告匯入完成；若提交前失敗，會還原本次記憶體變更並清掉本次新建的 import payload。

匯入不會：
- 先清空現有 History。
- 用備份 state.json 直接覆蓋目前 history.json。
- 覆蓋同 ID 的現有項目。
- 自動刪除目前分類、收藏、暫存或最近刪除資料。

匯入後的圖片／檔案／資料夾會變成 ClipShelf managed payload，放在目前設定的 storage 下；這樣來源電腦原本的絕對路徑不存在也仍可使用。
若備份是在 8 秒永久刪除 grace period 中建立，匯入後該項目會回到一般「最近刪除」，不會因舊 deadline 立即被 purge。

【v2.9 OCR 保留】
Ctrl+Alt+O、主視窗「框選 OCR」、浮動圖示／通知區入口照舊。
OCR 仍使用本機 Windows.Media.Ocr + Windows PowerShell 5.1；無雲端、API key、token 費用或背景畫面掃描。

【更新】
1. 完整離開舊版 ClipShelf。
2. 解壓本完整 source package。
3. 執行 BUILD_EXE.cmd。
4. 看到 BUILD SUCCESS 後啟動 ClipShelf_READY\ClipShelf.exe。

%LOCALAPPDATA%\ClipShelf 與自訂 StorageDirectory 不需要刪除，也不要手動初始化。
本版沒有把既有使用者資料搬進版本資料夾；新版本資料夾仍可獨立替換。

【驗證狀態】
目前開發環境已做 source diff、XAML/XML、事件 handler、archive/path safety 與 package structure 靜態檢查；此環境沒有 Windows .NET 8/WPF build/runtime，因此真正 Build Success 與匯出→匯入往返仍以使用者 Windows 實機為準。
若 build 失敗，傳 BUILD失敗_把這個傳給ChatGPT.txt。
