using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PrivateGalleryVault.Services;

namespace PrivateGalleryVault.Views;

public partial class BackupRestoreWizardView : UserControl
{
    private readonly VaultContext _context;
    private string? _backupPath;
    private string? _restorePath;

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

    private async void InspectRestore_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "복원 파일 선택",
            Filter = "Private Gallery Vault Backup|*.pgvbackup;*.zip|모든 파일|*.*",
            CheckFileExists = true
        };
        if (dlg.ShowDialog() != true)
            return;

        _restorePath = dlg.FileName;

        BackupPackageInfo info;
        try
        {
            info = _context.Backups.InspectBackupPackage(_restorePath);
        }
        catch (Exception ex)
        {
            SetProgress(0, "복원 파일 검토 실패");
            MessageBox.Show("복원 파일을 검토할 수 없습니다.\n\n" + ex.Message, "복원 파일 검토", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!info.IsValid)
        {
            SetProgress(0, "복원 파일이 올바르지 않습니다.");
            MessageBox.Show("올바른 백업 파일이 아닙니다. master.json 또는 catalog.db가 없습니다.", "복원 파일 검토", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var folderSchemaDescription = DescribeFolderSchema(info);
        SetProgress(10, $"복원 파일 검토 완료: {info.FileName}\n항목 {info.EntryCount}개 · {FormatBytes(info.SizeBytes)}\n{folderSchemaDescription}");
        _context.ActivityLogs.Add("restore", "복원 파일 검토", $"{Path.GetFileName(_restorePath)} / {folderSchemaDescription}");

        var confirm = MessageBox.Show(
            "선택한 백업으로 현재 Vault를 복원합니다.\n\n" +
            "복원 전 현재 Vault를 안전 백업한 뒤, 앱을 다시 시작합니다.\n\n" +
            $"파일: {info.FileName}\n" +
            folderSchemaDescription + "\n\n" +
            "구버전 백업이어도 복원 후 새 앱에서 폴더 구조를 자동 마이그레이션합니다.\n\n" +
            "계속할까요?",
            "Vault 복원",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
        {
            BackupStatusText.Text += "\n복원이 취소되었습니다.";
            return;
        }

        await RestoreSelectedBackupAsync(_restorePath);
    }

    private async Task RestoreSelectedBackupAsync(string restorePath)
    {
        string? safetyBackupPath = null;
        string? restoreStagePath = null;

        try
        {
            SetProgress(15, "복원 전 현재 Vault 안전 백업 생성 중...");
            var safetyDir = Path.Combine(AppContext.BaseDirectory, "restore-safety-backups");
            Directory.CreateDirectory(safetyDir);
            safetyBackupPath = Path.Combine(safetyDir, _context.Backups.GetDefaultPreRestoreBackupName());

            var backupProgress = new Progress<double>(p => SetProgress(15 + p * 30, "복원 전 현재 Vault 안전 백업 생성 중..."));
            await Task.Run(() => _context.Backups.CreateBackup(safetyBackupPath, backupProgress));
            _context.ActivityLogs.Add("restore", "복원 전 안전 백업 완료", Path.GetFileName(safetyBackupPath));

            SetProgress(48, "복원 백업을 임시 폴더에 준비 중...");
            var restoreService = new BackupRestoreService();
            var prepareProgress = new Progress<double>(p => SetProgress(48 + p * 22, "복원 백업을 임시 폴더에 준비 중..."));
            restoreStagePath = await Task.Run(() => restoreService.PrepareRestoreStage(restorePath, prepareProgress));

            SetProgress(75, "복원 도우미를 준비하고 데이터베이스 연결을 종료합니다...");
            var exePath = ResolveExecutablePath();
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                throw new FileNotFoundException("앱 실행 파일 경로를 찾을 수 없어 복원 후 재시작할 수 없습니다.", exePath);

            var helperScript = CreateRestoreHelperScript(restoreStagePath, VaultPaths.VaultRoot, exePath, safetyBackupPath);

            SetProgress(90, "복원 도우미 실행 중... 앱 종료 후 Vault를 교체합니다.");
            StartRestoreHelper(helperScript);
            _context.Dispose();

            MessageBox.Show(
                string.Join(Environment.NewLine,
                    "복원 준비가 완료되었습니다.",
                    string.Empty,
                    "앱을 종료한 뒤 별도 복원 도우미가 Vault 폴더를 안전하게 교체하고 앱을 다시 시작합니다.",
                    string.Empty,
                    "복원 전 안전 백업:",
                    safetyBackupPath ?? string.Empty),
                "Vault 복원",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            SetProgress(0, "복원 실패");
            if (!string.IsNullOrWhiteSpace(restoreStagePath))
                TryDeleteDirectory(restoreStagePath);

            MessageBox.Show(
                "복원 중 오류가 발생했습니다." + Environment.NewLine + Environment.NewLine + ex.Message +
                (string.IsNullOrWhiteSpace(safetyBackupPath)
                    ? string.Empty
                    : Environment.NewLine + Environment.NewLine + "복원 전 안전 백업:" + Environment.NewLine + safetyBackupPath),
                "Vault 복원 실패",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static string? ResolveExecutablePath()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            exePath = Process.GetCurrentProcess().MainModule?.FileName;
        return exePath;
    }

    private static string CreateRestoreHelperScript(string stageRoot, string vaultRoot, string exePath, string? safetyBackupPath)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"pgv_restore_helper_{Guid.NewGuid():N}.ps1");
        var parent = Path.GetDirectoryName(vaultRoot) ?? AppContext.BaseDirectory;
        var rollbackRoot = Path.Combine(parent, $"vault_restore_rollback_{DateTime.Now:yyyyMMdd_HHmmss}");

        var script = $$"""
$ErrorActionPreference = 'Stop'
$pidToWait = {{Environment.ProcessId}}
$stageRoot = '{{EscapePowerShellSingleQuoted(stageRoot)}}'
$vaultRoot = '{{EscapePowerShellSingleQuoted(vaultRoot)}}'
$rollbackRoot = '{{EscapePowerShellSingleQuoted(rollbackRoot)}}'
$exePath = '{{EscapePowerShellSingleQuoted(exePath)}}'
$safetyBackupPath = '{{EscapePowerShellSingleQuoted(safetyBackupPath ?? string.Empty)}}'
$logPath = Join-Path ([System.IO.Path]::GetTempPath()) 'pgv_restore_helper.log'

function Write-Log([string]$message) {
    $line = ('{0:yyyy-MM-dd HH:mm:ss} {1}' -f (Get-Date), $message)
    Add-Content -LiteralPath $logPath -Value $line -Encoding UTF8
}

function Invoke-WithRetry([scriptblock]$action, [string]$description) {
    $last = $null
    for ($i = 0; $i -lt 40; $i++) {
        try {
            & $action
            Write-Log ($description + ' OK')
            return
        } catch {
            $last = $_
            Write-Log ($description + ' retry ' + ($i + 1) + ': ' + $_.Exception.Message)
            Start-Sleep -Milliseconds (350 + ($i * 80))
        }
    }
    throw $last
}

try {
    Write-Log 'Restore helper started.'
    try { Wait-Process -Id $pidToWait -ErrorAction SilentlyContinue } catch {}
    Start-Sleep -Milliseconds 1200

    if (!(Test-Path -LiteralPath $stageRoot)) {
        throw 'Restore stage folder not found: ' + $stageRoot
    }

    if (Test-Path -LiteralPath $vaultRoot) {
        if (Test-Path -LiteralPath $rollbackRoot) {
            Invoke-WithRetry { Remove-Item -LiteralPath $rollbackRoot -Recurse -Force -ErrorAction Stop } 'remove old rollback'
        }
        Invoke-WithRetry { Move-Item -LiteralPath $vaultRoot -Destination $rollbackRoot -Force -ErrorAction Stop } 'move current vault to rollback'
    }

    Invoke-WithRetry { Copy-Item -LiteralPath $stageRoot -Destination $vaultRoot -Recurse -Force -ErrorAction Stop } 'copy restored vault into place'
    if (!(Test-Path -LiteralPath (Join-Path $vaultRoot 'master.json'))) { throw 'Restored vault is missing master.json.' }
    if (!(Test-Path -LiteralPath (Join-Path $vaultRoot 'catalog.db'))) { throw 'Restored vault is missing catalog.db.' }
    Invoke-WithRetry { Remove-Item -LiteralPath $stageRoot -Recurse -Force -ErrorAction Stop } 'remove restore stage'

    Write-Log ('Restore completed. Safety backup: ' + $safetyBackupPath)
    Start-Process -FilePath $exePath
} catch {
    Write-Log ('Restore failed: ' + $_.Exception.ToString())
    try {
        if (Test-Path -LiteralPath $rollbackRoot) {
            if (Test-Path -LiteralPath $vaultRoot) {
                Remove-Item -LiteralPath $vaultRoot -Recurse -Force -ErrorAction SilentlyContinue
            }
            Move-Item -LiteralPath $rollbackRoot -Destination $vaultRoot -Force -ErrorAction SilentlyContinue
            Write-Log 'Rollback restored after helper failure.'
        }
    } catch {
        Write-Log ('Rollback restore failed: ' + $_.Exception.ToString())
    }
    try {
        Add-Type -AssemblyName PresentationFramework
        $message = 'Vault 복원 도우미에서 오류가 발생했습니다.' + [Environment]::NewLine + [Environment]::NewLine + $_.Exception.Message + [Environment]::NewLine + [Environment]::NewLine + '로그: ' + $logPath
        [System.Windows.MessageBox]::Show($message, 'Vault 복원 실패', 'OK', 'Error') | Out-Null
    } catch {}
    try { Start-Process -FilePath $exePath } catch {}
}
""";

        File.WriteAllText(scriptPath, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return scriptPath;
    }

    private static string EscapePowerShellSingleQuoted(string value)
    {
        return value.Replace("'", "''");
    }

    private static void StartRestoreHelper(string scriptPath)
    {
        var powershellPath = Environment.GetEnvironmentVariable("SystemRoot") is { Length: > 0 } systemRoot
            ? Path.Combine(systemRoot, "System32", "WindowsPowerShell", "v1.0", "powershell.exe")
            : "powershell.exe";

        Process.Start(new ProcessStartInfo
        {
            FileName = powershellPath,
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch
        {
        }
    }

    private static void RestartApplication()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            exePath = Process.GetCurrentProcess().MainModule?.FileName;

        if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
            Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });

        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Application.Current.Shutdown();
    }

    private void SetProgress(double value, string status)
    {
        var clamped = Math.Max(0, Math.Min(100, value));
        BackupProgressBar.Value = clamped;
        ProgressPercentText.Text = $"{clamped:0}%";
        BackupStatusText.Text = status;
    }

    private static string DescribeFolderSchema(BackupPackageInfo info)
    {
        if (!string.IsNullOrWhiteSpace(info.DatabaseSchemaNote))
            return "폴더 구조: " + info.DatabaseSchemaNote;

        return info.HasVirtualFolderSchema
            ? $"폴더 구조: 가상 폴더 포함 · 폴더 {info.FolderCount}개 · 폴더 지정 파일 {info.FolderedMediaCount}개"
            : "폴더 구조: 구버전 백업 · 복원 후 자동 마이그레이션";
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }
}
