# ClipShelf 2.15.11 | Selected card TextBox collapsed by Auto toolbar

User video: ClipShelf 2.15.10 grid exterior correctly two-column; selecting a card reveals edit/copy/delete buttons and causes its text to wrap almost vertically. Returning to list cures it.

Old production XAML, unchanged, with only failing Windows native WPF geometry assertion added (CI run 39520704459; commit df759cfedc3b43d83b69ed354054ccb6f043aed9): ListBoxItem=247 DIP, root Grid=221 DIP; toolbar Auto column=184 DIP, star body column=37 DIP, `ClipSelectableText` TextBox=33 DIP. This directly reproduces the issue without private texts or destructive operations.

Fix: Separate toolbar into its own row (Grid.Row=0, spanning body and 18-DIP right gutter). Title row1, inline edit controls row2, selectable body TextBox row3, full-text controls row4, search/image row5. No changes to data model, clipboard, paste, storage, undo or 24-item grid pages.

Acceptance: same native WPF probe must now show selected TextBox width >=55% of root grid; repeat with second selected card; preserve 20 layout switches, edit expansion, selection Delete/Undo, A/B density, paging. User's own DPI, IME and long-run app interaction remain unverified until local confirmation.
