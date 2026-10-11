using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace ClipShelf;

public partial class FolderPickerDialog : Window
{
    private readonly List<(string Label, string Path)> _folders;
    public string SelectedPath { get; private set; } = "";

    public FolderPickerDialog(IEnumerable<string> folders, string initialPath = "")
    {
        InitializeComponent();
        _folders = folders
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .Select(x => (x.Replace("/", "  ›  "), x))
            .ToList();

        FolderBox.ItemsSource = _folders.Select(x => x.Label).ToList();
        var index = _folders.FindIndex(x => string.Equals(x.Path, initialPath, System.StringComparison.OrdinalIgnoreCase));
        FolderBox.SelectedIndex = index >= 0 ? index : (_folders.Count > 0 ? 0 : -1);
    }

    private void Choose_Click(object sender, RoutedEventArgs e)
    {
        if (FolderBox.SelectedIndex < 0 || FolderBox.SelectedIndex >= _folders.Count)
        {
            MessageBox.Show(this, "請先選一個資料夾。", "ClipShelf", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        SelectedPath = _folders[FolderBox.SelectedIndex].Path;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
