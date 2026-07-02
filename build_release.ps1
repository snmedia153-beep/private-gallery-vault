# Private Gallery Vault release build script
# Windows PowerShell 5.1 compatible. Keep this file ASCII-safe to avoid encoding parser issues.

$ErrorActionPreference = "Stop"

try {
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
} catch { }

Set-Location $PSScriptRoot

$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = "1"
$env:MSBuildEnableWorkloadResolver = "false"

$ScriptRoot = $PSScriptRoot
$ProjectPath = Join-Path $ScriptRoot "src\PrivateGalleryVault\PrivateGalleryVault.csproj"
$PublishPath = Join-Path $ScriptRoot "publish\win-x64"
$PublishRoot = Join-Path $ScriptRoot "publish"
$ReleaseZipPath = Join-Path $PublishRoot "PrivateGalleryVault-win-x64.zip"
$PackageStage = Join-Path $ScriptRoot ".release-package-stage"
$PreserveRoot = Join-Path $ScriptRoot (".build-preserve\" + (Get-Date -Format "yyyyMMdd-HHmmss-fff"))
$PreserveNames = @("vault", "restore-safety-backups")
$restoredPreservedData = $false

function Write-Section([string]$Text) {
    Write-Host ""
    Write-Host $Text -ForegroundColor Cyan
}

function Test-IsUnderPath([string]$ChildPath, [string]$ParentPath) {
    try {
        $childFull = [System.IO.Path]::GetFullPath($ChildPath).TrimEnd('\') + '\'
        $parentFull = [System.IO.Path]::GetFullPath($ParentPath).TrimEnd('\') + '\'
        return $childFull.StartsWith($parentFull, [System.StringComparison]::OrdinalIgnoreCase)
    } catch {
        return $false
    }
}

function Stop-PrivateGalleryVaultProcesses {
    param([string]$TargetPublishPath)

    $processes = @(Get-Process -Name "PrivateGalleryVault" -ErrorAction SilentlyContinue)
    foreach ($proc in $processes) {
        $shouldStop = $true
        try {
            $exePath = $proc.MainModule.FileName
            if ($exePath) {
                $shouldStop = (Test-IsUnderPath -ChildPath $exePath -ParentPath $TargetPublishPath)
            }
        } catch {
            # If MainModule is not accessible, stop it to prevent locked publish files.
            $shouldStop = $true
        }

        if ($shouldStop) {
            try {
                Write-Host ("Stopping running app process PID={0}" -f $proc.Id) -ForegroundColor Yellow
                $proc.CloseMainWindow() | Out-Null
                if (-not $proc.WaitForExit(2000)) {
                    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
                    Start-Sleep -Milliseconds 300
                }
            } catch {
                Write-Host ("Process stop warning PID={0}: {1}" -f $proc.Id, $_.Exception.Message) -ForegroundColor Yellow
            }
        }
    }
}

function Clear-FileAttributesForDelete {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) { return }

    try {
        Get-ChildItem -LiteralPath $Path -Recurse -Force -ErrorAction SilentlyContinue | ForEach-Object {
            try { $_.Attributes = [System.IO.FileAttributes]::Normal } catch { }
        }
        try { (Get-Item -LiteralPath $Path -Force).Attributes = [System.IO.FileAttributes]::Directory } catch { }
    } catch { }
}

function Remove-DirectoryWithRetry {
    param(
        [Parameter(Mandatory=$true)][string]$Path,
        [int]$MaxRetry = 6
    )

    if (-not (Test-Path -LiteralPath $Path)) { return }

    Stop-PrivateGalleryVaultProcesses -TargetPublishPath $PublishPath
    Clear-FileAttributesForDelete -Path $Path

    $lastError = $null
    for ($attempt = 1; $attempt -le $MaxRetry; $attempt++) {
        try {
            Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
            return
        } catch {
            $lastError = $_
            Write-Host ("Remove retry {0}/{1}: {2} :: {3}" -f $attempt, $MaxRetry, $Path, $_.Exception.Message) -ForegroundColor Yellow
            Stop-PrivateGalleryVaultProcesses -TargetPublishPath $PublishPath
            Start-Sleep -Milliseconds (350 * $attempt)
            Clear-FileAttributesForDelete -Path $Path
        }
    }

    $suffix = Get-Date -Format "yyyyMMdd-HHmmss-fff"
    $oldPath = "$Path.old_$suffix"
    try {
        Rename-Item -LiteralPath $Path -NewName ([System.IO.Path]::GetFileName($oldPath)) -Force -ErrorAction Stop
        Write-Host ("Could not delete directory, moved aside: {0}" -f $oldPath) -ForegroundColor Yellow
        return
    } catch {
        throw ("Failed to remove directory: {0} :: {1}" -f $Path, $lastError.Exception.Message)
    }
}

function Save-PublishUserData {
    if (-not (Test-Path -LiteralPath $PublishPath)) { return }

    Stop-PrivateGalleryVaultProcesses -TargetPublishPath $PublishPath

    foreach ($name in $PreserveNames) {
        $src = Join-Path $PublishPath $name
        if (Test-Path -LiteralPath $src) {
            $dst = Join-Path $PreserveRoot $name
            New-Item -ItemType Directory -Path (Split-Path -Parent $dst) -Force | Out-Null
            if (Test-Path -LiteralPath $dst) {
                Remove-DirectoryWithRetry -Path $dst
            }
            Write-Host ("Preserving user data: {0}" -f $name) -ForegroundColor Yellow
            Move-Item -LiteralPath $src -Destination $dst -Force -ErrorAction Stop
        }
    }
}

function Restore-PreservedPublishData {
    if (-not (Test-Path -LiteralPath $PreserveRoot)) { return }

    New-Item -ItemType Directory -Path $PublishPath -Force | Out-Null

    foreach ($name in $PreserveNames) {
        $src = Join-Path $PreserveRoot $name
        if (Test-Path -LiteralPath $src) {
            $dst = Join-Path $PublishPath $name
            if (Test-Path -LiteralPath $dst) {
                Remove-DirectoryWithRetry -Path $dst
            }
            Write-Host ("Restoring user data: {0}" -f $name) -ForegroundColor Yellow
            Move-Item -LiteralPath $src -Destination $dst -Force -ErrorAction Stop
        }
    }

    try {
        Remove-Item -LiteralPath $PreserveRoot -Recurse -Force -ErrorAction SilentlyContinue
        $parent = Split-Path -Parent $PreserveRoot
        if ((Test-Path -LiteralPath $parent) -and -not @(Get-ChildItem -LiteralPath $parent -Force -ErrorAction SilentlyContinue).Count) {
            Remove-Item -LiteralPath $parent -Force -ErrorAction SilentlyContinue
        }
    } catch { }
}

function Warn-SourceBackups {
    $backupFiles = @(Get-ChildItem -LiteralPath $ScriptRoot -Recurse -File -Force -ErrorAction SilentlyContinue |
        Where-Object {
            $_.FullName -notlike "*\publish\*" -and
            $_.FullName -notlike "*\.git\*" -and
            ($_.Name -like "*.bak" -or $_.Name -like "*.bak-*" -or $_.Name -like "*.backup" -or $_.Name -like "*.orig" -or $_.Name -like "*.tmp")
        })

    if ($backupFiles.Count -gt 0) {
        Write-Host ("Source backup/temp files found: {0}. They will be excluded from release ZIP." -f $backupFiles.Count) -ForegroundColor Yellow
        $backupFiles | Select-Object -First 8 | ForEach-Object {
            $rel = $_.FullName
            if ($rel.StartsWith($ScriptRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
                $rel = $rel.Substring($ScriptRoot.Length).TrimStart('\')
            }
            Write-Host (" - {0}" -f $rel) -ForegroundColor DarkYellow
        }
        if ($backupFiles.Count -gt 8) {
            Write-Host (" - ... and {0} more" -f ($backupFiles.Count - 8)) -ForegroundColor DarkYellow
        }
    }
}

function Test-ExcludedReleasePath {
    param([string]$RelativePath, [bool]$IsDirectory)

    $normalized = $RelativePath -replace '/', '\'
    $parts = @($normalized.Split('\') | Where-Object { $_ -ne "" })
    foreach ($part in $parts) {
        if ($part -ieq "vault" -or $part -ieq "logs" -or $part -ieq "diagnostics" -or $part -ieq "restore-safety-backups") {
            return $true
        }
    }

    if (-not $IsDirectory) {
        $name = [System.IO.Path]::GetFileName($normalized)
        if ($name -ieq "PrivateGalleryVault-win-x64.zip") { return $true }
        if ($name -like "*.bak" -or $name -like "*.bak-*" -or $name -like "*.backup" -or $name -like "*.orig" -or $name -like "*.tmp") {
            return $true
        }
    }

    return $false
}

function New-ReleaseZip {
    if (-not (Test-Path -LiteralPath $PublishPath)) {
        throw ("Publish folder does not exist: {0}" -f $PublishPath)
    }

    if (Test-Path -LiteralPath $PackageStage) {
        Remove-DirectoryWithRetry -Path $PackageStage
    }
    New-Item -ItemType Directory -Path $PackageStage -Force | Out-Null

    if (Test-Path -LiteralPath $ReleaseZipPath) {
        Remove-Item -LiteralPath $ReleaseZipPath -Force -ErrorAction SilentlyContinue
    }

    $publishFull = [System.IO.Path]::GetFullPath($PublishPath).TrimEnd('\')
    $items = @(Get-ChildItem -LiteralPath $PublishPath -Recurse -Force -ErrorAction Stop)
    foreach ($item in $items) {
        $itemFull = [System.IO.Path]::GetFullPath($item.FullName)
        $rel = $itemFull.Substring($publishFull.Length).TrimStart('\')
        if ([string]::IsNullOrWhiteSpace($rel)) { continue }

        if (Test-ExcludedReleasePath -RelativePath $rel -IsDirectory $item.PSIsContainer) {
            continue
        }

        $dest = Join-Path $PackageStage $rel
        if ($item.PSIsContainer) {
            New-Item -ItemType Directory -Path $dest -Force | Out-Null
        } else {
            New-Item -ItemType Directory -Path (Split-Path -Parent $dest) -Force | Out-Null
            Copy-Item -LiteralPath $item.FullName -Destination $dest -Force -ErrorAction Stop
        }
    }

    New-Item -ItemType Directory -Path $PublishRoot -Force | Out-Null
    Compress-Archive -Path (Join-Path $PackageStage "*") -DestinationPath $ReleaseZipPath -CompressionLevel Optimal -Force
    Remove-DirectoryWithRetry -Path $PackageStage

    Write-Host ("Release ZIP: {0}" -f $ReleaseZipPath) -ForegroundColor Green
}

if (-not (Test-Path -LiteralPath $ProjectPath)) {
    throw ("Project file not found: {0}" -f $ProjectPath)
}

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "Private Gallery Vault Release Build" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ("Project: {0}" -f $ProjectPath)
Write-Host ""

Write-Host "Current .NET SDK:" -ForegroundColor Yellow
& dotnet --version
if ($LASTEXITCODE -ne 0) {
    throw "dotnet command failed. Install .NET 8 SDK or later."
}

Write-Host ""
Write-Host "Installed SDKs:" -ForegroundColor Yellow
& dotnet --list-sdks

try {
    Warn-SourceBackups

    if (Test-Path -LiteralPath $PublishPath) {
        Write-Host ""
        Write-Host ("Existing publish folder found: {0}" -f $PublishPath) -ForegroundColor Yellow
        Save-PublishUserData
        Write-Host "Cleaning publish folder..." -ForegroundColor Yellow
        Remove-DirectoryWithRetry -Path $PublishPath
    }

    Write-Section "1/2 Restoring project..."
    & dotnet restore $ProjectPath -r win-x64 /p:MSBuildEnableWorkloadResolver=false
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore failed."
    }

    Write-Section "2/2 Publishing Release build..."
    & dotnet publish $ProjectPath `
        -c Release `
        -r win-x64 `
        --self-contained true `
        --no-restore `
        /p:MSBuildEnableWorkloadResolver=false `
        /p:PublishSingleFile=false `
        /p:PublishTrimmed=false `
        /p:DebugType=embedded `
        -o $PublishPath

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed. Check compiler errors above."
    }

    Restore-PreservedPublishData
    $restoredPreservedData = $true

    New-ReleaseZip

    Write-Host ""
    Write-Host "Release build completed." -ForegroundColor Green
    Write-Host ("Run: {0}" -f (Join-Path $PublishPath "PrivateGalleryVault.exe")) -ForegroundColor Green
    Write-Host "Release ZIP excludes vault/logs/diagnostics/backup/temp files." -ForegroundColor Green
} catch {
    if (-not $restoredPreservedData) {
        try {
            Restore-PreservedPublishData
        } catch {
            Write-Host ("Additional error while restoring preserved user data: {0}" -f $_.Exception.Message) -ForegroundColor Red
        }
    }
    throw
}
