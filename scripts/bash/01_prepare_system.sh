#!/usr/bin/env bash
# 01_prepare_system.sh
# Проверка и подготовка машины перед прогоном бенчмарков.
# Не убивает процессы автоматически — только показывает состояние и рекомендации.

set -u

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

echo "=== arena-bench: system preparation check ==="
echo "root: $ROOT_DIR"
echo

echo "=== Required tools ==="
for c in dotnet taskset git; do
    if command -v "$c" >/dev/null 2>&1; then
        echo "[ok]   $c: $(command -v "$c")"
    else
        echo "[MISSING] $c"
    fi
done
echo

echo "=== OS / CPU ==="
uname -a
lscpu | grep -E 'Model name|Architecture|CPU\(s\)|Thread|Core|Socket|NUMA' || true
echo

echo "=== Power profile / governor ==="
if command -v powerprofilesctl >/dev/null 2>&1; then
    echo "current: $(powerprofilesctl get 2>/dev/null || echo unknown)"
    if powerprofilesctl set performance >/dev/null 2>&1; then
        echo "set performance: OK"
    else
        echo "could not set performance (polkit/sudo). Run manually: powerprofilesctl set performance"
    fi
    echo "now: $(powerprofilesctl get 2>/dev/null || echo unknown)"
elif command -v cpupower >/dev/null 2>&1; then
    echo "current governor: $(cat /sys/devices/system/cpu/cpu0/cpufreq/scaling_governor 2>/dev/null || echo n/a)"
    if sudo cpupower frequency-set -g performance >/dev/null 2>&1; then
        echo "set performance: OK"
    else
        echo "could not set performance. Run manually: sudo cpupower frequency-set -g performance"
    fi
else
    echo "neither powerprofilesctl nor cpupower found"
fi
echo

echo "=== Battery ==="
cat /sys/class/power_supply/BAT*/status 2>/dev/null || echo "no battery (desktop?)"
echo

echo "=== Memory ==="
free -h
echo

echo "=== Thermal ==="
for z in /sys/class/thermal/thermal_zone*/temp; do
    [ -f "$z" ] && echo "$z: $(awk '{printf "%.1f C\n", $1/1000}' "$z")"
done
if command -v sensors >/dev/null 2>&1; then
    sensors
else
    echo "sensors not installed (optional: sudo apt install lm-sensors)"
fi
echo

echo "=== Top CPU consumers ==="
ps -eo pid,comm,%cpu,%mem --sort=-%cpu | head -12
echo

echo "=== Recommendations ==="
echo "- Close IDE, browser, Docker, sync/antivirus before the final run."
echo "- Prefer a clean TTY (Ctrl+Alt+F3) or SSH without GUI."
echo "- Keep the laptop on AC power."
echo "- Fix CPU cores with taskset for reproducibility."
echo "- If StdDev/Mean > 5%, do not compare at percent level."
echo "Done."
