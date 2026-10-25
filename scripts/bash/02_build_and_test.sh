#!/usr/bin/env bash
# 02_build_and_test.sh
# Сборка и тесты в Release — та же конфигурация, что у бенчей.

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT_DIR"

echo "=== build (Release) ==="
dotnet build -c Release

echo "=== test (Release) ==="
dotnet test -c Release

echo "=== OK: build + tests passed ==="
