using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ClipShelf;

public partial class SettingsDialog : Window
{
    private readonly AppSettings _baseSettings;
    public AppSettings Result { get; private set; }

    public SettingsDialog(AppSettings settings, string hotkeyStatus = "")
    {
        InitializeComponent();
        HotkeyStatus.Text = hotkeyStatus;
        PickerHotkey.SelectedIndex = settings.PickerHotkeyKey == 86 ? 1 : settings.PickerHotkeyKey == 81 ? 2 : 0;
        _baseSettings = settings.Clone();
        Result = settings.Clone();
        FloatingIconCheck.IsChecked = settings.FloatingIconEnabled;
        CaptureTextCheck.IsChecked = settings.CaptureText;
        CaptureImagesCheck.IsChecked = settings.CaptureImages;
        CaptureFilesCheck.IsChecked = settings.CaptureFiles;
        MaxItemsBox.Text = settings.MaxRegularItems.ToString();
        foreach (var raw in TrashRetentionCombo.Items)
        {
            if (raw is ComboBoxItem option && string.Equals(option.Tag?.ToString(), settings.TrashRetentionDays.ToString(), StringComparison.Ordinal))
            {
                TrashRetentionCombo.SelectedItem = option;
                break;
            }
        }
        if (TrashRetentionCombo.SelectedItem == null) TrashRetentionCombo.SelectedIndex = 1;
        StoragePathBox.Text = ResolveStoragePath(settings.StorageDirectory);
        Loaded += (_, _) => MaxItemsBox.SelectAll();
    }

    private static string ResolveStoragePath(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim();
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipShelf", "storage");
    }

    private void MaxItemsBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        => e.Handled = !Regex.IsMatch(e.Text, "^[0-9]+$");

    private void BrowseStorage_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "選擇 ClipShelf 檔案與圖片的存放位置",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = Directory.Exists(StoragePathBox.Text.Trim()) ? StoragePathBox.Text.Trim() : ResolveStoragePath(_baseSettings.StorageDirectory)
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
            StoragePathBox.Text = dialog.SelectedPath;
    }

    private void OpenStorage_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = StoragePathBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(path)) path = ResolveStoragePath(_baseSettings.StorageDirectory);
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"無法打開這個位置。\n\n{ex.Message}", "ClipShelf", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(MaxItemsBox.Text.Trim(), out var max) || max < 0 || max > 1000000)
        {
            MessageBox.Show(this, "保留數量請輸入 0～1,000,000。\n0 代表不限數量。", "ClipShelf", MessageBoxButton.OK, MessageBoxImage.Information);
            MaxItemsBox.Focus();
            MaxItemsBox.SelectAll();
            return;
        }

        var storage = StoragePathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(storage)) storage = ResolveStoragePath("");
        try
        {
            Directory.CreateDirectory(storage);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"這個儲存位置無法使用。\n\n{ex.Message}", "ClipShelf", MessageBoxButton.OK, MessageBoxImage.Warning);
            StoragePathBox.Focus();
            return;
        }

        Result = _baseSettings.Clone();
        Result.PickerHotkeyKey = int.Parse(((ComboBoxItem)PickerHotkey.SelectedItem).Tag.ToString()!);
        Result.PickerHotkeyModifiers = Result.PickerHotkeyKey == 192 ? 2 : 3;
        Result.FloatingIconEnabled = FloatingIconCheck.IsChecked == true;
        Result.CaptureText = CaptureTextCheck.IsChecked == true;
        Result.CaptureImages = CaptureImagesCheck.IsChecked == true;
        Result.CaptureFiles = CaptureFilesCheck.IsChecked == true;
        var trashDays = 30;
        if (TrashRetentionCombo.SelectedItem is ComboBoxItem trashOption &&
            int.TryParse(trashOption.Tag?.ToString(), out var parsedTrashDays))
            trashDays = parsedTrashDays;

        Result.MaxRegularItems = max;
        Result.TrashRetentionDays = trashDays;
        Result.StorageDirectory = storage;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
