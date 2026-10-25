# Стенд B — provisional replication

Два полных прогона каждого проекта на другой машине. Стенд нужен, чтобы
проверить **направление и устойчивость отношений**, а не заменить абсолютные
числа стенда A. Причины статуса `provisional`: governor `schedutil` и
`Failed to set up high priority (Permission denied)` в каждом логе.

## Прогоны

| Проект | Run | Лог (старт) | Кейсов | Длительность | Отчётов |
|---|---|---|---:|---:|---:|
| allocator | run1 | `2026-10-24 19:19:10` | 241 | 1:21:04 | 15 классов |
| serialization | run1 | `2026-10-24 20:45:21` | 122 | 0:38:19 | 9 классов |
| allocator | run2 | `2026-10-24 21:26:34` | 241 | 1:20:19 | 15 классов |
| serialization | run2 | `2026-10-24 22:52:10` | 122 | 0:38:01 | 9 классов |

Пары `run1`/`run2` — независимые полные прогоны одной и той же матрицы. Внутри
пары сравниваются те же кейсы; между стендами A и B сравнивать абсолютные
значения нельзя.

## Окружение (из шапок `*-report-github.md`)

| Параметр | Значение |
|---|---|
| BenchmarkDotNet | 0.15.8 |
| ОС | Linux Ubuntu 24.04.5 LTS (Noble Numbat) |
| CPU | AMD Ryzen 7 4800H with Radeon Graphics, 1 CPU, 8 physical / 16 logical cores |
| .NET SDK | 10.0.113 |
| Runtime | .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT |
| JIT-архитектура | `x86-64-v3` |
| Affinity (BDN / taskset) | `101010101010101` = `0x5555`, ядра `0,2,4,6,8,10,12,14` |
| CPU governor | `schedutil` (не `performance`) |
| Приоритет процесса | high priority поднять не удалось (permission denied) |
| Job | `DefaultJob` |
| Commit | `5ce4bf4` (`5ce4bf4ea699770c34028f079f5aa626fae37164`) |
| Состояние дерева | **dirty**: в обоих `*.Bench.csproj` добавлены `TieredCompilation=true` и `TieredPGO=true`; `scripts/` не в коммите |
| Переменные окружения | из скриптов 03/04: `DOTNET_TieredCompilation=1`, `DOTNET_TieredPGO=1`, `DOTNET_ReadyToRun=1`, `DOTNET_NOLOGO=1` (логи их не печатают) |

Команды соответствуют [`scripts/bash/03_run_allocator.sh`](../../scripts/bash/03_run_allocator.sh)
и [`scripts/bash/04_run_serialization.sh`](../../scripts/bash/04_run_serialization.sh):
`taskset -c 0,2,4,6,8,10,12,14 dotnet run -c Release --project … -- --job default`.
Скрипты, которыми снят прогон, после него переименованы из `09/10` в `03/04`
без изменения команды BDN; в новых версиях путь артефактов сокращён до
`…-allocator` / `…-serialization`, а в старых ссылках могут встречаться имена
`09_run_allocator_full.sh` / `10_run_serialization_full.sh`.
Каждый прогон писал в отдельный каталог BDN; логи и `results/` перенесены сюда
без изменения содержимого.

## Повторяемость run1 против run2

| Проект | Кейсов | Медиана `abs(δ)` | p95 `abs(δ)` | `abs(δ) > 10 %` |
|---|---:|---:|---:|---:|
| allocator | 241 | 0.86 % | 9.39 % | 11 / 241 |
| serialization | 122 | 0.60 % | 5.41 % | 3 / 122 |

Худшие классы по p95: `MarkReleaseBench` 31.1 %, `SolverParallelBench` 22.7 %,
`AllocSingleBench` 15.9 %, `SolverCsrBench` 12.8 %. Отдельный выброс —
`T2ExtendedRecord128Bench`: baseline `S01` 13.669 µs в run1 против 11.057 µs в
run2 (−19 %), из-за чего **все отношения к `S01` на 128 B** гуляют на 20–24 %.
Абсолютные значения `S05`–`S15` при этом стабильны (2.6 µs и 1.03 µs).

## Отношения между стендами

Медиана `abs(stand B − stand A)` по совпавшим кейсам:

| Проект | B run1 vs A | B run2 vs A |
|---|---:|---:|
| allocator | 68.72 % | 68.34 % |
| serialization | 54.32 % | 54.46 % |

Поэтому в docs стенд A оставлен источником абсолютных таблиц, а стенд B —
источником проверки отношений. Изменения, которые повторный прогон **не**
подтвердил или подтвердил только по знаку, перечислены в
[`REPLICATION.md`](REPLICATION.md) и в соответствующих разделах отчётов.

## Ограничения

- `schedutil` и отсутствие high priority могут менять абсолютные числа и
  усиливать параллельные кейсы (E04); это не финальный профиль.
- `TieredPGO=true` форсирован в `.csproj` и через переменные окружения; на
  стенде A эти флаги не форсировались. Это ещё одна причина не сравнивать
  абсолютные значения.
- Данные синтетические и детерминированные; RSS/GC/pinning/PMU не собирались.

## Целостность

`SHA256SUMS` рядом содержит SHA-256 для каждого лога и файла в `results/`
(пути относительно этого каталога). Проверка:

```bash
cd results/stand-b-2026-10-24-linux-4800h
sha256sum -c SHA256SUMS
```
