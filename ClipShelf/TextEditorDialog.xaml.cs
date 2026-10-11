using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace ClipShelf;

public partial class TextEditorDialog : Window
{
    public string ItemTitle => TitleBox.Text.Trim();
    public string ItemNote => NoteBox.Text.Trim();
    public string Value => TextBox.Text;
    public string SelectedCategory => CategoryBox.SelectedItem?.ToString() == "不分類" ? "" : CategoryBox.SelectedItem?.ToString() ?? "";
    public bool Favorite => FavoriteCheck.IsChecked == true;
    public bool Temporary => TemporaryCheck.IsChecked == true;

    public TextEditorDialog(
        IEnumerable<string> categories,
        string initialCategory = "",
        string initialText = "",
        bool favorite = false,
        bool temporary = false,
        bool editMode = false, string title = "", string note = "")
    {
        InitializeComponent();
        TitleBox.Text = title; NoteBox.Text = note;
        var list = new List<string> { "不分類", "AI 用語", "快速用語" };
        list.AddRange(categories.Where(x => !string.IsNullOrWhiteSpace(x)));
        foreach (var c in list.Distinct()) CategoryBox.Items.Add(c);
        CategoryBox.SelectedItem = string.IsNullOrWhiteSpace(initialCategory) ? "不分類" : initialCategory;
        if (CategoryBox.SelectedIndex < 0) CategoryBox.SelectedIndex = 0;

        TextBox.Text = initialText ?? "";
        FavoriteCheck.IsChecked = favorite;
        TemporaryCheck.IsChecked = temporary;

        if (editMode)
        {
            Title = "編輯文字";
            DialogHeading.Text = "編輯文字";
            DialogSubheading.Text = "Enter 儲存 · Shift+Enter 換行。";
            SaveButton.Content = "儲存";
        }

        Loaded += (_, _) =>
        {
            TextBox.Focus();
            TextBox.CaretIndex = TextBox.Text.Length;
        };
    }

    private void Add_Click(object sender, RoutedEventArgs e)
        => CommitText();

    private void TextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        // Product default: Enter commits immediately. Shift+Enter remains the explicit
        // multiline gesture so fast capture does not require leaving the keyboard.
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;

        e.Handled = true;
        CommitText();
    }

    private bool CommitText()
    {
        if (string.IsNullOrWhiteSpace(TextBox.Text))
        {
            MessageBox.Show(this, "請先輸入文字。", "ClipShelf", MessageBoxButton.OK, MessageBoxImage.Information);
            TextBox.Focus();
            return false;
        }

        DialogResult = true;
        Close();
        return true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
