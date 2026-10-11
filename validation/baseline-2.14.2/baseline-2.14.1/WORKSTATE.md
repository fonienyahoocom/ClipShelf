# ClipShelf 2.14.1 patch workstate

Source: Google Drive folder 1VZk-qoa7lbTtmY2pBWDqo4tBYs7qHwTq.
Baseline Source.zip: 1fnHwlOUUGQexWPHcftyk3dtnPnWfJNSy; 238459 bytes; SHA-256 9daf008ea7277eccbb738bca86bfd8c26b74a33519ce0736ad877d50ba8ebddc.
Baseline installer: 1pf1EWOzDAg6A8x5QxawMgL89MWJTff99; 49700456 bytes.
Specification: 11tIMZ3z1xhkJmYLMjbPqx5mgTNvoJD9-lVLiuRzItnI, v0.3 read 2026-10-09.

Authorized delta: shared A/B density switch; inline text drafts/save/cancel; inline title rename. Core/WPF split, safe-delete, backup/import, identity and data directory remain baseline.
Acceptance: actual self-contained win-x64 publish + NSIS Setup; baseline tests and edit persistence/dedup tests; installer payload/upgrade/data-retention checks where executable. Native Windows Chinese IME, real clipboard/paste focus and DPI must be marked unverified if unavailable.
State: source patch completed; Core/WPF self-contained win-x64 publish completed; 36 data/edit checks and 29 offline package checks passed. Setup generated and 930 payload files verified. Native Windows and Wine runtime remain unverified/blocked.
Persistence: deliver new files to specified Google Drive folder only; keep all v2.14.0 files. No Library CURRENT promotion requested.
Next: upload new deliverables to the specified Google Drive folder; rediscover/read back names, sizes and bytes; keep v2.14.0 untouched.
