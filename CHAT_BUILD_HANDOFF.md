# ClipShelf｜一般 Chat 遠端建置入口

2026-10-11 本版快照：`PIPELINE_SOURCE_PREPARED / REPOSITORY_CREATION_BLOCKED / CHAT_BUILD_READY=NO`。
此快照不是日後 current authority。每次先讀 Library owner `/Codex|工作區治理/ClipShelf/00_READ_FIRST.md`、`01_CURRENT_STATE.md`、`02_ASSET_INDEX.md`，再查正式 Drive manifest 和 Source，核對實際大小／SHA-256，禁止回退過時工程。

## 精確入口與權威

- 已核 GitHub 帳號：`fonienyahoocom`。已核可存取私有 repo：`fonienyahoocom/ai-library`；它不是 ClipShelf repo，本輪未修改。
- ClipShelf repo 實際位置：**尚未建立／未取得**。預定私有位置 `https://github.com/fonienyahoocom/ClipShelf`，不能把這個預定 URL 當成已存在。
- 當前 Source／Setup 的正式位置由 Library CURRENT 和 Asset Index 指向；Drive 資料夾 `https://drive.google.com/drive/folders/1VZk-qoa7lbTtmY2pBWDqo4tBYs7qHwTq`。
- GitHub 僅工程鏡像／建置入口；沒有取消 Library owner 或 Drive 交付責任。本轮 USER 明確授權 ClipShelf 專屬私有 GitHub 工程，不復活已退役的全 Library Git migration／Tier 2 mirror。
- Repo 建立後將完整 Source 根目錄檔案作 repo root；工程為 `ClipShelf.Core/`、`ClipShelf/`、`Shared/`、`Tests/`；Workflow 為 `.github/workflows/windows-build.yml`。初始化時帶入 `.ci/initial-baseline-sha256.json`，沒有前個工程 commit 時先比對已驗證的 2.15.0 六個資料安全檔案；一般 PR 改用真實 base commit。

## 一次性缺口

本聊天室 GitHub Connector 實讀 profile、repo list、installed repo search 成功，有分支、檔案、tree/commit/ref、PR、Workflow runs/jobs/logs/artifact 工具，沒有 create-repository 操作；本機也沒有 GitHub CLI／登入 token。不能藉此宣稱 repo 已建立或 Actions 已執行。
需要建立**私有** ClipShelf repo（可初始化 README）並授予現有 GitHub Connector 存取。不要將原始碼公開、不索取或保存使用者 token。若採 GitHub 網頁代建，需先取得本次 Browser fallback 允許；只缺一次性入口，不需使用者手動編譯。

## Repo 可用後的一般 Chat 操作路徑

