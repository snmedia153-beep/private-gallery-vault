using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PrivateGalleryVault.Services;

namespace PrivateGalleryVault.Views;

public partial class BackupRestoreWizardView : UserControl
{
    private readonly VaultContext _context;
    private string? _backupPath;

    public BackupRestoreWizardView(VaultContext context)
    {
        InitializeComponent();
        _context = context;
        VaultPathText.Text = VaultPaths.VaultRoot;
    }

    private void ChooseBackupPath_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "백업 파일 저장 위치",
            FileName = _context.Backups.GetDefaultBackupName(),
            Filter = "Private Gallery Vault Backup|*.pgvbackup|ZIP 파일|*.zip"
        };
        if (dlg.ShowDialog() != true)
            return;
        _backupPath = dlg.FileName;
        BackupPathText.Text = _backupPath;
    }

    private async void StartBackup_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_backupPath))
            ChooseBackupPath_Click(sender, e);
        if (string.IsNullOrWhiteSpace(_backupPath))
            return;

        SetProgress(5, "백업 준비 중...");
        var progress = new Progress<double>(p => SetProgress(p * 100, "vault 데이터를 압축하는 중입니다..."));
        try
        {
            var target = _backupPath!;
            await Task.Run(() => _context.Backups.CreateBackup(target, progress));
            SetProgress(100, "백업 완료");
            _context.ActivityLogs.Add("backup", "백업 완료", Path.GetFileName(target));
        }
        catch (Exception ex)
        {
            SetProgress(0, "백업 실패");
            _context.ActivityLogs.Add("backup", "백업 실패", ex.Message, result: "fail", actor: "System");
            MessageBox.Show(ex.Message, "백업 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void InspectRestore_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "복원 파일 선택",
            Filter = "Private Gallery Vault Backup|*.pgvbackup;*.zip|모든 파일|*.*",
            CheckFileExists = true
        };
        if (dlg.ShowDialog() != true)
            return;
        BackupStatusText.Text = $"복원 파일 검토 완료: {Path.GetFileName(dlg.FileName)}\n실제 복원은 현재 vault를 안전 백업한 뒤 진행하는 단계로 확장됩니다.";
        _context.ActivityLogs.Add("restore", "복원 파일 검토", Path.GetFileName(dlg.FileName));
    }

    private void SetProgress(double value, string status)
    {
        var clamped = Math.Max(0, Math.Min(100, value));
        BackupProgressBar.Value = clamped;
        ProgressPercentText.Text = $"{clamped:0}%";
        BackupStatusText.Text = status;
    }
}
