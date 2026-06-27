# Windows PowerShell에서 실행하세요.
# 결과: publish/win-x64/PrivateGalleryVault.exe 와 실행 폴더가 생성됩니다.
# WPF 프로젝트 파일을 직접 restore/publish하여 SDK 환경 차이에 따른 Solution restore 문제를 줄입니다.
$ErrorActionPreference = "Stop"

Set-Location $PSScriptRoot

# 빌드 로그와 워크로드 검사로 인한 불필요한 실패를 줄입니다. WPF 프로젝트는 별도 workload가 필요 없습니다.
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = "1"
$env:MSBuildEnableWorkloadResolver = "false"

$ProjectPath = Join-Path $PSScriptRoot "src\PrivateGalleryVault\PrivateGalleryVault.csproj"
$PublishPath = Join-Path $PSScriptRoot "publish\win-x64"

if (-not (Test-Path $ProjectPath)) {
    throw "프로젝트 파일을 찾을 수 없습니다: $ProjectPath"
}

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "Private Gallery Vault Release Build" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "Project: $ProjectPath"
Write-Host ""

Write-Host "사용 중인 .NET SDK:" -ForegroundColor Yellow
& dotnet --version
if ($LASTEXITCODE -ne 0) {
    throw "dotnet 명령을 실행할 수 없습니다. .NET 8 SDK 이상을 설치했는지 확인하세요."
}

Write-Host ""
Write-Host "설치된 SDK 목록:" -ForegroundColor Yellow
& dotnet --list-sdks

if (Test-Path $PublishPath) {
    Write-Host ""
    Write-Host "기존 publish 폴더 삭제: $PublishPath" -ForegroundColor Yellow
    Remove-Item $PublishPath -Recurse -Force
}

Write-Host ""
Write-Host "1/2 프로젝트 복원 중..." -ForegroundColor Cyan
& dotnet restore $ProjectPath -r win-x64 /p:MSBuildEnableWorkloadResolver=false
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore 실패. SDK 설치가 손상된 경우 관리자 PowerShell에서 'dotnet workload repair' 또는 'dotnet workload update'를 먼저 실행해 보세요."
}

Write-Host ""
Write-Host "2/2 Release publish 중..." -ForegroundColor Cyan
& dotnet publish $ProjectPath `
    -c Release `
    -r win-x64 `
    --self-contained true `
    --no-restore `
    /p:MSBuildEnableWorkloadResolver=false `
    /p:PublishSingleFile=false `
    /p:PublishReadyToRun=false `
    -o $PublishPath
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish 실패. 위의 컴파일 오류 내용을 확인하세요."
}

Write-Host ""
Write-Host "완료: publish\win-x64 폴더 전체를 ZIP으로 압축해서 이동하면 됩니다." -ForegroundColor Green
Write-Host "실행 파일: publish\win-x64\PrivateGalleryVault.exe" -ForegroundColor Green
