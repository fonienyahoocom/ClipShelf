# ClipShelf 2.15.0：雙模式與常駐喚醒

狀態：`SOURCE_IMPLEMENTED / BUILD_PASS / PACKAGE_PASS / NATIVE_WINDOWS_UNVERIFIED`。
本輪是可安裝 Windows x64 候選交付，未提升為 USER Windows 實機通過。

## 基準與範圍

施工前重新查詢 ClipShelf 專屬 Drive 資料夾，最新有效交付為 2.14.2，沒有更高正式版本。
原始碼為 `ClipShelf-2.14.2-Source.zip`（396817 bytes），SHA-256：
`b3e0b65f87935507afe8897d9823b04f584b09239eac44b18d02a815f3362ff7`。
以實際 ZIP、交付 manifest、時間與 hash 校對。Library 2.13.0 metadata 為過時狀態，未用它回退 source。
新增獨立工作模式，因此版本由 2.14.2 升為 2.15.0；EXE、DLL、Setup 版本資源一致。
Core／管理／儲存採局部修補；新能力為同一 WPF 程序內的獨立 QuickPickerWindow。

## 實作結果

- Ctrl+~ 與浮動圖示／系統匣左鍵開啟小型快捷清單；各右鍵選單提供兩種視窗入口；主視窗左上增加小按鈕。
- 真實歷史快照、搜尋、虛擬化、鍵盤／單擊選取、雙擊／Enter 貼上、Esc、Ctrl+C、Shift+Enter、全文預覽、圖片縮圖及視窗位置保存。
- 清單本身不建立資料庫；共享 ClipItem 與原有 HistoryStore。搜尋與選取不動管理草稿、A/B 狀態或多選。
- 開啟期間維持快照順序；更新以增量集合保留容器，既有紀錄使用次數變動不重新排序；新紀錄加在尾端。
- Paste 保存 HWND 與 PID，驗證目標仍存在、排除自家程序，等實體修飾鍵放開後恢復焦點並送出一次 Ctrl+V。失敗顯示複製／手動貼上提示。
- 單筆操作閘門、熱鍵 MOD_NOREPEAT 與 120ms 抑制；剪貼簿序號防止內部寫入重捕獲。IPC 請求携帶獨立目標與 ACK。
- 快捷鍵註冊回傳結果；衝突時系統匣提示，設定提供 Ctrl+Alt+V／Ctrl+Alt+Q，拒絕 Ctrl+Shift+F8。
- WPF 啟動時在背景預熱，取消 45 秒／300 秒退出；同一 Quick Picker 重用，真正退出先保存，失敗取消退出。
- 隱藏時略過定期附件全文重掃。無新增引擎、資料庫、雲端、Hook 或滑鼠輪詢；Core 與 WPF 維持分離。
- 浮動圖示右鍵：開啟原生選單期間暫時允許 owner 接受焦點、釋放 drag capture，關閉後恢复 no-activate。這是最小候選修補，尚未 Windows 實測點外關閉成功。
- Safe Delete、備份／匯入 merge、去重與 fail-closed 元件保留原檔；沒有重寫儲存格式或清除使用者資料。

## 自動化與建置

| 層級 | 本輪結果 | 邊界 |
|---|---|---|
| 原有資料／編輯回歸 | 40 PASS | 使用原有真實 Shared／ClipItem／儲存程式 |
| 快捷模型與設定 | 24 PASS | 搜尋、穩定排序、12,000 筆、選取／重複操作、同步、草稿、持久化、Core 設定相容 |
| 實際 named-pipe IPC | 4 BLOCKED | Linux host `SocketException: AccessDenied`；不記為 PASS；Windows 測試會實際執行 |
| Core／WPF | BUILD PASS | .NET SDK 8.0.425、Windows runtime 8.0.31，Release win-x64 self-contained；零編譯警告／錯誤 |
| NSIS | PACKAGE PASS | NSIS 3.09 amd64 Unicode；重新解包逐檔比較 SHA-256，詳見 source validation |
| 原生 Windows GUI／Installer | UNVERIFIED | 本環境無 Windows 桌面，未使用 Wine 冒充驗收 |

