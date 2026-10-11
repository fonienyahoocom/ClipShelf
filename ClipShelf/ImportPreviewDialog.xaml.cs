using System;
using System.Windows;

namespace ClipShelf;

public partial class ImportPreviewDialog : Window
{
    private readonly BackupService.ImportPreviewResult _preview;

    public bool IncludeUncertain => IncludeUncertainCheckBox.IsChecked == true;

    public ImportPreviewDialog(BackupService.ImportPreviewResult preview)
    {
        _preview = preview;
        InitializeComponent();

        NewItemsText.Text = preview.NewItemCount.ToString();
        DuplicateIdText.Text = preview.DuplicateIdCount.ToString();
        DuplicateContentText.Text = preview.DuplicateContentCount.ToString();
        UncertainText.Text = preview.UncertainCount.ToString();
        NewFoldersText.Text = preview.NewFolderCount.ToString();

        var sourceVersion = string.IsNullOrWhiteSpace(preview.SourceVersion) ? "未知" : preview.SourceVersion;
        BackupInfoText.Text = $"備份項目總數：{preview.TotalItems}　來源版本：{sourceVersion}　建立時間：{preview.BackupCreatedAtUtc.ToLocalTime():yyyy/MM/dd HH:mm}";

        WarningText.Text = preview.Warnings.Count == 0
            ? "去重以實際內容為主：文字比對內容；圖片比對檔案雜湊；檔案／資料夾比對內容與資料夾結構。"
            : $"掃描時有 {preview.Warnings.Count} 個項目無法完整建立指紋，因此列入「無法安全判定」。預設不匯入這些項目。";

        IncludeUncertainCheckBox.IsEnabled = preview.UncertainCount > 0;
        UpdateImportButton();
    }

    private void IncludeUncertain_Changed(object sender, RoutedEventArgs e) => UpdateImportButton();

    private void UpdateImportButton()
    {
        if (ImportButton == null) return;
        var itemCount = _preview.NewItemCount + (IncludeUncertain ? _preview.UncertainCount : 0);
        var hasAnything = itemCount > 0 || _preview.NewFolderCount > 0;
        ImportButton.IsEnabled = hasAnything;
        ImportButton.Content = itemCount > 0 ? $"匯入 {itemCount} 項" : "匯入資料夾";
    }

    private void Import_Click(object sender, RoutedEventArgs e)
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
