# CompanionAI_v2 Build and Deploy Script
$ErrorActionPreference = "Stop"

$SourceDll = "c:\Users\veria\Downloads\CompanionAI_v2\bin\Release\net481\CompanionAI_v2.dll"
$TargetDll = "C:\Users\veria\AppData\LocalLow\Owlcat Games\Warhammer 40000 Rogue Trader\UnityModManager\CompanionAI_v2\CompanionAI_v2.dll"

Write-Host "=== Building CompanionAI_v2 ===" -ForegroundColor Cyan
Set-Location "c:\Users\veria\Downloads\CompanionAI_v2"
dotnet build CompanionAI_v2.csproj -c Release

if ($LASTEXITCODE -eq 0) {
    Write-Host "Build succeeded!" -ForegroundColor Green

    Write-Host "=== Deploying to mod folder ===" -ForegroundColor Cyan
    try {
        Copy-Item -Path $SourceDll -Destination $TargetDll -Force
        $sourceInfo = Get-Item $SourceDll
        $targetInfo = Get-Item $TargetDll

        if ($sourceInfo.Length -eq $targetInfo.Length) {
            Write-Host "Deploy succeeded!" -ForegroundColor Green
            Write-Host "Source: $($sourceInfo.Length) bytes, $($sourceInfo.LastWriteTime)"
            Write-Host "Target: $($targetInfo.Length) bytes, $($targetInfo.LastWriteTime)"
        } else {
            Write-Host "WARNING: File sizes don't match!" -ForegroundColor Yellow
        }
    } catch {
        Write-Host "Deploy FAILED: $_" -ForegroundColor Red
        Write-Host "Is the game running? Close it and try again." -ForegroundColor Yellow
    }
} else {
    Write-Host "Build FAILED!" -ForegroundColor Red
}