Source ZIP 保留完整工程、Tests、Diagnostics、當次 build/test/package 日誌、修改 diff 及逐檔 payload hash。
舊 validation 移至 `validation/baseline-2.14.2/`，其 PASS 不可移算本版。
測試 fixture 只用臨時資料；封裝無 history.json 或使用者私有剪貼內容。

## 效能與待驗證矩陣

| 項目 | 結果 |
|---|---|
| 啟動後首次喚醒 | NOT_MEASURED |
| 隱藏後立即／1／10／30 分鐘喚醒 | NOT_MEASURED |
| 暖機 P95 <250ms | NOT_MEASURED；目標，未宣稱達成 |
| 閒置 Core/WPF CPU、working set、private bytes | NOT_MEASURED |
| 多次連續喚醒、記憶體成長 | NATIVE_UNVERIFIED |
| 管理→快捷／鍵盤與滑鼠／位置記憶 | SOURCE_IMPLEMENTED；NATIVE_UNVERIFIED |
| Edge／Notepad++／Obsidian／一般輸入框 Paste | SOURCE_IMPLEMENTED；NATIVE_UNVERIFIED |
| 中文 IME、125%／150% DPI、混合 DPI／多螢幕邊緣 | NATIVE_UNVERIFIED |
| 圖片／檔案／長文字、快捷鍵冲突、目標關閉／權限拒絕 | 模型／來源檢查；NATIVE_UNVERIFIED |
| 浮動右鍵選單點外關閉、不干擾／置頂／拖曳 | PATCH_IMPLEMENTED；NATIVE_UNVERIFIED |
| 原地升級、有資料、退出／重啟、完整恢復 | 自動化資料回歸 PASS；Windows 操作仍 UNVERIFIED |

執行 `Diagnostics/Measure-Wake.ps1` 可取得真實 Windows 熱鍵與常駐採樣；本輪只交付此採樣工具，沒有執行結果。
程式診斷 CSV 量測範圍是 Core 收到熱鍵→IPC→WPF ContextIdle，非螢幕首像素完成；含 PID 以判定是否冷啟動。
採樣工具 50 次暖機計算 P95，分別隱藏 0／60／600／1800 秒後喚醒，再量測 10 秒閒置 CPU 與記憶體。
權限更高的目標可能受 Windows UIPI 拒絕；成功 SendInput 只表示 Windows 收到，目標 app 無回覆協定，需確認實際文字位置。
純文字歷史不含 HTML／RTF；Shift+Enter 與 Enter 對文字輸出相同 Unicode 文字，沒有新增富文字引擎。

## 回退與保存

保留 Drive 上 2.14.2 完整交付，及原 Library 歷史 source，不覆寫或刪除。
如本候選 Windows 行為不符，先做完整備份、系統匣退出，再執行 2.14.2 Setup 原地回退。設定新增欄位可由舊版忽略，歷史 schema 不變。
2.14.2 的 build/package 有證據，但不等於已取得其 Windows 原生實機 PASS。
歷史 USER 2.10.1 的 Build／startup／Import Preview 證據仍保留，不能推論其他版本或全部功能已實機通過。
當次五份正式交付物保存在原 ClipShelf Drive 資料夾，下載原始 bytes 再核大小／SHA-256；manifest 記錄身分。
Library current/source 指向更新為候選版並保留 rollback 與未驗證清單；寫回不提升 known-good。

## 設計參考

查閱 [Ditto 官方 Wiki](https://github.com/sabrogden/Ditto/wiki) 與 [官方 README](https://github.com/sabrogden/Ditto/blob/master/ReadMe.md)，採用緊湊清單、熱鍵、鍵盤選取、Enter／雙擊回原視窗與 Shift+Enter 純文字的 UX。
未複製第三方程式碼或引入 Ditto 資料庫／引擎。公開 implementation 頁面讀取遭搜尋服務拒絕，故不對其焦點實作作未核對的宣稱。
