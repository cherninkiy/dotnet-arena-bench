# dotnet-arena-bench

> Экспериментальный PoC: даёт ли **memory arena** поверх unmanaged-памяти
> реальный выигрыш в .NET — на сериализации и на границах с внешними
> компонентами.

[![build](https://github.com/cherninkiy/dotnet-arena-bench/actions/workflows/ci.yml/badge.svg)](https://github.com/cherninkiy/dotnet-arena-bench/actions/workflows/ci.yml)
[![license: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com)

---

## Зачем это

`ArrayPool`, `MemoryPool`, `stackalloc`, `BinaryReader`, pinned-массивы —
в .NET уже есть всё для быстрой работы с памятью. Но есть ниша, где
они либо не подходят, либо проигрывают:

- **P/Invoke** — pinned managed-массив блокирует GC и фрагментирует кучу.
- **Alignment** — managed-массивы не дают контроля над выравниванием
  под кэш-линию или SIMD.
- **Mixed workload** — 2–3 типа данных с разным lifetime и alignment
  не уживаются в одном пуле.
- **Rollback** — в Branch & Bound откат узла должен быть O(1), а не
  возврат N буферов в пул.

PoC проверяет гипотезу: **arena поверх unmanaged-памяти закрывает эти
ниши лучше, чем managed-альтернативы.** Или не закрывает — это тоже
результат.

## Что внутри

Два независимых PoC, каждый отвечает на один вопрос.

### PoC-1: Serialization

**Вопрос:** какой способ сериализации/десериализации быстрее
и экономнее на интеграциях, и даёт ли arena выигрыш?

Сравниваются:

- **Источники:** `MemoryStream`, `FileStream`, `NetworkStream`,
  `UnmanagedMemoryStream`, `byte[]`, arena.
- **Способы чтения:** `BinaryReader`, `BinaryPrimitives`,
  `MemoryMarshal.Read`, `Unsafe.ReadUnaligned`, `Cast<T>`, manual, Pipelines.
- **Форматы:** binary stream, binary structured, text/JSON.
- **Alignment:** tight, 16, 64.
- **Endianness:** native, LE, BE, mixed.

### PoC-2: Allocator

**Вопрос:** даёт ли arena выигрыш как аллокатор по сравнению
с `ArrayPool` / `NativeMemory` / `PathPool` / `T[]`,
в том числе на границах системы?

Сравниваются:

- **Аллокаторы:** Arena, `ArrayPool<T>`, `NativeMemory`, `stackalloc`,
  `T[]`, `PathPool` (мок семантики из netgraph-optimizer).
- **Сценарии:** alloc/free, CSR обход, B&B rollback, P/Invoke,
  native callbacks, egress, mixed workload.
- **Параллелизм:** single-threaded, `Parallel.For`
  (thread-local vs shared).

Реализованы все серии: A (`A01`–`A04`: alloc/free, batch, churn, zeroing),
B (`B01`–`B03`: Span/Memory, AoS vs SoA), E (`E01`–`E05`: CSR, B&B rollback,
parallel, incremental solve), I (`I01`–`I09`: libc, `[UnmanagedCallersOnly]`,
egress, mmap, raw-экспорт) и M (`M01`–`M04`: mixed workload и skew) плюс ядро
`ArenaBench.Allocator.Core` (микро-CSR, `PathPoolMock`, Mini B&B, сценарии
границ, генератор workload).

Нативная сторона — **без C-кода**: P/Invoke на libc
(`memcpy`/`memset`/`memcmp`) и `[UnmanagedCallersOnly]`
([ADR-002](docs/adr/ADR-002-native-side.md)). Дизайн arena —
[ADR-001](docs/adr/ADR-001-arena-design.md).

> ⚠️ **`Reset()` / `Dispose()` арены инвалидирует всё, что она выдала**
> ([ADR-001 D9](docs/adr/ADR-001-arena-design.md)). `Memory<T>` из
> `AllocMemory<T>` отлавливается (арена считает поколения, обращение к
> протухшему виду бросает `InvalidOperationException`), а `Span<T>` и `IntPtr`
> проверить нечем — их использование после `Reset`/`Dispose` остаётся
> undefined behavior. `Mark`/`Release` память не переиспользует и виды не
> инвалидирует.

## Что получится на выходе

- `docs/poc-1-serialization.md` — таблицы, графики, выводы.
- `docs/poc-2-allocator.md` — то же для PoC-2.
- `docs/summary.md` — **инженерное решение**: где arena оправдана,
  где нет, и почему.

**Что НЕ входит** в основной прогон (см.
[`docs/backlog.md`](docs/backlog.md)): GC pause, pinning time,
cache miss rate / branch mispredictions (PMU — только Linux),
ARM64-специфика, persistent arena, multi-tenancy.

Ожидаемые выводы (гипотезы, не факты):

| Сценарий | Гипотеза |
|---|---|
| P/Invoke, native callbacks | ✅ arena выигрывает (нет pinning) |
| Alignment-критичные структуры (64 B) | ✅ arena выигрывает (cache-line) |
| Mixed workload (2–3 типа, skew) | ✅ per-type арены выигрывают |
| B&B rollback | ✅ `Mark`/`Release` за O(1) |
| CSR обход в солвере | ⚠️ `int[]` уже zero-alloc, arena мало что даёт |
| Text / JSON | ❌ `Utf8JsonWriter` + `ArrayBufferWriter` справляются |
| Homogeneous bulk | ❌ `ArrayPool` достаточен |

## Структура

```
dotnet-arena-bench/
├── src/
│   ├── ArenaBench.Core/                # arena, SafeHandle, MemoryManager
│   ├── ArenaBench.Serialization.Core/  # структуры, моки источников
│   └── ArenaBench.Allocator.Core/      # микро-солвер, границы, workload
├── benchmarks/
│   ├── ArenaBench.Serialization.Bench/ # PoC-1
│   └── ArenaBench.Allocator.Bench/     # PoC-2
├── tests/                              # xunit, round-trip, утечки
└── docs/
    ├── concept.md                      # полный план и критерии успеха
    ├── adr/                            # ADR-001: дизайн arena; ADR-002: native-сторона
    ├── backlog.md                      # отложенные задачи (GC pause, PMU, ARM64…)
    ├── poc-1-serialization.md
    ├── poc-2-allocator.md
    ├── summary.md
    └── methodology.md
```

Один репозиторий, два бенч-проекта, общие компоненты — в `.Core`.
Никаких смешанных бенч-проектов: один PoC — один exe.

## Как запускать

```bash
# Сборка и тесты
dotnet build -c Release
dotnet test -c Release

# PoC-1 (полный прогон)
dotnet run -c Release --project benchmarks/ArenaBench.Serialization.Bench

# PoC-2 (полный прогон)
dotnet run -c Release --project benchmarks/ArenaBench.Allocator.Bench

# Быстрый прогон для CI (short bench — smoke, оба PoC)
dotnet run -c Release --project benchmarks/ArenaBench.Serialization.Bench -- --job short
dotnet run -c Release --project benchmarks/ArenaBench.Allocator.Bench -- --job short
```

Требования: **.NET 10 SDK**.

### Артефакты и ручной прогон

Каждый прогон BenchmarkDotNet складывает отчёты по классам в
`BenchmarkDotNet.Artifacts/results/` (`*-report-github.md`, `*.csv`, `*.html`),
а логи (`*.log`) — в корне `BenchmarkDotNet.Artifacts/`. Каталог не коммитится
(`.gitignore`); в CI он **загружается артефактом** целиком — `benchmarks-<os>`
(хранение 14 дней, загружается даже если шаг с бенчем упал), поэтому отчёт
можно посмотреть и процитировать, не перезапуская прогон. В CI каталог задан
явно (`--artifacts "$GITHUB_WORKSPACE/BenchmarkDotNet.Artifacts"`), чтобы его
расположение не зависело от рабочего каталога шага.

Имена файлов — по классу бенчмарка, например
`ArenaBench.Serialization.Bench.T2ServiceRecord16Bench-report-github.md`
(таблицу из такого файла можно вставлять в `docs/*.md` как есть). Забрать
артефакты конкретного прогона:

```bash
gh run list --workflow ci.yml
gh run download <run-id> -n benchmarks-ubuntu-latest
```

Ручной запуск: **Actions → ci → Run workflow** со входными параметрами:

| Параметр | Значения | Зачем |
|---|---|---|
| `job` | `short` (по умолчанию), `default`, `dry` | `short` — smoke, `default` — полный профиль |
| `filter` | glob BDN: `*T2*`, `*T1StreamBench*`, `*` — все | прогнать одну серию вместо всей матрицы |
| `os` | `all` (по умолчанию) или `ubuntu-latest` / `windows-latest` / `macos-latest` | длинный профиль дешевле на одном раннере |

Push и pull_request идут без входов: `--job short`, вся матрица, а из бенчей —
по одному smoke-классу на PoC: **одна репрезентативная серия** PoC-1
(`*T2ServiceRecord16Bench*`, 20 кейсов: все источники, все стратегии и арена на
16-байтовой записи) и `*Allocator.Bench.SmokeBench*` в PoC-2 (4 кейса: rent,
`Reset`, `Mark`/`Release`, create/dispose). Это держит бенч-шаги в пределах
~3 минут на ОС (полный набор PoC-1 — ~15 минут, матрица PoC-2 — 241 кейс);
остальные серии запускаются ручным прогоном (у входа `filter` по
умолчанию `*`, то есть ручной запуск идёт по всему набору). Логика T1/T3 при
этом не остаётся без проверки: их стратегии и парсеры кросс-проверяются
юнит-тестами, которые CI гоняет на каждой ОС; ядро PoC-2 (CSR, пул семантики
netgraph, B&B) — тестами `ArenaBench.Allocator.Tests`.

Результаты для `docs/*.md` фиксируются на одной выделенной машине
без фоновой нагрузки (окружение — из вывода BenchmarkDotNet; генераторы
данных детерминированные — фиксированные формулы `Create(index)`, без RNG).
CI-прогон (`--job short`) и ручной `--job default` в CI — по-прежнему
не для сравнения: это инфраструктура для повторяемости и просмотра отчётов.

Полный чеклист финального прогона (тихая машина, питание и частоты, фиксация
ядер для параллельных кейсов, правила чтения таблиц) — в
[docs/methodology.md](docs/methodology.md); там же разница между `--job short`
(предварительные числа) и `--job default` (числа для решений).

## Критерии успеха

**PoC-1 успешен, если получены ответы:**

1. `BinaryReader` vs Span-based — во сколько раз и на каких размерах.
2. align 64 vs tight — где выигрыш, где потеря.
3. Где arena выигрывает по T1/T2/T3 — и где нет.
4. **Решение** для netgraph-optimizer: какие форматы и способы чтения.

**PoC-2 успешен, если получены ответы:**

1. arena vs `ArrayPool` / `NativeMemory` / `PathPool` на alloc/free.
2. arena vs pinned `T[]` на P/Invoke.
3. per-type арены vs shared на mixed workload.
4. arena на B&B rollback — обходит ли `PathPool`.
5. **Решение**: тащить arena в netgraph-optimizer или нет, и в какие точки.

Полные критерии, план работ, риски — в [`docs/concept.md`](docs/concept.md).

## Результаты

- **[PoC-1: сериализация](docs/poc-1-serialization.md)** — матрица T1/T2/T3:
  span-based чтение в 2.5–9 раз быстрее `BinaryReader` (10× на кадрах), арена под
  парсинг не даёт ничего, alignment на sequential-чтении не виден.
- **[PoC-2: аллокатор](docs/poc-2-allocator.md)** — серии A/B/E/I/M: арена в
  0.07–0.31× от `PathPool` на партиях, B&B rollback в 1.5× быстрее `PathPool`,
  per-type арены 2.20–3.19×, на границах P/Invoke — паритет с pinned-массивом.
- **[Итог и решения](docs/summary.md)** — сводная таблица «точка → решение →
  обоснование», отрицательные результаты и порядок внедрения.

Числа обоих отчётов сняты `--job default` на выделенной машине без фоновой
нагрузки (AMD Ryzen 7 7800X3D, Windows 11, .NET 10.0.13): это финальные таблицы,
а не предварительный тренд.

## Полный план

Этот README — краткое введение. Все детали — цели, не-цели, оси бенчей,
метрики, план по дням, риски, ожидаемые результаты — в
**[`docs/concept.md`](docs/concept.md)**.

## Лицензия

MIT. См. [LICENSE](LICENSE).

## Связанные проекты

- [netgraph-optimizer](https://github.com/cherninkiy/netgraph-optimizer) —
  high-performance Multi-Commodity Flow solver на C#, к которому
  применяются выводы этого PoC.