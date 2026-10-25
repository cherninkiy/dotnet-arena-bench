# Стенд A — final

Единственный полный прогон каждого проекта, на числах которого построены
таблицы [`docs/poc-1-serialization.md`](../../docs/poc-1-serialization.md) и
[`docs/poc-2-allocator.md`](../../docs/poc-2-allocator.md).

## Прогоны

| Проект | Run | Лог (старт) | Кейсов | Длительность | Отчётов |
|---|---|---|---:|---:|---:|
| allocator | run1 | `2026-10-22 22:27:09` | 241 | 1:24:37 | 15 классов |
| serialization | run1 | `2026-10-22 21:42:31` | 122 | 0:41:22 | 9 классов |

Проекты запускались не одновременно: сначала сериализация, затем аллокатор.
Оба — `--job default` (`DefaultJob`), полная матрица без `--filter`.

## Окружение (из шапок `*-report-github.md`)

| Параметр | Значение |
|---|---|
| BenchmarkDotNet | 0.15.8 |
| ОС | Windows 11 (10.0.26300.9711) |
| CPU | AMD Ryzen 7 7800X3D 4.20 GHz, 1 CPU, 8 physical / 16 logical cores |
| .NET SDK | 10.0.402 |
| Runtime | .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT |
| JIT-архитектура | `x86-64-v4` |
| Affinity (BDN) | `0xFFFF` — все 16 логических ядер |
| Power plan | High performance (GUID `8c5e7fda-…`), переключён BenchmarkDotNet |
| Job | `DefaultJob` |
| `TieredCompilation` / `TieredPGO` | не форсировались в `.csproj` этого прогона |
| Commit | **не записан** в логах; дерево исходников неизвестно |
| Путь сборки | `C:\Users\mmork\OneDrive\Документы\dotnet-arena-bench\…` |

## Как получены файлы

Старые отчёты лежали одной плоской папкой `BenchmarkDotNet.Artifacts/results-full`
без логов. При переносе они разделены по префиксу
`ArenaBench.Allocator.Bench.*` / `ArenaBench.Serialization.Bench.*`, а логи
`BenchmarkRun-20261022-222709.log` и `BenchmarkRun-20261022-214231.log`
приложены к соответствующим проектам. Содержимое CSV/MD/HTML не менялось.

## Ограничения

- **Не смешивать абсолютные значения со стендом B** (см. `../README.md`).
- Commit и точная ревизия исходников не восстановлены; в логе нет `git`
  метаданных. Это единственный стенд, на котором сняты «финальные» таблицы,
  поэтому его происхождение зафиксировано по Environment BDN и путям сборки.
- E04 снят при `Parallelism=4`, но affinity не ограничивалась восемью ядрами:
  BDN использовал все 16 логических. На стенде B — `taskset` на 8 физических ядер.
- RSS, GC pause, pinning time и PMU не собирались.

## Целостность

`SHA256SUMS` рядом содержит SHA-256 для каждого лога и файла в `results/`
(пути относительно этого каталога). Проверка:

```bash
cd results/stand-a-2026-10-22-win11-7800x3d
sha256sum -c SHA256SUMS
```
