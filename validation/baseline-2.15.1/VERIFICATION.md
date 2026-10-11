# ClipShelf 2.15.1 驗證分級

基準：正式 Drive 2.15.0 Source ZIP，516191 bytes，SHA-256 `505ec65ff91e4ab7da42b16ca36d0d8ba91249227adaf3364d129c16ceb6286a`，2026-10-11 即時查詢無更新 ClipShelf 基準，沒有回退 2.13.0。USER 附圖證明其 2.15.0 快捷介面已實際顯示，並報告浮動圖示失蹤；不是 2.15.1 native PASS。

本次修補：浮動顯示獨立持久設定；Core 管理與快捷請求不再隱藏圖示；既有唯一 BubbleForm、TopMost/NoActivate／拖曳不變。Quick Picker 使用 Extended 原生 Shift/Ctrl 點選，清單 Ctrl+A，列 ×／Delete／右鍵／多選 footer 的批次刪除沿用 SoftDeleteItems，不永久刪除。多選文字以顯示順序合併複製／貼上，混合圖片／檔案拒絕批次。資料夾來源接 canonical folder paths 與原分類欄位，預設 History，搜尋限目前來源，不操作 manager selection／draft。Deactivated 收合不把焦點搶回原目標；保持開啟設定保存；內部 ComboBox／context menu／owned preview 與貼上 busy 交易豁免。按鈕或資料夾 Enter 不觸發貼上，連續雙擊刪除不貼上替代列。

自動化：保留 64 項既有回歸，新增18項 folder projection／batch copy guard／dismissal policy／float setting 與 Core 跨設定保存，共82 PASS。實際 production named-pipe 的4項傳輸測試仍被 Linux socket AccessDenied 阻擋，不算 PASS。詳 tests.log。WPF 事件由 XAML compile／static wiring 驗證，沒有把純模型檢查稱為 Windows 滑鼠實測。

建置：.NET SDK8.0.425；Core 與 WPF 各自 Release win-x64 self-contained publish 成功，包含 Windows runtime8.0.31。NSIS3.09 amd64 Unicode 安裝檔；實際離線封裝檢查數、932個 published payload 的解包 SHA-256 結果在 package-checks.json，所有 EXE 的 version resources 必須2.15.1／2.15.1.0。完整 user-data protections 六個檔案 byte-identical to verified2.15.0。installer 不清 `%LOCALAPPDATA%\ClipShelf`，升級要求 Core 完全退出。

Chat Build：新增 Windows Actions workflow、BUILD_PORTABLE.py、CI_BASELINE.py、CI_RELEASE.py、CHAT_BUILD_HANDOFF.md、initial protected hashes。YAML／Python syntax 與 Workflow 靜態設定檢查通過；未建立私有 ClipShelf Repo，Connector 沒有 create-repository 操作，故沒有遠端 Windows run／job／artifact，`CHAT_BUILD_READY=NO`。USER 的這次追加授權只對 ClipShelf 工程 GitHub 鏡像，不復活全 Library Git migration。不要混用唯一可存取的 ai-library repo。

受管 Linux 的 Python 直接啟動部分 .NET child process 受 `/proc` 存取限制，曾出現 CLI StartTime／MSBuild ProcessName 錯誤，一鍵建置沒有通過；實際本輪 source／installer 以逐條正式工具命令完成並校验。保留 host-limitations.log，不能聲稱一鍵流程或一般 Chat 已端到端通過。此限制不屬於 Windows產品UI測試結果。

Windows 原生未驗：圖示始終置頂且設定開關正確、兩種介面切換、Shift/Ctrl 滑鼠範圍選取、Ctrl+A焦點分流、Delete／恢復、點外／desktop 收合、保持開啟、內部選單豁免、右鍵命中行、連續點擊、防誤貼上、IME、Edge／Notepad++／Obsidian 貼回原焦點、125/150%DPI／多螢幕／原地升級／退出重啟。沒有 Windows GUI，不宣稱實機通過。

暖機 P95、隱藏1／10／30分鐘喚醒、working set／private memory／CPU／長時間成長：`NOT_MEASURED`。Diagnostics/Measure-Wake.ps1 與真實 CSV 量測入口保留；不得填虛構效能数字。

回退：上一個已交付2.15.0 Source／Setup保留，Windows已有USER顯示介面及bug的部分觀察，不當作全功能known-good；歷史2.14.2交付保留。先備份、系統匣退出，再原地安裝前版；不直接覆寫history.json。新版本仍為已建置／封裝候選，native acceptance待使用者實機。
