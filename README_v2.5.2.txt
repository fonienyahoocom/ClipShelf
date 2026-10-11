ClipShelf v2.5.2 - LONG TEXT SELECTION + EASY COLLAPSE + SLOW WHEEL

Based on v2.5.1. Only the requested long-text UX is changed.

1) Mouse-only long selection
- While dragging a text selection, moving to the top/bottom edge of History auto-scrolls slowly.
- The selection is extended as the outer list scrolls, so long passages can be selected without wheel/keyboard assistance.
- The timer exists only during an active edge-selection drag; there is no new background polling.

2) Easy collapse
- Expanded text can still use the existing '收起 ↑' button.
- In addition, click the blank area on the right side below the card action buttons to collapse immediately.
- After collapse the source card is brought back into view.

3) Slower wheel for expanded text
- While any card is expanded, wheel scrolling is pixel-based and intentionally reduced to about 42 px per notch.
- Compact-list behavior remains unchanged.
- Recycling virtualization remains enabled.

All v2.5.1 history safety, Core/UI split, warm standby, folder collapse and other behavior are retained.
