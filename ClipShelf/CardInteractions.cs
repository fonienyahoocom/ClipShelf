using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ClipShelf;

public partial class MainWindow
{
    private ClipItem? _inlineEditItem;
    private bool _editWasExpanded;
    private TextBox? _textPressBox;
    private Point _textPressPoint;
    private bool _textPressCanCopy;
    private TextBox? _inlineCompositionBox;
    private bool _deferInlineClipboardRefresh;
    private bool _inlineClipboardRefreshPending;
    private SidebarItem? _inlineEditSection;
    private bool _restoringInlineEditSection;
    private ContextMenu? _activeCardMenu;

    private void TrackInlineComposition()
    {
        TextCompositionManager.AddPreviewTextInputStartHandler(this, (_, _) =>
        {
            if (Keyboard.FocusedElement is TextBox text && CanStartBodyEdit(text)) BeginInlineTextEdit((ClipItem)text.DataContext, text);
            if (Keyboard.FocusedElement is TextBox box && IsInlineEditor(box)) _inlineCompositionBox = box;
        });
        TextCompositionManager.AddPreviewTextInputHandler(this, (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Text) && Keyboard.FocusedElement is TextBox text && CanStartBodyEdit(text) &&
                !BeginInlineTextEdit((ClipItem)text.DataContext, text)) e.Handled = true;
            _inlineCompositionBox = null;
        });
    }

    private void ViewMode_Click(object sender, RoutedEventArgs e)
        => SetCompactView(ReferenceEquals(sender, CompactViewButton));

    private void SyncViewModeControls()
    {
        StandardViewButton.IsChecked = !_settings.CompactView;
        CompactViewButton.IsChecked = _settings.CompactView;
    }

    private void SetCompactView(bool compact)
    {
        if (_settings.CompactView == compact) { SyncViewModeControls(); return; }
        var viewer = FindVisualChild<ScrollViewer>(HistoryList);
        var panel = FindVisualChild<VirtualizingStackPanel>(HistoryList);
        var anchor = panel?.Children.OfType<ListBoxItem>().FirstOrDefault(x =>
            x.TransformToAncestor(HistoryList).Transform(new Point(0, x.ActualHeight)).Y > 0);
        var anchorItem = anchor?.DataContext as ClipItem;
        var top = anchor?.TransformToAncestor(HistoryList).Transform(new Point()).Y ?? 0;
        var offset = viewer?.VerticalOffset ?? 0;

        _settings.CompactView = compact;
        foreach (var item in _visibleItems) item.Compact = compact;
        SyncViewModeControls();
        QueueSave();
        // No ItemsSource replacement: selected IDs, marked items, text selection,
        // expanded cards and drafts remain in the original list and controls.
        HistoryList.UpdateLayout();
        if (viewer != null)
        {
            if (anchorItem != null && HistoryList.ItemContainerGenerator.ContainerFromItem(anchorItem) is ListBoxItem row)
            {
                var newTop = row.TransformToAncestor(HistoryList).Transform(new Point()).Y;
                viewer.ScrollToVerticalOffset(viewer.VerticalOffset + newTop - top);
            }
            else viewer.ScrollToVerticalOffset(offset);
        }
    }

    private bool BeginInlineTextEdit(ClipItem item, TextBox? sourceBox = null)
    {
        if (item.Kind != "Text" || item.IsDeleted || item.IsPendingPurge) return false;
        if (ReferenceEquals(_inlineEditItem, item) && item.IsEditingText) { if (sourceBox == null) FocusInlineEditor(item, false); return true; }
        var start = sourceBox?.SelectionStart ?? 0;
        var length = sourceBox?.SelectionLength ?? 0;
        if (!ResolveInlineEdit()) return false;
        _inlineEditItem = item;
        _inlineEditSection = CategoryList.SelectedItem as SidebarItem;
        _editWasExpanded = item.IsContentExpanded;
        item.DraftText = item.Text;
        item.IsEditingText = true;
        _suspendAutoHide++;
        if (sourceBox != null)
        {
            sourceBox.GetBindingExpression(TextBox.IsReadOnlyProperty)?.UpdateTarget();
            sourceBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            sourceBox.Select(Math.Min(start, sourceBox.Text.Length), Math.Min(length, Math.Max(0, sourceBox.Text.Length - start)));
        }
        else FocusInlineEditor(item, false);
        return true;
    }

    private void BeginInlineTitleEdit(ClipItem item)
    {
        if (item.IsDeleted || item.IsPendingPurge) return;
        if (ReferenceEquals(_inlineEditItem, item) && item.IsEditingTitle) { FocusInlineEditor(item, true); return; }
        if (!ResolveInlineEdit()) return;
        _inlineEditItem = item;
        _inlineEditSection = CategoryList.SelectedItem as SidebarItem;
        _editWasExpanded = item.IsContentExpanded;
        item.DraftTitle = item.Title;
        item.IsEditingTitle = true;
        _suspendAutoHide++;
        FocusInlineEditor(item, true, selectAll: true);
    }

    private void RenameTitle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is ClipItem item) BeginInlineTitleEdit(item);
        e.Handled = true;
    }

    private void EditBody_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is ClipItem item) BeginInlineTextEdit(item);
        e.Handled = true;
    }

    private void SaveInlineEdit_Click(object sender, RoutedEventArgs e) { SaveInlineEdit(); e.Handled = true; }
    private void CancelInlineEdit_Click(object sender, RoutedEventArgs e) { FinishInlineEdit(); e.Handled = true; }

    private bool CanStartBodyEdit(TextBox box)
        => !_multiSelectMode && box.Name == "ClipSelectableText" && box.DataContext is ClipItem item && item.Kind == "Text" &&
           !item.IsEditingText && !item.IsDeleted && !item.IsPendingPurge;

    private bool StartBodyEditForKey(TextBox box, KeyEventArgs e)
    {
        if (!CanStartBodyEdit(box)) return false;
        var modifiers = Keyboard.Modifiers;
        if ((modifiers & (ModifierKeys.Alt | ModifierKeys.Windows)) != 0) return false;
        var editingKey = e.Key == Key.Delete || e.Key == Key.Back || e.Key == Key.F2 ||
                         (modifiers.HasFlag(ModifierKeys.Control) && (e.Key == Key.V || e.Key == Key.X));
        if (!editingKey) return false;
        if (!BeginInlineTextEdit((ClipItem)box.DataContext, box)) { e.Handled = true; return true; }
        if (e.Key == Key.F2) e.Handled = true;
        return true;
    }

    private void ClipRow_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        // The editor's inner ScrollViewer still handles caret/selection scrolling.
        // Suppress only the outer-list request for a text editor whose row is
        // already visible, so focusing it does not align a tall card to its bottom.
        if (sender is not ListBoxItem row || e.TargetObject is not Visual target ||
            FindVisualAncestor<TextBox>(target) == null) return;
        var top = row.TransformToAncestor(HistoryList).Transform(new Point()).Y;
        var viewer = FindVisualChild<ScrollViewer>(HistoryList);
        if (viewer != null && top < viewer.ViewportHeight && top + row.ActualHeight > 0) e.Handled = true;
    }

    private void FocusInlineEditor(ClipItem item, bool title, bool selectAll = false)
    {
        // A context menu is its own focus scope. Wait until its close/focus
        // restoration has finished, and do not scroll an already-realized row.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!ReferenceEquals(_inlineEditItem, item) || !item.IsInlineEditing) return;
            if (_activeCardMenu?.IsOpen == true) return;
            if (HistoryList.ItemContainerGenerator.ContainerFromItem(item) == null) HistoryList.ScrollIntoView(item);
            HistoryList.UpdateLayout();
            if (HistoryList.ItemContainerGenerator.ContainerFromItem(item) is not ListBoxItem row) return;
            var box = FindNamedTextBox(row, title ? "ClipTitleEditor" : "ClipSelectableText");
            if (box == null) return;
            if (!box.IsKeyboardFocusWithin) Keyboard.Focus(box);
            if (title && selectAll) box.SelectAll();
        }), DispatcherPriority.ContextIdle);
    }

    private void TrackCardMenuFocus(ContextMenu menu)
    {
        _activeCardMenu = menu;
        menu.Closed += (_, _) =>
        {
            if (!ReferenceEquals(_activeCardMenu, menu)) return;
            _activeCardMenu = null;
            if (_inlineEditItem is ClipItem item) FocusInlineEditor(item, item.IsEditingTitle);
        };
    }

    private static TextBox? FindNamedTextBox(DependencyObject parent, string name)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is TextBox box && box.Name == name) return box;
            var found = FindNamedTextBox(child, name);
            if (found != null) return found;
        }
        return null;
    }

    private bool SaveInlineEdit()
    {
        var item = _inlineEditItem;
        if (item == null) return true;
        try
        {
            void Persist() { QueueSave(); _store.SaveNowChecked(); }
            if (item.IsEditingText) ClipEditing.SaveText(item, item.DraftText, _allItems, Persist);
            else ClipEditing.SaveTitle(item, item.DraftTitle, Persist);
        }
        catch (Exception ex)
        {
            // Replace the queued failed snapshot with the rolled-back record. Drafts
            // remain available for correction/retry and are never serialized.
            QueueSave();
            MessageBox.Show(this, "尚未儲存，原紀錄已保留。\n" + ex.Message, "ClipShelf", MessageBoxButton.OK, MessageBoxImage.Warning);
            FocusInlineEditor(item, item.IsEditingTitle);
            return false;
        }
        FinishInlineEdit();
        try { _coreBridge.NotifyStateChanged(); } catch { }
        if (!string.IsNullOrWhiteSpace(SearchBox.Text)) RefreshVisibleItems();
        CaptureStateText.Text = "已儲存";
        return true;
    }

    private void FinishInlineEdit()
    {
        var item = _inlineEditItem;
        if (item == null) return;
        item.IsEditingText = false;
        item.IsEditingTitle = false;
        item.DraftText = "";
        item.DraftTitle = "";
        item.IsContentExpanded = _editWasExpanded;
        _inlineEditItem = null;
        _inlineEditSection = null;
        _inlineCompositionBox = null;
        _suspendAutoHide = Math.Max(0, _suspendAutoHide - 1);
        if (_inlineClipboardRefreshPending)
        {
            _inlineClipboardRefreshPending = false;
            RefreshVisibleItems();
        }
    }

    private bool ResolveInlineEdit()
    {
        if (_inlineEditItem is not ClipItem item) return true;
        var changed = item.IsEditingText ? item.DraftText != item.Text : item.DraftTitle != item.Title;
        if (!changed) { FinishInlineEdit(); return true; }
        var result = MessageBox.Show(this, "目前有尚未儲存的修改。要先儲存嗎？\n「否」會放棄草稿；「取消」會繼續編輯。", "ClipShelf", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (result == MessageBoxResult.Cancel) return false;
        if (result == MessageBoxResult.Yes) return SaveInlineEdit();
        FinishInlineEdit();
        return true;
    }

    private static bool IsInlineEditor(TextBox box)
        => box.DataContext is ClipItem item && ((box.Name == "ClipSelectableText" && item.IsEditingText) ||
                                                (box.Name == "ClipTitleEditor" && item.IsEditingTitle));

    private void InlineEditor_KeyDown(TextBox box, KeyEventArgs e)
    {
        // IME-generated keys belong to WPF's native composition engine. In
        // particular, Enter accepting a Chinese candidate must not save or paste.
        if (e.Key == Key.ImeProcessed || ReferenceEquals(box, _inlineCompositionBox)) return;
        if (e.Key == Key.Escape) { FinishInlineEdit(); HistoryList.Focus(); e.Handled = true; }
        else if (e.Key == Key.Enter && (box.Name == "ClipTitleEditor" || Keyboard.Modifiers.HasFlag(ModifierKeys.Control)))
        {
            e.Handled = true;
            SaveInlineEdit();
        }
    }

    private void ClipSelectableText_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_textPressCanCopy || !ReferenceEquals(sender, _textPressBox)) return;
        var delta = e.GetPosition(this) - _textPressPoint;
        if (Math.Abs(delta.X) >= SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(delta.Y) >= SystemParameters.MinimumVerticalDragDistance) _textPressCanCopy = false;
    }

    private void ClipSelectableText_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var copy = _textPressCanCopy && ReferenceEquals(sender, _textPressBox);
        _textPressCanCopy = false;
        if (!copy || sender is not TextBox box || box.DataContext is not ClipItem item ||
            item.IsInlineEditing || item.IsDeleted || item.IsPendingPurge || box.SelectionLength != 0) return;
        // A plain single click reuses the original copy operation. Native drag
        // selection, modifier selection and editable drafts never enter this path.
        CopyItem(item);
    }

    private void ShowInlineTextMenu(TextBox box)
    {
        var menu = new ContextMenu { PlacementTarget = box };
        void Command(string label, System.Windows.Input.ICommand command)
            => menu.Items.Add(new MenuItem { Header = label, Command = command, CommandTarget = box });
        Command("復原", ApplicationCommands.Undo);
        Command("重做", ApplicationCommands.Redo);
        menu.Items.Add(new Separator());
        Command("剪下", ApplicationCommands.Cut);
        Command("複製", ApplicationCommands.Copy);
        Command("貼上", ApplicationCommands.Paste);
        Command("刪除", System.Windows.Documents.EditingCommands.Delete);
        Command("全選", ApplicationCommands.SelectAll);
        menu.Items.Add(new Separator());
        var save = new MenuItem { Header = "儲存修改", InputGestureText = box.Name == "ClipTitleEditor" ? "Enter" : "Ctrl+Enter" };
        save.Click += (_, _) => SaveInlineEdit();
        var cancel = new MenuItem { Header = "取消修改", InputGestureText = "Esc" };
        cancel.Click += (_, _) => FinishInlineEdit();
        menu.Items.Add(save); menu.Items.Add(cancel);
        _suspendAutoHide++;
        menu.Closed += (_, _) => _suspendAutoHide = Math.Max(0, _suspendAutoHide - 1);
        box.ContextMenu = menu;
        TrackCardMenuFocus(menu);
        menu.IsOpen = true;
    }

    private void InlineTitle_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is TextBox box) { ShowInlineTextMenu(box); e.Handled = true; }
    }
}
