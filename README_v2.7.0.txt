ClipShelf v2.7.0 - SAFE DELETE / RECENTLY DELETED
==================================================

BASED ON
--------
ClipShelf v2.6.6 Build Harness 2, which the user has already verified on Windows as BUILD SUCCESS.
TextProbe remains experimental and optional; it must not block the main ClipShelf delivery.

WHAT v2.7.0 CHANGES
-------------------
1. Adds a fixed sidebar section: 最近刪除.
2. True deletion is now soft-delete first. The clip keeps its original folder / favorite / temporary / history placement metadata so Restore can return it safely.
3. Removing a clip from 收藏 / 暫存 / a folder is still only a placement change; it is NOT treated as true deletion.
4. A standalone History item that is deleted now goes to 最近刪除 instead of being destroyed immediately.
5. Normal library views no longer expose irreversible permanent deletion. Use 更多 -> 移到最近刪除 first.
6. In 最近刪除 each item has:
   - 復原
   - 永久刪除
7. Permanent delete requires confirmation and uses an 8-second grace period. During that grace period the item remains visible in 最近刪除 with a pending status; its managed payload is not physically deleted yet, and either Restore or the snackbar can cancel the purge.
8. Pending permanent deletion survives UI shutdown. If the UI closes during the grace period, the pending timestamp is persisted and is finalized the next time the UI starts after the deadline.
9. Settings now include 最近刪除 retention: 7 / 30 / 90 days / never auto-clear. Default is 30 days.
10. Expired trash is checked on UI startup and after changing settings; no permanent background polling was added.
11. Multi-select in 最近刪除 supports Restore and Permanent Delete.
12. Dragging an active clip onto 最近刪除 performs soft-delete.
13. Managed images/files are kept while soft-deleted and are physically removed only at permanent purge.

IMPORTANT DATA SEMANTICS
------------------------
- History removal is not the same as deleting an organized source item.
- Folder/favorite/temporary removal is not true deletion.
- Soft-delete hides the item from every active surface but keeps the underlying record for restore.
- Permanent purge removes the record and, when ManagedByClipShelf is true, its managed image/file payload.

BUILD
-----
1. Fully exit older ClipShelf versions.
2. Run BUILD_EXE.cmd.
3. On BUILD SUCCESS, start ONLY:
     ClipShelf_READY\ClipShelf.exe
4. On BUILD FAILED, send:
     BUILD失敗_把這個傳給ChatGPT.txt

VALIDATION STATUS
-----------------
This source package was structurally/source checked here, but this environment does not have the Windows .NET 8 SDK/WPF toolchain. The user's Windows BUILD SUCCESS + runtime test remains the final validation for v2.7.0.
