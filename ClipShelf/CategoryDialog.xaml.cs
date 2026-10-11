using System.Collections.Generic;
using System.Windows;

namespace ClipShelf;

public partial class CategoryDialog : Window
{
    public string SelectedCategory => ClearBox.IsChecked == true ? "" : (CategoryBox.SelectedItem?.ToString() ?? "");

    public CategoryDialog(IEnumerable<string> categories, string current)
    {
        InitializeComponent();
        foreach (var c in categories) CategoryBox.Items.Add(c);
        CategoryBox.SelectedItem = current;
        if (CategoryBox.SelectedIndex < 0 && CategoryBox.Items.Count > 0) CategoryBox.SelectedIndex = 0;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
