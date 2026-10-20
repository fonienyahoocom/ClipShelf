# ClipShelf 2.15.9｜原生 WPF 版面座標驗證

驗證狀態：2.15.9 的雙欄基本排列已在 Windows GitHub Actions 原生 STA／Dispatcher 視窗測試中通過；使用者本機、DPI 125% 和完整人工操作仍待驗收。

## 來源與重現

- 工程：`fonienyahoocom/ClipShelf`，修復分支 `fix/grid-two-column-trigger-2.15.9`，PR #8。
- 修補線索：2026-10-21 使用者影片顯示方塊按鈕與頁碼已變更，但卡片保持直排。
- 前一版的 WPF ListBox.Tag 傳 bool，XAML DataTrigger 以字串 True 比對。2.15.9 改用雙方同為 Grid/List 字串。這是原始碼可驗證的型別契約修正；是否為原先單欄故障唯一根因，仍須對照舊版同環境幾何實測。
- 原生測試方式：Windows Server 2022 runner、STA 執行緒、真正 MainWindow XAML、Window.Show()、Dispatcher.Measure/Arrange、實際 RadioButton 路由 Click、ItemContainerGenerator/TransformToAncestor 讀取卡片位置。使用三張合成短句，沒有取用或記錄使用者正文。

## 已取得的實際 WPF 幾何數值

來源：GitHub Actions 工作 `39511193145`，Commit `4461bbf3985c76fdc36b62db70480ef583ee3d0f`。以下為視窗寬 1120 DIP、DPI 比例 1.0 時：

| 模式 | ItemsPanel | 清單寬度 | 卡片 1 (X,Y,寬) | 卡片 2 (X,Y,寬) |
|---|---|---:|---|---|
| 直排 | VirtualizingStackPanel | 833 | (1, 1, 831) | (1, 124.25, 831) |
| 方塊 | WrapPanel | 833 | (1, 1, 391.5) | (400.5, 1, 391.5) |
| 窄窗方塊 | WrapPanel | 429 | (1, 1, 387) | (1, 125, 387) |

方塊兩張卡片同 Y=1，第二張位於第一張右側，右邊界 792 小於 viewport 831。有效 Width 來源為 `StyleTrigger/expression=True/binding=Active`。驗證 20 次直排／方塊往返、由窄回寬、多筆 24 筆分頁上限；該輪 137 項原生視窗斷言通過。

## 額外驗收與邊界

後續新增原生 WPF 编辑卡片全寬、文字局部刪除、Undo、A/B 密度、分頁下一頁測試。它們的測試結果以後續 CI 實際 log 為準，不能預先寫成通過。

尚未驗證使用者本機的中文輸入法、125% DPI、其他應用程式輸入焦點、持續多分鐘使用與既有 2.15.8 錯誤版本同場幾何對照。請在完成以前保持 PR 待驗收，不要只因 CI 綠燈就宣稱本機故障正式結案。
