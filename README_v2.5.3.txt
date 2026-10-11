ClipShelf v2.5.3 - STABLE MOUSE SELECTION AUTOSCROLL

This build only refines the v2.5.2 long-text mouse-selection behavior.
Other v2.5.2 UI, history safety, folder collapsing, slow wheel scrolling, Core/UI lifecycle and storage rules are intentionally unchanged.

Fixes
-----
1. Stable selection anchor
   - The character where the mouse-down selection starts is kept fixed.
   - Auto-scroll no longer lets layout jitter flip the selection across that anchor.

2. Preserve the already highlighted range
   - Before and after every outer-list scroll tick, ClipShelf explicitly re-applies the current selection range.
   - This prevents the previously selected text from visually collapsing/disappearing while the page moves.

3. Monotonic edge scrolling
   - While scrolling downward, the selection endpoint cannot jump backward.
   - While scrolling upward, it cannot jump forward.
   - The user can still reduce a downward selection by moving upward, but the endpoint will not randomly cross the original anchor.

4. Less twitchy edge activation
   - Edge zone reduced to 44 px.
   - 120 ms dwell before scrolling begins.
   - Scroll step reduced from the v2.5.2 speed to a gentler maximum of 10 px per 32 ms tick.

5. Selection remains visibly highlighted
   - TextBox inactive-selection highlighting is enabled so a short focus/capture transition caused by WPF scrolling does not make the selected range look lost.

Recommended test
----------------
Expand a long text card, select a paragraph, keep holding the left mouse button and drag to the bottom edge.
The page should scroll slowly, the old highlighted paragraph must remain selected, and the highlight should extend continuously into the newly revealed text.
Then drag upward while still holding the mouse; the selection should shrink/extend upward predictably without disappearing.
