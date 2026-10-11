ClipShelf v2.13.0 — Interaction Maturity Pass

Based on: v2.12.0 Windows Multi-Select + Safe Rapid Delete

本版不新增大型功能，集中整理日常操作的產品成熟度：

1. 新增文字
- Enter：直接加入 / 儲存。
- Shift+Enter：文字內換行。
- 主要動作「加入 / 儲存」移到左側，取消在右側。
- 視窗內直接提示鍵盤操作。

2. Windows 式多選 contextual toolbar
- Ctrl / Shift 點卡片非文字區仍可直接進入多選。
- 一旦進入選取狀態，普通「新增檔案 / 選取」工具列會暫時換成「已選 N 項 / 全選 / 批次動作 / 完成」。
- 不需要使用者記得先切換模式。

3. Destructive action 視覺
- 每張卡片仍保留收藏 / 複製 / 刪除 / 更多四個常駐動作。
- 垃圾桶平時降為中性色；滑鼠移上才轉為紅色危險狀態。
- v2.12.0 的 rapid-delete guard 與 Safe Delete lifecycle 保持不變。

4. Footer 降噪
- 移除 Core 常駐 / UI 按需、模式等工程狀態的常駐展示。
- 日常只保留快捷提示、自動保存狀態與「本機保存」。

5. Header 視覺層級
- 新增文字成為明確 primary action。
- 暫停 / 備份維持 secondary action。
- 搜尋保留，但縮小並降低視覺權重。

未改動：
- history.json schema / persistence model
- Safe Delete / Recently Deleted / Restore / Permanent Delete
- Backup / Import Preview / content dedup
- Core/UI split / Ctrl+~ 5-minute warm standby
- 左側資料夾結構
- OCR 仍保持移除

Validation state:
- Source/XAML/XML/reference/package static checks: PASS in development environment.
- Windows 10 BUILD SUCCESS and runtime UX: NOT YET VERIFIED.
