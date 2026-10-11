# ClipShelf｜一般 Chat 施工入口

工程 Repository：https://github.com/fonienyahoocom/ClipShelf 。依 USER 2026-10-11 最新授權為 Public。
正式專案 owner：`/Codex|工作區治理/ClipShelf/`；原 Drive：https://drive.google.com/drive/folders/1VZk-qoa7lbTtmY2pBWDqo4tBYs7qHwTq 。
本文件是操作入口；當前版本、Source/Setup、已驗證 commit/run/artifact、交付雜湊與 CHAT_BUILD_READY 由 owner 的 `01_CURRENT_STATE.md`、`02_ASSET_INDEX.md` 和最新 ChatBuild 驗證報告持有。不得把文件或 YAML 的存在當成驗收。

## 下一次說「更新 ClipShelf」時

1. 讀 owner 的 READ_FIRST、CURRENT_STATE、Asset Index，查原 Drive 最新交付 manifest。用 GitHub Connector 查 `fonienyahoocom/ClipShelf` 的 main head、此文件和實際要修改的 Source。核對 lineage，不用舊聊天重建；GitHub 是授權的工程版本控制與建置入口，Drive/Library 仍持有正式發布狀態。
2. 從核對後的確切 main SHA 建立修改分支：`create_branch(repository_full_name="fonienyahoocom/ClipShelf", branch_name="clipshelf/<目的>-<日期>", sha=<main SHA>)`。用 `fetch_file` 取得完整原文及 blob SHA，再 `update_file(...branch=<新分支>, sha=<原 blob SHA>, content=<真實修改>)`。二進位檔使用 create_blob(base64)；多檔可 create_tree → create_commit → update_ref，帶 expected_sha，禁止盲 force。
3. 工程根目錄：`ClipShelf.Core/`、`ClipShelf/`、`Shared/`、`Tests/`。功能版本更新必須同步 Core/UI csproj、NSIS Product/FileVersion、About/診斷文字及說明；資料安全及使用者資料路徑不改。管線調整可保持產品版本，但新的安裝包必須依 commit/run/hash 與同版舊包明確區分。禁止提交使用者 history、token 或憑證。
4. `create_pull_request(...head=<修改分支>, base="main")` 觸發 `.github/workflows/windows-build.yml`；後續同 PR commit 自動重建。不需 Workflow Dispatch，也不要求一般 Chat 具備 Windows/.NET/NSIS。
5. `fetch_commit_workflow_runs(repo_full_name="fonienyahoocom/ClipShelf", commit_sha=<PR HEAD SHA>)` 找到該提交的 PR run。此工具只回 PR runs；不要用 main push 查不到結果來判定未建置。`fetch_workflow_run_jobs(...run_id)`、`fetch_workflow_job_steps(...job_id)`、`fetch_workflow_job_logs(...job_id)` 分別查看 Windows job、步驟與失敗日誌。失敗時修同一分支並提交；不降版、不請 USER 手動編譯。
6. Windows-2022 Runner 固定 SDK 8.0.425、Python 3.12/UTF-8、pefile 2024.8.26、NSIS 3.11。`.ci/Install-NsisAmd64.ps1` 下載同版 Debian nsis-common_3.11-1，核對 SHA-256 `103a3284c1a5356efa0aba90fdfc391c3cde8e7df8726377cef0f98471584047`，補齊 x64 stubs/plugins。執行真實 Tests（含 production IPC）、Core/WPF win-x64 self-contained publish、NSIS、7-Zip 解包、版本及逐檔 payload hashes、Source ZIP CRC。`CI_RELEASE.py` 記錄實際 SOURCE_COMMIT；PR 的 GITHUB_SHA 可是 merge ref，不能當成真正 checkout 的 Source SHA。
7. run/job 必須 completed + success，必要步驟不得 skipped。`fetch_workflow_run_artifacts(...run_id)` 找到未過期 release ID，`download_workflow_artifact(repo_full_name="fonienyahoocom/ClipShelf", artifact_id=<ID>)` 取得真正 ZIP 檔案。核對 ZIP digest、run/head、manifest/BuildReceipt 版本與 Source/Setup 的實際大小及 SHA-256，檢查 PE x64/version resources。只有拿到可用 Setup bytes 才算取得安裝版。
8. 檢查並合併已驗證 PR，帶 expected_head_sha；保留驗證分支/PR/run 證據。交付物以新檔名或獨立發布名稱保存原 Drive，不覆寫舊版；重新下載並比對實際雜湊。依當次 live Library governance staging → payload → indexes → CURRENT_STATE last，保留 stable identity、版本鎖並 discovery/read-back。只有全部成功才更新 CHAT_BUILD_READY。

## Artifact、權限和恢復

Release Artifact 內有 Source ZIP、Setup.exe、BuildReceipt、版本清單、更新與測試說明、使用說明。Logs Artifact 提供本次實際命令輸出；下載 Artifact 後仍須驗 bytes。保留期限7天，25分鐘 job timeout，同 PR 新提交會取消舊 run。Artifact 是暫存結果，原 Drive/Library 的正式副本才是持久交付；過期後從相同核對 commit 重新建置並重新計算雜湊，不沿用舊 hash。

一般 Chat 需要同帳號 GitHub Connector 的 source/branch/PR/Actions/log/artifact 操作，以及檔案分析/交付目的地存取；不需要 Work 或本機 Windows SDK。若某個聊天室沒有所需工具，回報缺少的操作，不能把「此聊天室沒有工具」說成整條管線不存在。Connector 403、Actions disabled、預算/runner 配額、依賴下載失敗、Artifact 過期和下載失敗分別診斷；不索取 token，不改使用者預算，不以另一個舊 Source 回退。GitHub 暫時不能用時仍保留最後正式 Source；有新的候選先核對 commit lineage，再修復同步。

Windows Console Tests、Build/NSIS/解包通過，仍不代表原生互動 GUI 驗收。Edge/Notepad++/Obsidian 貼上焦點、中文 IME、DPI/多螢幕、圖示/點外/釘選、多選刪除/恢復、原地更新以及1/10/30分鐘喚醒/CPU/RAM，需要獨立 Windows GUI/USER 證據；不得由 Actions 成功升格。
