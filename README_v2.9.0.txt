ClipShelf v2.9.0 — 本機框選 OCR

使用：
1. 正常啟動 ClipShelf_READY\ClipShelf.exe。
2. 在要複製的畫面按 Ctrl+Alt+O；也可用 ClipShelf「框選 OCR」按鈕，或浮動圖示／通知區右鍵選單。
3. 滑鼠拖曳框選，放開即辨識；Esc 或右鍵取消。首次出現框選前需等待 PowerShell 啟動及載入。
4. 在預覽視窗檢查、修改文字，按「加入歷史」。取消不新增。
5. 主視窗可選「繁中／混合」或「英文」；預設繁中，重開管理介面回到預設。快捷鍵被別的軟體占用時仍可用按鈕／選單。WG 可設定送出 Ctrl+Alt+O。

這次是完整 ClipShelf source 更新包，不是獨立測試包。OCR 已接到既有加入歷史流程。
核心不載入 OCR；明確觸發才建立獨立 PowerShell helper，辨識完成後先退出，再顯示預覽。
沒有雲端、API key、自動下載或背景掃描。框選採本次螢幕快照，僅所選範圍交給 OCR；全桌面快照只在 helper 記憶體，裁切暫存圖在本次結束清理。
正常取消／成功／失敗會退出；主 UI 提供 120 秒逾時回收；helper 另監測父程序與 150 秒期限。早期動態編譯階段仍依賴父程序。异常退出清理与多螢幕混合 DPI 尚待實機驗證。
使用你這台 Windows 10 已實測成功的 Windows.Media.Ocr + Windows PowerShell 5.1 路線，不需新增 SDK、模型或付費服務。正式跨機部署支援仍有限制，本版不宣稱所有 Windows 電腦都可用。

辨識可能出錯，尤其程式碼和符號；預覽不會自動修改錯字，只去掉繁中漢字彼此間 OCR 多加的空格，保留換行。UIA 指向擷取仍維持移除。

更新：完整離開舊版 → 解壓本完整包 → 執行 BUILD_EXE.cmd → BUILD SUCCESS → 開啟 ClipShelf_READY\ClipShelf.exe。
%LOCALAPPDATA%\ClipShelf 與自訂 storage 照舊；歷史／設定／資料格式不遷移。不要刪使用者資料；若附件自訂在舊程式資料夾，先保留附件。

本環境已做靜態檢查，不能執行 Windows build 或框選 UI。OCR 引擎部分承接兩次實機成功證據；新整合、快捷鍵、框選與預覽仍需本版首次實機確認。若 build 失敗請傳 BUILD失敗_把這個傳給ChatGPT.txt。
舊 README/TEST/SOURCE_CHECK 為歷史文件；以本 README 與 DEVELOPMENT_HISTORY 最上方為準。
