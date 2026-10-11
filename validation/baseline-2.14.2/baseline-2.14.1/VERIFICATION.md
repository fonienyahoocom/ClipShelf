# ClipShelf 2.14.1｜更新說明與測試結果

交付狀態：安裝包已建置；Windows 原生執行與實機升級未驗證。這份報告區分建置、資料測試、離線封裝檢查與桌面執行。

## 施工來源

指定 Google Drive 工程資料夾：1VZk-qoa7lbTtmY2pBWDqo4tBYs7qHwTq。

- ClipShelf-2.14.0-Source.zip，檔案 ID：1fnHwlOUUGQexWPHcftyk3dtnPnWfJNSy，238459 bytes。
- 原始碼包 SHA-256：9daf008ea7277eccbb738bca86bfd8c26b74a33519ce0736ad877d50ba8ebddc。
- 舊安裝版：ClipShelf-2.14.0-Setup.exe，檔案 ID：1pf1EWOzDAg6A8x5QxawMgL89MWJTff99，49700456 bytes。
- 舊安裝包 SHA-256：4d5799bb5c961dd8d2142d5ecb50e50c684cdab78a4963427fc64930e336620a。
- 工程備案需求 v0.3，文件 ID：11tIMZ3z1xhkJmYLMjbPqx5mgTNvoJD9-lVLiuRzItnI；施工前與交付前確認修改時間均為 2026-10-09T08:36:48.559Z。
- 未使用其他帳號或舊版原始碼。未覆蓋、刪除 v2.14.0 交付檔案。

## 實際修改

**A／B 直接切換。** 主視窗篩選列加入兩個並排按鈕，與原「檢視 / 工具」共用 SetCompactView。仍使用 AppSettings.CompactView，A=false、B=true。B 的卡片內距由 12 縮為 8、圓角由 12 縮為 8、卡片下方間距由 8 縮為 4、文字上方間距由 7 縮為 4、收合預覽由 112 縮為 38；字級仍維持 14。切換直接更新原有項目並嘗試保持畫面頂端紀錄位置，不重建列表、不清除搜尋、多選標記或展開狀態。窄視窗允許切換列換行，保留工具選單。

**文字原地編輯。** 右鍵／更多選單「編輯文字」使用同一個原文字 TextBox，輸入時只改草稿。Enter 換行、Ctrl+Enter 儲存、Esc 取消。原生編輯右鍵提供剪下、複製、貼上、全選、復原和重做，以及儲存／取消。編輯鍵盤優先於視窗貼上、刪除卡片和搜尋的快捷鍵；IME 組字中的鍵不觸發儲存。編輯期間仍保存外部新剪貼紀錄，但延後列表重建至編輯結束，保留原生編輯框的游標與復原堆疊。一般單擊文字呼叫既有 CopyItem；拖曳、修飾鍵、既有選取和編輯狀態不觸發整筆複製。不新增卡片底部按鈕。

**標題原地改名。** 抬頭小鉛筆或右鍵「重新命名標題」啟用同位置單行編輯框。重用 Title；Enter 儲存、Esc 取消、空白恢復內容摘要。類型膠囊、日期、分類、收藏和拖曳仍是獨立欄位／控制項。改名不更改正文、雜湊、重複次數或日期；新標題使用既有搜尋流程。

**儲存與去重。** 新編輯入口使用既有安全寫入並立即檢查寫入結果；失敗會回復原紀錄並保留原地草稿。真正修改正文重新計算 T|內容 的 SHA-256，重複次數重設為 1，避免沿用舊內容的 ×n；相同正文再儲存則不重設。若撞到另一筆有效文字，拒絕修改並保留兩筆，避免無聲合併丟失名稱和分類。原完整文字對話框保留，改用相同內容雜湊／次數規則與安全寫入；原 Enter／Shift+Enter 用法保留。

Core/WPF 架構、設定格式、HistoryStore、SafeState、備份、匯入與 Safe Delete 程式保留基準。Core 業務程式只更新診斷版本字串；沒有加入 OCR。程式「關於」、Core 診斷、程序集與安裝包版本均為 2.14.1。

## 已通過的驗證