1. 用 Connector `get_profile`、`list_repositories`／`search_installed_repositories_v2` 核對 repo 身分、private、push 權限；確認 owner CURRENT 同版。首次載入完整最新 Source 到 root。文字可用 create/update file，二進位 ICO 必須用 create_blob(base64) → create_tree → create_commit → update_ref；保留精確 bytes。主分支更新要求 expected head SHA，不 force 覆蓋他人變更。禁止上傳 `%LOCALAPPDATA%` 使用者歷史或憑證。
2. 取得 repo main 的確切 head SHA；從該 SHA `create_branch`，例如 `clipshelf/quick-picker-fix-YYYYMMDD`。用 `fetch_file`／`fetch_blob` 讀取真實 source；在這條分支修改、加測試、更新版本資源及說明。不要從舊聊天室重建產品。
3. 以原 blob SHA 的 update_file 或 tree/commit 提交，開向 main 的 PR。Workflow 用同 repo PR 事件自動觸發；不需 Workflow Dispatch。push main 亦觸發。避免逐檔 main commit 產生大量重複 build；先做單一整合 commit，再 PR。
4. 用 `fetch_commit_workflow_runs` 找該 PR head SHA 的 run（此工具只回 PR runs）。用 `fetch_workflow_run_jobs` 確认 Windows job，`fetch_workflow_job_steps` 和 `fetch_workflow_job_logs` 查每一步與失敗原因。run 未完畢／失敗／取消皆不得當成功。首次 main import 的 run 用 repo Actions URL／GitHub fetch collection 查詢；正式 E2E 固定用 PR，避免 Connector 的 PR-only 查詢限制。
5. Workflow 在 `windows-2022` 固定 SDK 8.0.425，安裝 NSIS 3.11／pefile，執行真實 Tests（Windows 也要跑 IPC）、Core/WPF self-contained publish、NSIS、7-Zip 解包及版本／逐檔 SHA-256，最後產 Source ZIP、Setup、BuildReceipt、說明及版本清單。工具缺失或 package 校驗失敗立即停止。純建置不是互動 GUI 驗收。
6. run success 後 `fetch_workflow_run_artifacts` 列出未過期 release artifact ID，再 `download_workflow_artifact` 取真正 ZIP bytes；檢查 run head SHA、名稱、版本、大小、archive digest、manifest 內每個檔案 SHA-256。重新計算 Setup 和 Source bytes，不能只信 log PASS 或 artifact 名稱。必須能拿到實際檔案才算取得安裝版。
7. 經 Source 差異與真實測試驗證後，以候選狀態交付原 Drive，重新下載核對 SHA-256。依當時 Library root／owner／staging／overwrite 規則更新 Source／Release 指向、Asset Index、CURRENT_STATE，payload first，control last，重新 discovery＋read-back。Actions artifact 不是正式持久交付；舊版不能刪除或覆蓋。

## 首次端到端驗收

Repo 建立後，先讀回初始化 Source 與正式 ZIP 每一檔 bytes 比對；建立測試分支，僅在 README 加一行「受控 E2E 建置驗證」，提交并开 PR；等待 Windows job 完成，核對 Tests、publish、NSIS、package logs。下載 release artifact，驗實際 Source／Setup version／hash，再按正式交付規則保存並讀回。記錄 repository、base/head SHA、PR/run/job/artifact ID、實際檔案大小與hash、交付讀回證據。驗證流程可由一般 Chat Connector 完成、不要求本機 Windows。只有全鏈通過，CURRENT_STATE 才能標 `CHAT_BUILD_READY`。
本版尚未執行這次 E2E，不能將 Workflow YAML 或 Linux cross-build 當作遠端 Windows PASS。

## 配額、期限及故障恢復

Workflow 使用標準 Windows runner、25 分鐘 timeout、同 PR 最新 run concurrency、artifact 保留7天，沒有新付費服務或雲端憑證。Actions 的私有 repo 配額／預算／餘額尚未查證；GitHub 官方說明 https://docs.github.com/en/billing/concepts/product-billing/github-actions 。不可假設無限／免費或擅自調高預算。repo actions disabled、權限403、額度耗盡、缺 runner/toolchain、artifact 過期或Connector無法下載要分別回報；不能說 CHAT_BUILD_READY。

Actions 壞掉時先取失敗 logs，在同 PR 修補工具／程式並重新 commit 觸發；不偷偷降版。Artifact 過期時取相同已驗證 Source commit 重新建置並新取hash，不沿用舊安裝檔hash。GitHub source 比 canonical 新時它是候選，先核證 lineage；比 canonical 舊時先更新工程鏡像，不把旧 branch 覆蓋正式Source。Connector 暫缺時仍保留原 Drive／Library 最新正式交付。

原生 Windows GUI 尚需獨立驗證：浮動圖示、Shift/Ctrl 多選、Delete／恢復、點外收合／保持開啟、內部選單、目標貼上焦點、中文IME、DPI、多螢幕、原地升級和待機／喚醒資源。Actions 的 console tests 或成功 NSIS 打包不能證明上述 UI 全通過。
