ClipShelf v2.5.4 - NATIVE FULL-TEXT SELECTION

This release replaces the custom outer-list selection autoscroll used in v2.5.2/v2.5.3.
That approach was inherently unstable because WPF's native TextBox drag-selection and a second programmatic selection loop were both changing the selection while the outer ListBox moved.

v2.5.4 design:
- First click on a compact text card expands it; that click no longer starts a selection while layout is changing.
- Expanded text has its own bounded TextBox viewport (480 px max) with a native vertical scrollbar.
- Subsequent drag-selection is 100% WPF TextBox native selection/autoscroll. No custom selection timer, no programmatic Select() during dragging.
- Mouse wheel inside expanded text scrolls that text viewport slowly (30 px/notch).
- Wheel outside the text body keeps the gentle outer-list scroll.
- Ctrl+C / context-menu selected-text copy still creates a History entry exactly as v2.5.x requires.
- Right blank area / collapse button still collapses the card.

Unchanged: Core/UI architecture, warm standby, History safety model, folder hierarchy/collapse, normal/non-interference modes.