| 項目 | 結果 | 證據與範圍 |
|---|---|---|
| Core Windows x64 自含發佈 | 通過 | .NET SDK 8.0.425；core-publish.log；PE x64、版本 2.14.1.0 |
| WPF Windows x64 自含發佈 | 通過 | ui-publish.log；包含 WPF 原生與受管理執行環境；不等於桌面操作驗收 |
| 原 v2.14.0 資料測試 | 14 項通過 | 從指定原始碼重新執行，不借用舊報告；fail-closed、刪除、收藏、備份、恢復、去重與快照 |
| v2.14.1 資料與編輯測試 | 共 36 項通過 | data-test-build.log、data-tests.log；原 14 項＋新增 22 項，直接測試正式 ClipItem／ClipEditing／CoreStateStore／SafeState |
| NSIS 安裝包生成 | 通過 | NSIS 3.09 amd64 Unicode、installer-build.log；生成實際 Setup.exe |
| 離線封裝檢查 | 29 項通過 | package-checks.log、package-checks.json；安裝包解開後的 930 個檔案逐檔以 SHA-256 與此次發佈比較 |
| 程式與安裝包版本 | 通過 | PE 資源及已編譯「關於」／診斷字串核對為 2.14.1 |
| 安裝／移除的資料保護邏輯 | 靜態核對通過 | 安裝腳本僅更新版本；程式與資料路徑分離；移除清單只含此次程式檔案；沒有實際執行升級 |
| 執行環境差異 | 通過 | 共用執行環境檔案與舊安裝包位元組一致；變動為程式本體、PDB 與應用程式版本 metadata |

新增 22 項資料／編輯檢查包含：Compact 通知、展開保持、草稿隔離、JSON 不保存草稿、取消、正文與中繼資料分離、雜湊及重複次數、實際讀回、背景重新擷取去重、舊內容重新擷取、未改正文儲存、有效重複拒絕、空白正文拒絕、保存失敗回復、改名、改名讀回、改名失敗回復、空白名稱回退、已刪除重複內容、已刪除紀錄禁止改名、既有 B 設定讀回。完整逐項結果在 data-tests.log。

使用既有 BUILD_EXE.cmd／BUILD_SETUP.cmd 所定義的發佈配置、Core／UI 輸出結構和 NSIS 安裝腳本，在 Linux 執行等價建置。Windows PowerShell 啟動器未在 Windows 執行；.cmd 的 CRLF 與 .ps1 的 UTF-8 BOM 已核對。

## 尚未完成或受阻的驗證

- **原生 Windows 10／11 安裝、從 v2.14.0 實際升級、解除安裝及實際保留個人資料：未測。** 安裝語義沿用基準，已核對程式；沒有把靜態檢查說成實機通過。
- **WPF 真實互動：未測。** 包括 A／B 卡片實際密度、捲動與游標位置、普通單擊／拖曳選取、Ctrl／Shift 多選、上下文選單、鍵盤儲存／取消、IME 組字、高 DPI、外部程式貼上及全域快捷鍵。
- **Wine 執行：受阻。** 已取得 Wine 9，但主機禁止必要通訊 socket，啟動回報 `wineserver: socket: Operation not permitted`。這證明測試環境阻塞，不代表程式執行通過或失敗。未重用 v2.14.0 的 Wine 成功紀錄冒充新版證據。
- 安裝包沿用未簽章狀態。

## Windows 使用方法與待確認項目

先在舊版系統匣選「退出」，再執行 ClipShelf-2.14.1-Setup.exe；不用先解除安裝。仍安裝到目前使用者的 `%LOCALAPPDATA%\Programs\ClipShelf`，沿用 `%LOCALAPPDATA%\ClipShelf` 的資料，不需自行編譯或重新設定。

建議首次升級前做一次完整備份。Windows 實機應確認：開啟後原分類／收藏／歷史仍在；A／B 切換並重新開啟保留；原地輸入中文、復原、取消及儲存；改名／空白回退並重新搜尋；普通點擊複製與選取複製；最近刪除／復原、備份與匯入未退步。

這個版本不改資料 schema，若需要回退，可退出新版後使用保留的 v2.14.0 安裝包；此回退執行同樣未在 Windows 實機測試。

## 交付檔案

ClipShelf-2.14.1-Setup.exe、ClipShelf-2.14.1-Source.zip、ClipShelf-2.14.1-使用說明.txt、ClipShelf-2.14.1-更新說明與測試結果.md、ClipShelf-2.14.1-版本交付清單.json。

精確大小、SHA-256、來源 ID 與驗證狀態在版本交付清單；原始碼包包含此次程式、建置腳本、測試程式、測試紀錄、930 檔案的 payload 清單與此報告。舊版驗證放在 validation/baseline-2.14.0，屬歷史資料。
