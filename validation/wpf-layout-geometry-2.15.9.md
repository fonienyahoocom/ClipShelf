# ClipShelf 方塊單欄故障｜Windows WPF 實測與因果反證

**狀態：使用者故障尚未找到根因，PR #8 維持候選／不合併。**

## 1. 已有的使用者實機現象

使用者 2026-10-21 影片：在「快速用語 → 語音回話」選取「▦ 方塊」，按鈕變成已選取、出現 `1/1 頁`，但卡片仍然垂直堆疊、占滿整列。另一個「歷史」畫面有 503 筆資料，仍須區分舊版曾發生的按鈕無回應／當機與單欄顯示問題。

## 2. 對照方法

Windows Server 2022 GitHub Actions runner，執行**真正的 ClipShelf.MainWindow XAML、WPF ItemsPanel、ItemContainerStyle、TextBox**，而非只檢查模型或 XAML 字串。使用 STA、Dispatcher、Window.Show、原生 RadioButton Routed Click，量測 ItemContainerGenerator 生成之容器位置與 Width Binding 的有效來源。

- 資料：三張合成短句、另有 63 筆分頁測試，不使用、輸出或修改使用者句庫正文。
- 兩個版本使用同一份 `Tests.WpfLayout` 測試、同一 runner 與相同起始版面；由於測試程序可能保存上一個版面偏好，必須先**強制重置為直排**。
- 對照執行：<https://github.com/fonienyahoocom/ClipShelf/actions/runs/39512934389>。
- 2.15.9 完整 162 項原生 WPF 斷言與發行檢查成功執行：<https://github.com/fonienyahoocom/ClipShelf/actions/runs/39511863416>。

## 3. A／B 實測結果：2.15.8 與 2.15.9 完全相同

兩版在 100% DPI、1120 DIP 視窗、833 DIP 清單內均得到：

| 狀態 | 實際 ItemsPanel | 卡片 1 (X,Y,寬) | 卡片 2 (X,Y,寬) |
|---|---|---|---|
| 直排 | VirtualizingStackPanel | (1, 1, 831) | (1, 124.25, 831) |
| 方塊 | WrapPanel | (1, 1, 391.5) | (400.5, 1, 391.5) |
| 窄窗方塊（清單寬 429） | WrapPanel | (1, 1, 387) | (1, 125, 387) |

兩版 `three-short-cards-wide` 的實際 Width 來源都為 `StyleTrigger/expression=True/binding=Active`；第二張卡片皆在第一張右方、不重疊、未超出 viewport。兩版 **均通過 162 項實際 WPF 視窗斷言**，包含 20 次切換、長文展開、編輯卡片全寬、選取文字局部刪除、Undo、A/B 密度與 63 筆分頁。

因此：**先前判斷的「布林值／字串比對不一致導致雙欄失敗」不成立為已證實根因。** WPF 可能已替觸發條件做值轉換，或在這台 runner 上原本就能正確套用。2.15.9 的 Grid/List 字串改寫不具備解釋使用者影片故障的因果證據，不應宣稱已正式修復。

## 4. 下一個能區分假說的檢查

請在使用者**已安裝的實際 WPF UI 程序**取得低敏感的版面診斷（不記錄正文）：
- 真正執行檔檔案版本、路徑、檔案 SHA-256、執行程序 PID 與載入 WPF runtime 版本。
- 切換前後的 `GridLayout`、`HistoryList.Tag` 值與實際型別、RadioButton 狀態。
- `ItemsPanel` 型別、ListBox / ScrollViewer viewport / Panel 實際寬度。
- 第一至第三張外層 `ListBoxItem` 的 Effective `Width`、ActualWidth、Margin、MinWidth、`DependencyPropertyHelper.GetValueSource`、`BindingExpression.Status` 和相對清單 X/Y 座標。
- DPI 比例，是否啟動編輯，是否有卡片處於 IsInlineEditing / IsContentExpanded。
- 接續觀測 LayoutUpdated／SizeChanged 是否短時間重複；若卡死先取 UI thread stack，而非靠延時掩蓋。

此診斷需要透過使用者授權的診斷候選版或安全的 Windows 原生偵錯方式取得。CI 的原生 WPF 測試雖有真實幾何，但不能代替使用者主機。

**仍未測：** 使用者本機版本／安裝路徑、125% DPI、中文 IME、真實剪貼資料與長時間操作、卡頓時 UI thread stack。任何未測項都不得宣稱通過。
