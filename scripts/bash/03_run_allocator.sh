#!/usr/bin/env bash
set -euo pipefail

# Полная матрица бенча аллокатора (ArenaBench.Allocator.Bench).
# Запуск без параметров: все настройки фиксированы внутри.
# Ядра по умолчанию — 8 физических без SMT: 0,2,4,6,8,10,12,14 (override: CPUS).
# Переопределяются также ARTIFACTS_ROOT и LABEL.

cd "$(dirname "$0")/../.."

ARTIFACTS_ROOT="${ARTIFACTS_ROOT:-$HOME/arena-bench-artifacts}"
CPUS="${CPUS:-0,2,4,6,8,10,12,14}"
JOB="default"
LABEL="${LABEL:-allocator}"
PROJECT="benchmarks/ArenaBench.Allocator.Bench"

export DOTNET_TieredCompilation=1
export DOTNET_TieredPGO=1
export DOTNET_ReadyToRun=1
export DOTNET_NOLOGO=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1

DATE="$(date +%Y%m%d-%H%M%S)"
COMMIT="$(git rev-parse --short HEAD 2>/dev/null || echo local)"
OUT="$ARTIFACTS_ROOT/${DATE}-${COMMIT}-${LABEL}"
mkdir -p "$OUT"

echo "=== Allocator full matrix ==="
echo "CPUs (taskset): $CPUS"
echo "Job: $JOB"
echo "Filter: (none — full matrix)"
echo "Artifacts: $OUT"
echo

taskset -c "$CPUS" dotnet run -c Release --project "$PROJECT" -- \
  --job "$JOB" \
  --artifacts "$OUT"