using System.Windows;
using System.Windows.Input;

namespace ClipShelf;

public partial class InputDialog : Window
{
    public string Value => ValueBox.Text.Trim();

    public InputDialog(string title, string prompt, string initial = "")
    {
        InitializeComponent();
        Title = title;
        DialogTitleText.Text = title;
        PromptText.Text = string.IsNullOrWhiteSpace(prompt) ? "輸入名稱" : prompt.TrimEnd('：', ':');
        PrimaryButton.Content = title.Contains("新增") ? "建立" : "儲存";
        ValueBox.Text = initial;
        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void ValueBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (ValidationText != null) ValidationText.Text = "";
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ValueBox.Text))
        {
            ValidationText.Text = "請先輸入資料夾名稱";
            ValueBox.Focus();
            return;
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
