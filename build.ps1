# TOTP Vault ビルドスクリプト（Windows PowerShell / PowerShell 7）
#   .\build.ps1            … テスト実行 + dist\TotpVault.exe の作成
#   .\build.ps1 -Installer … さらに Inno Setup でインストーラーを作成（Inno Setup 6 が必要）
param([switch]$Installer)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

Write-Host "== テスト実行 ==" -ForegroundColor Cyan
dotnet test tests/TotpVault.Core.Tests -c Release --logger "trx;LogFileName=test-results.trx" --results-directory artifacts/test
if ($LASTEXITCODE -ne 0) { throw "テストが失敗しました" }

Write-Host "== exe の作成 ==" -ForegroundColor Cyan
dotnet publish src/TotpVault.App -c Release -o dist
if ($LASTEXITCODE -ne 0) { throw "ビルドに失敗しました" }
Write-Host "作成完了: dist\TotpVault.exe" -ForegroundColor Green

if ($Installer) {
    $iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    if (-not (Test-Path $iscc)) { throw "Inno Setup 6 が見つかりません: $iscc" }
    & $iscc installer\TotpVault.iss
    Write-Host "作成完了: artifacts\installer\" -ForegroundColor Green
}
