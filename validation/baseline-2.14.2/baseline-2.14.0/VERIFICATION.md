# ClipShelf 2.14.0 validation — 2026-10-03

## Verified
- .NET SDK 8.0.425 Windows x64 self-contained Core and WPF publish: PASS. Logs included.
- 14 headless tests linked to actual persistence/backup code: PASS. See data-tests.log and Tests/Program.cs.
- NSIS 3.09 amd64 Unicode installer generation: PASS.
- Wine 9 isolated prefix: Core and WPF window start, with no error output. Chinese glyph rendering incomplete; screenshot included. This is a smoke check, not visual acceptance.
- Wine installer: running Core blocks install (exit 2); fresh install matches all 928 payload files by SHA-256; in-place upgrade succeeds and preserves history bytes; uninstall removes exact shipped files and registration while retaining personal history and unrelated files. See installer-tests.log.

## Not verified / known boundaries
- Native Windows 10/11 real-machine runtime, Chinese IME, global hotkeys, external paste focus and multi-monitor/high-DPI interaction remain unverified.
- New UI flows compiled and reviewed, but not exhaustively exercised by GUI automation.
- Installer is unsigned; there is no code-signing certificate in this project.
- Automatic backup includes readable individual file/image payloads and metadata; external directories are not recursively included. Use manual full backup for folder payloads.
- Automatic backups share the local disk and do not protect against loss of that disk.
- MD/TXT indexing capped at 16 MiB per file; no PDF extraction or OCR.
- Windows BUILD_SETUP.ps1 is included for rebuilding; installer was generated with equivalent native Linux makensis, not this PowerShell launcher.

Evidence levels must remain distinct: build PASS / headless data PASS / Wine installer PASS are not USER Windows real-machine PASS.
