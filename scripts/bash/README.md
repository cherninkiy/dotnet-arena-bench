# Bash-хелперы для прогонов

Linux-only скрипты для подготовки машины и запуска полных матриц
`ArenaBench.Allocator.Bench` и `ArenaBench.Serialization.Bench`.
Канонические команды и правила чтения чисел — в
[`docs/methodology.md`](../../docs/methodology.md); этот набор лишь
фиксирует окружение и повторяет команды, которыми снят стенд B.

## Состав

| № | Скрипт | Что делает |
|---|---|---|
| 01 | `01_prepare_system.sh` | Проверяет `dotnet`, `taskset`, CPU, governor/питание, память, температуру и топ-процессы; пытается включить `performance` |
| 02 | `02_build_and_test.sh` | `dotnet build -c Release` и `dotnet test -c Release` |
| 03 | `03_run_allocator.sh` | Полная матрица аллокатора, `--job default`, ядра через `taskset` |
| 04 | `04_run_serialization.sh` | Полная матрица сериализации, `--job default`, ядра через `taskset` |

## Порядок запуска

```bash
./scripts/bash/01_prepare_system.sh
./scripts/bash/02_build_and_test.sh

# Полные матрицы — последовательно, с паузой на остывание
./scripts/bash/03_run_allocator.sh
./scripts/bash/04_run_serialization.sh
```

`03/04` запускают **всю** матрицу без `--filter`: это те же команды, что
использованы для `results/stand-b-2026-10-24-linux-4800h/`. Для одной серии
используйте `dotnet run` с `--filter` из `docs/methodology.md`; отдельного
раннера для серий в репозитории нет, чтобы не держать два набора скриптов.

## Переменные окружения

| Переменная | Где | По умолчанию | Назначение |
|---|---|---|---|
| `ARTIFACTS_ROOT` | 03/04 | `$HOME/arena-bench-artifacts` | Корень каталогов с отчётами BDN |
| `CPUS` | 03/04 | `0,2,4,6,8,10,12,14` | Список CPU для `taskset`; переопределяйте под топологию машины |
| `LABEL` | 03/04 | `allocator` / `serialization` | Метка в имени каталога артефактов |

По умолчанию выбраны 8 физических ядер без SMT (чётные CPU) — так снят
стенд B. На машине с другим числом ядер проверьте `lscpu` и задайте `CPUS`,
иначе `taskset` завершится ошибкой.

## Артефакты

- Каталог: `$ARTIFACTS_ROOT/<date>-<commit>-<label>/`.
- Внутри — `results/` с `*-report-github.md`, CSV и HTML, рядом BDN пишет
  `BenchmarkRun-*.log`.
- Артефакты не коммитятся. Для публикации чисел переносите их в
  `results/<стенд>/<run>/{allocator,serialization}/` по правилам
  [`results/README.md`](../../results/README.md), затем обновляйте `MANIFEST.md`
  и `SHA256SUMS`.

## Ограничения

- Скрипты рассчитаны на Linux: используются `taskset`, `lscpu`, `powerprofilesctl`
  или `cpupower`. Для Windows-стенда A команды запускались вручную; готового
  PowerShell-эквивалента нет.
- `01_prepare_system.sh` — проверка с попыткой смены governor. Если прав нет,
  он печатает подсказку, но прогон продолжается; фактический governor нужно
  записать в `MANIFEST.md`.
- `02` дублирует шаги CI, но полезен как локальный preflight перед финальным
  прогоном.
