# ClipShelf 可攜式更新

ClipShelf 執行檔本身不依賴 ChatGPT、Work、Library 或 Drive。Source 現在提供獨立建置工具 `BUILD_PORTABLE.py`，可在具有執行工具的一般 Chat、Linux 工程容器或 Windows 電腦使用同一工程完成 Source、Windows x64 Setup 和驗證記錄。這不是遠端自動更新服務，沒有新增網路常駐或外部資料庫。

需要：Python 3.10+、.NET SDK 8、NSIS 3.x（amd64 Unicode stub）、7-Zip、`pip install pefile`。原生 Windows 也保留原 `BUILD_EXE.cmd` → `BUILD_SETUP.cmd` 路線。

可攜式指令：

```sh
python BUILD_PORTABLE.py --baseline /path/to/verified-previous-source --output /path/to/new-deliverables --dotnet /path/to/dotnet --makensis /path/to/makensis --sevenzip /path/to/7z
```

Linux 使用自解壓 NSIS 時可加 `--nsisdir /path/to/usr/share/nsis`。Windows 路徑含空格請加雙引號。工具路徑在 PATH 時可省略三個工具參數。輸出目錄不得包含既有同版 Setup 或 Source；工具拒絕覆寫。NSIS 必須包含 `amd64-unicode` stub。

工程流程為：先保存未修改正式 Source 作 baseline，再在另一份 Source 修補，更新 Core／UI csproj 三個版本屬性、NSIS 版本、About／Crash／CSV／README／build scripts，執行上述指令。工具檢查各版本一致，執行真實回歸測試、兩個 self-contained publish、NSIS、解包逐檔 SHA-256、來源回歸與 ZIP CRC，再輸出 Source／Setup／BuildReceipt。失敗即停止，不把部分 build 當正式安裝版。

新功能需要同步新增真正的測試，並更新 `validation/verify_release.py` 的驗收條件；不要為了通過而降低資料保護條件。歷史驗證檔案留在 `validation/baseline-*`，本次測試另外記錄。

一般 Chat 的能力由該聊天環境決定。若只有文字回覆、没有檔案執行工具，就不能直接修改 ZIP 或編譯 Windows 安裝包。若有 Python 檔案執行但沒有 SDK／NSIS／網路，能產出 source patch，不能聲稱 Setup 完成。工具齊全時本流程可直接製作成品，不要求 Work 專用 API；本版在受管 Linux 主機以逐條正式命令完成 cross-build；本主機 Python 子程序的 /proc 存取阻擋一鍵流程，沒有將該失敗當作 PASS。一般 Chat 的實際執行尚未獨立驗證。

Source 是程式維護介面；聊天本身沒有 ClipShelf 自動升級權限。Drive／Library 保存則使用該聊天可用的連接器，遵守現行正式 owner 和覆寫規則；沒有連接器时下載交付仍可用，但不得聲稱已同步持久保存。本輪另依 USER 明確追加授權準備 GitHub Actions Windows pipeline，詳 CHAT_BUILD_HANDOFF.md。專屬私有 Repository 尚未建立，不能聲稱遠端能力已可用。

Installer 更新前从系統匣完全退出舊版，保留 `%LOCALAPPDATA%\ClipShelf`。回退用前版正式 Setup；資料若需回復，先備份，再從完整管理的 Import Preview 合併匯入，不直接覆蓋使用者歷史檔案。
