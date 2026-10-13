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

Нативная сторона — **без C-кода**: P/Invoke на libc
(`memcpy`/`memset`/`memcmp`) и `[UnmanagedCallersOnly]`
([ADR-002](docs/adr/ADR-002-native-side.md)). Дизайн arena —
[ADR-001](docs/adr/ADR-001-arena-design.md); предупреждение:
`Reset()` / `Dispose()` арены инвалидирует ранее выданные
`Memory<T>` / `Span<T>` / `IntPtr` (UB, ADR-001 D9).

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

Результаты для `docs/*.md` фиксируются на одной выделенной машине
без фоновой нагрузки (окружение — из вывода BenchmarkDotNet,
seed фиксирован — 42). CI-прогон (`--job short`) — только smoke,
не для сравнения.

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