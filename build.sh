#!/usr/bin/env bash
# Linux / macOS からのクロスビルド（Windows 用 exe を作成）
set -euo pipefail
cd "$(dirname "$0")"
dotnet test tests/TotpVault.Core.Tests -c Release --logger "trx;LogFileName=test-results.trx" --results-directory artifacts/test
dotnet publish src/TotpVault.App -c Release -o dist
echo "作成完了: dist/TotpVault.exe"
