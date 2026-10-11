# ClipShelf 2.14.2 工作檢查點

來源：指定 Drive 的 v2.14.1 Source.zip（SHA256 d9fcd2569ffd9e270b1a3cf8d477d4e4ba2e6ebd9657cceaf784034d7daf4ab7），最新檔名、ID、修改時間與大小已重新查詢一致。

使用者錄影：ClipShelf.UI_THCQk6UJw6.mp4，135.7 秒。觀察：選取後 Delete 不改正文、單擊文字擴張與複製重排、空白提示框、跨資料夾保留上一個編輯狀態。

本次局部修補：直接正文編輯入口；選取後 Delete/Backspace、貼上或輸入進入原地草稿；清楚的編輯中標記與上方儲存/取消；正文不彈提示框；複製不重建/重排目前列表；可見編輯框不強制 ScrollIntoView；切換資料夾/類型先處理草稿。

程式已凍結；Core/WPF Windows x64 發佈、40 項資料／模型／編輯服務測試與 29 項封裝檢查已通過，930 個 payload 檔案逐檔 SHA-256 相符。下一步：Drive 新建交付與重新下載核對；最終雲端狀態另記外部版本交付清單。Windows 真實互動仍未驗證，不重試已證實受主機 socket 限制的 Wine 路徑。

原 v2.14.1 驗證報告在 validation/baseline-2.14.1；v2.14.0 歷史保留。
