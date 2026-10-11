ClipShelf 2.15.0 Windows x64
1. 安裝 .NET 8 SDK。
2. 執行 BUILD_EXE.cmd，成功輸出 ClipShelf_READY（含執行環境）。
3. 安裝 NSIS 3.x。
4. 執行 BUILD_SETUP.cmd，成功輸出 ClipShelf-2.15.0-Setup.exe。
5. 安裝前由系統匣退出舊版；更新不須先解除安裝。

資料測試：dotnet run --project Tests/Tests.csproj -c Release
Linux 交叉建置需額外傳入 -p:EnableWindowsTargeting=true。
安裝檔本身尚未簽章。
