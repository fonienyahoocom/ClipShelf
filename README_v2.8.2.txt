ClipShelf v2.8.2 — 移除指向文字擷取

本版依使用者要求移除 β 指向擷取：主視窗按鈕、浮動圖示選單、設定入口、Core/UI 事件路由、UIA helper 與舊 TextProbe source。
一般 Ctrl+C 剪貼簿記錄、文字/圖片/檔案歷史、分類、Recently Deleted 與 Safe Delete 保留。OCR 暫不加入。

更新：
1. 從浮動圖示或通知區選「離開 ClipShelf」，完整關閉舊版。
2. 照原本方式解壓此完整原始碼包，可替換舊程式資料夾；不要刪使用者資料資料夾。
3. 執行 BUILD_EXE.cmd，看到 BUILD SUCCESS 後啟動 ClipShelf_READY\ClipShelf.exe。
4. 確認既有歷史／分類／設定、圖片與保存附件正常；一般複製仍會加入歷史；β 指向擷取入口不再出現。

資料位置沿用 %LOCALAPPDATA%\ClipShelf，並保留自訂 storage 路徑。若你自行把附件存到舊程式資料夾內，不要連附件一起刪除。
HistoryStore.cs、CoreStateStore.cs、AppSettings.cs、ClipItem.cs 完全未改；旧 ExperimentalPointerTextCapture 欄位只為資料相容而保留，不再啟動功能。

這是完整 source 更新包，並非此環境已編譯的 EXE。已做靜態檢查；這裡沒有 Windows/.NET，Windows 編譯與執行待實機驗證。
若建置失敗，傳回 BUILD失敗_把這個傳給ChatGPT.txt。
舊 README / SOURCE_CHECK / TEST 文件為歷史紀錄，以本 README 和 DEVELOPMENT_HISTORY 最上方 v2.8.2 為目前狀態。
