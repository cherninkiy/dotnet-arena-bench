# dotnet-arena-bench — Concept

## 1. Назначение

PoC-исследование: даёт ли **memory arena** поверх unmanaged-памяти
ощутимый выигрыш в .NET для сценариев **сериализации** и **интеграции
с внешними компонентами** — по сравнению с managed-альтернативами
(`byte[]`, `ArrayPool<T>`, `PathPool`, pinned-массивы).

Результат — **инженерная рекомендация на основе PoC**: где arena показала
преимущество в измеренных сценариях, где нет, и какие варианты стоит проверять
дальше в продукте. Бенчмарки на реальных данных netgraph вынесены за scope,
поэтому PoC сам по себе не подтверждает эффект на production-нагрузке.

## 2. Контекст

PoC выполняется в связке с проектом **[netgraph-optimizer](https://github.com/cherninkiy/netgraph-optimizer)**
(high-performance Multi-Commodity Flow solver на C#).

Характеристики системы, которые важны для PoC:

- 2–3 типа commodity с разной нагрузкой и разной сериализацией:
  - **T1. Binary stream** — voice/video, variable-length, alignment важен.
  - **T2. Binary structured** — service records, fixed-layout struct.
  - **T3. Text / fixed string / JSON** — текстовые данные.
- Граф в CSR-формате на плоских массивах (`int[]`, `Span<int>`).
- Hot path солвера (Beam Search, Branch & Bound) — zero-allocation.
- Существующий пул `PathPool` на `ConcurrentBag<int[]>`.
- Планируются интеграции: нативный солвер (P/Invoke), egress
  в SDN controller, возможно mmap/shared memory.

## 3. Цели

### 3.1. Основные

1. **Ответить на вопрос:** даёт ли arena выигрыш как аллокатор
   в hot path солвера и на границах системы?
2. **Ответить на вопрос:** какой формат и способ чтения выбирать
   для каждого типа commodity на интеграциях?
3. **Определить кандидатов:** где arena стоит дополнительно проверить в
   netgraph-optimizer и где измерения не дают оснований для перехода.

### 3.2. Дополнительные

- Зафиксировать **отрицательные результаты** — где arena не даёт
  выигрыша, и почему.
- Создать переиспользуемую инфраструктуру (arena, SafeHandle,
  MemoryManager, моки источников) для будущих экспериментов.
- Собрать бенчмарк-репозиторий как reference для подобных задач.

## 4. Не-цели

Явно **вне scope** этого PoC:

- Реализация production-ready arena-библиотеки.
- Замена `ArrayPool` / `MemoryPool` в BCL.
- Multi-tenancy, плагины, AssemblyLoadContext.
- ARM-специфичные оптимизации (отдельная задача).
- Benchmarking на реальных данных netgraph (отдельная задача).
- Оптимизация самого солвера (только измерение эффекта памяти).
- Измерение GC pause / pinning time — требует GC-нагрузочного
  harness; [backlog](backlog.md) B-001/B-002.
- PMU-метрики на Windows (cache miss, branch mispredict) —
  собираются только на Linux (`perf stat`); [backlog](backlog.md)
  B-003.

## 5. Структура PoC

PoC разбит на **два независимых проекта**, каждый отвечает
на один вопрос.

### 5.1. PoC-1: Serialization

**Вопрос:** какой способ сериализации/десериализации быстрее
и экономнее на интеграциях, и даёт ли arena выигрыш?

**Покрывает:**

- **Источники данных:** MemoryStream, FileStream, NetworkStream
  (эмуляция), UnmanagedMemoryStream, `byte[]`, arena.
- **Способы чтения:**
  - T2: BinaryReader, BinaryPrimitives, MemoryMarshal.Read,
    Unsafe.ReadUnaligned, MemoryMarshal.Cast, manual.
  - T1: Span.Slice, Unsafe.ReadUnaligned, Pipelines.
  - T3: Utf8 parse, SearchValues, Utf8JsonWriter.
- **Размеры структур:** 16, 32, 64, 128, 256 B + variable-length.
- **Alignment:** tight, 16, 64.
- **Endianness:** native LE, explicit LE, explicit BE, mixed.
- **Паттерны:** one-shot, scan, random access, streaming.
- **Raw-чтение** (SourcesBench, R01–R04): производительность самого
  источника (`Stream.Read`) без парсинга — отделяет цену чтения
  от цены парсинга; baseline для S01–S04.

**Ключевые сравнения:**

| Вопрос | Бенчи |
|---|---|
| BinaryReader vs Span-based | S01–S04 vs S05–S15 |
| MemoryStream vs FileStream vs NetworkStream | S01 vs S02 vs S03 |
| UnmanagedMemoryStream как мост | S04 |
| BinaryPrimitives vs MemoryMarshal vs Unsafe | S05 vs S07 vs S08 |
| Cast vs Read vs Unsafe | S15 vs S13 vs S14 |
| Tight vs align-16 vs align-64 | S10/S11/S12 |
| Endianness стоимость | S05 vs S06, S19 |
| Variable-length | S21, S22 |
| Streaming с carry | V07, V08 |
| Text/JSON — где arena проигрывает | C03 vs C06 vs C07 |

**Объём:** ~40 бенчей, ~200 подбенчей с `[Params]`.

### 5.2. PoC-2: Allocator

**Вопрос:** даёт ли arena выигрыш как аллокатор по сравнению
с `ArrayPool` / `NativeMemory` / `PathPool` / `T[]`,
в том числе на границах системы?

**Покрывает:**

- **Аллокаторы:** Arena, ArrayPool<T>, NativeMemory,
  stackalloc, T[], PathPool (мок семантики netgraph-optimizer).
- **Сценарии:**
  - A: alloc/free, batch alloc, per-type арены.
  - B: Span/Memory доступ, AoS vs SoA.
  - E: CSR обход, B&B rollback, parallel B&B, incremental solve.
  - I: P/Invoke (out/in/inout), native callbacks, egress,
    mmap (опционально).
  - M: mixed workload (T1/T2/T3), skew, interference.
- **Размеры:** 32 B, 1 KB, 64 KB, 1 MB.
- **Параллелизм:** single-threaded, Parallel.For
  (thread-local vs shared).
- **Lifetime:** per-call, per-request, per-node, per-session.

**Дизайн arena** зафиксирован в [ADR-001](adr/ADR-001-arena-design.md):
single block фиксированного размера (стабильные указатели),
single-thread bump + `ThreadLocalArena`, auto + explicit align
(default 16), `Mark`/`Release` и `Reset` за O(1), zeroing off.

**Предупреждение:** `Reset()` / `Dispose()` инвалидирует все ранее
выданные `Memory<T>` / `Span<T>` / `IntPtr`; использование после —
undefined behavior ([ADR-001](adr/ADR-001-arena-design.md) D9).

**Нативная сторона** — без C-кода, по [ADR-002](adr/ADR-002-native-side.md):
P/Invoke на libc (`memcpy`/`memset`/`memcmp`), callbacks через
`[UnmanagedCallersOnly]`, mmap — через BCL `MemoryMappedFile`
(опционально, I08).

**Моки из netgraph-optimizer:** `PathPoolMock` повторяет семантику
`NetGraph.Core.PathPool` (`ConcurrentBag<int[]>` / `float[]`,
`Rent(minSize)` / `Return`); `CsrGraph` генерируется по образцу
`GraphGenerator.CreateRandom` (кольцо + доп. рёбра) с фиксированным
seed, как в бенчах netgraph-optimizer (`new Random(42)`).

**Ключевые сравнения:**

| Вопрос | Бенчи |
|---|---|
| Arena vs ArrayPool на alloc/free | A01, A03 |
| Arena vs PathPool на B&B rollback | E03 |
| Thread-local arena vs shared | E04 |
| Persistent arena vs recreate | E05 |
| Arena vs pinned T[] на P/Invoke | I01, I02, I03 |
| Native callback без копий | I04 |
| Egress без pinning | I06 |
| 3 арены vs 1 (mixed) | M01, M02 |
| Interference | M03 |

**Объём:** ~24 бенча, ~100 подбенчей с `[Params]`.

### 5.3. Зависимости

```
ArenaBench.Core (arena)               ── используется ──►  PoC-1, PoC-2
ArenaBench.Serialization.Core         ── структуры ─────►  PoC-2 (Allocator)
```

PoC-2 использует **структуры** из `Serialization.Core`
(для mixed workload), но не бенчи. Это переиспользование
инфраструктуры, а не зависимость по смыслу.

## 6. Оси бенчей (сводно)

| Ось | Значения |
|---|---|
| **Источник** | MemoryStream, FileStream, NetworkStream, UnmanagedMemoryStream, byte[], arena |
| **Способ чтения** | BinaryReader, BinaryPrimitives, MemoryMarshal.Read, Unsafe.ReadUnaligned, Cast<T>, manual, Pipelines |
| **Формат** | T1 stream, T2 structured, T3 text/JSON |
| **Размер** | 16, 32, 64, 128, 256 B + variable |
| **Alignment** | tight, 16, 64 ([ADR-001](adr/ADR-001-arena-design.md) D3) |
| **Endianness** | native LE, explicit LE, explicit BE, mixed |
| **Паттерн** | one-shot, scan, random access, streaming |
| **Аллокатор (PoC-2)** | Arena, ArrayPool, NativeMemory, stackalloc, T[], PathPool |
| **Параллелизм** | single, Parallel.For |
| **Lifetime** | per-call, per-request, per-node, per-session |

**Определения:**

- **Mixed endianness** — сообщение, где части имеют разный
  endianness: например, заголовок T1-фрейма в BE (как в сетевых
  протоколах), полезная нагрузка T2 — в native LE.
- **Raw-чтение** — чтение из источника (`Stream.Read`) без
  парсинга (R01–R04).

## 7. Метрики

### 7.1. Обязательные (в scope PoC)

- **ns/op** — latency.
- **GB/s** — throughput (для scan/parse).
- **Allocated bytes/op** — должно быть 0 для Span-based.
- **Gen0/1/2 collections** — должно быть 0.
- **RSS / working set** — для padding overhead и mmap.
- **Latency p50/p99 per-type** — для mixed workload.

Этого достаточно для решения «где arena оправдана».

**Важно:** `MemoryDiagnoser` **не видит unmanaged-аллокации**.
Для arena / `NativeMemory` / stackalloc «Allocated: 0» —
0 по определению (GC-байтов нет), но это не значит, что памяти
нет: реальную стоимость нативной памяти меряем через
RSS / working set (dotnet-counters).

### 7.2. Nice to have (backlog, вне scope PoC)

- **GC pause** от pinned `T[]` — требует фонового GC-нагрузочного
  harness. → [backlog](backlog.md) B-001.
- **Pinning time** — требует EventPipe + dotnet-trace-анализа. →
  [backlog](backlog.md) B-002.
- **Cache miss rate** — PMU-метрики собираются **только на
  Linux-машине** (`perf stat`); на Windows альтернативы —
  ETW/WPR (дороже в настройке и анализе). →
  [backlog](backlog.md) B-003.
- **Branch mispredictions** — только `perf stat` (Linux). →
  [backlog](backlog.md) B-003.
- **Contention** — только для parallel B&B (E04). →
  [backlog](backlog.md) B-004.

### 7.3. Инструменты

- **BenchmarkDotNet** с `[MemoryDiagnoser]`, `[ThreadingDiagnoser]` —
  обязательно.
- **dotnet-counters** — для RSS / working set.
- **dotnet-trace** / **perf stat** — только если останется время;
  не блокируют завершение PoC.

### 7.4. Условия прогона

По аналогии с бенчами netgraph-optimizer:

- **BenchmarkDotNet** со стандартным конфигом +
  `[MemoryDiagnoser]`; оси фиксированы `[Params]` в коде бенчей.
- **Seed фиксирован** (`new Random(42)`) — генерация данных
  воспроизводима, как в `SolverBenchmarks` netgraph-optimizer.
- **Официальные результаты** для `docs/poc-*.md` — с одной
  выделенной машины, без фоновой нагрузки; окружение фиксируется
  из вывода BDN (`HostEnvironmentInfo`).
- **CI-прогон (`--job short`, оба bench-проекта)** — только smoke:
  собрать, прогнать, не упасть; детекция регрессий, не сбор метрик
  ([ADR-002](adr/ADR-002-native-side.md)). Результаты CI не
  сравниваются с локальными.
- PMU-метрики (cache miss, branch mispredict) — отдельный прогон
  на Linux-машине, не в основном прогоне.

## 8. Структура репозитория

```
dotnet-arena-bench/
├── README.md
├── LICENSE                             # MIT
├── .gitignore                          # стандартный .NET
├── .editorconfig                       # стиль кода
├── DotNetArenaBench.slnx
│
├── .github/workflows/
│   └── ci.yml                          # build + test + short bench
│
├── src/
│   ├── ArenaBench.Core/                # arena, SafeHandle, MemoryManager
│   │   ├── Arena.cs
│   │   ├── ArenaScope.cs               # Mark/Release
│   │   ├── SafeArenaHandle.cs
│   │   ├── ArenaMemoryManager.cs
│   │   └── Native/
│   │       ├── NativeMethods.cs        # LibraryImport: memcpy, memset, memcmp
│   │       └── LibcResolver.cs         # libc / libSystem / ucrtbase по ОС (ADR-002)
│   │
│   ├── ArenaBench.Serialization.Core/  # структуры, моки источников
│   │   ├── Structures/
│   │   │   ├── ServiceRecord16.cs
│   │   │   ├── Commodity32.cs
│   │   │   ├── VideoRecord64.cs
│   │   │   ├── ExtendedRecord128.cs
│   │   │   └── MultiFieldRecord256.cs
│   │   ├── Sources/
│   │   │   ├── MemoryStreamFactory.cs
│   │   │   ├── FileStreamFactory.cs
│   │   │   ├── NetworkStreamFactory.cs
│   │   │   └── UnmanagedStreamFactory.cs
│   │   └── Writers/
│   │       ├── BinaryWriterHelper.cs
│   │       └── Utf8WriterHelper.cs
│   │
│   └── ArenaBench.Allocator.Core/      # солвер, границы, workload
│       ├── Solver/
│       │   ├── CsrGraph.cs             # микро-CSR + генератор по образцу GraphGenerator (кольцо + доп. рёбра, seed)
│       │   ├── PathPoolMock.cs         # семантика NetGraph.Core.PathPool: ConcurrentBag<int[]> / float[]
│       │   └── MiniBranchAndBound.cs
│       ├── Boundaries/
│       │   ├── PInvokeScenarios.cs     # I01–I03, I06–I07: libc memcpy/memset/memcmp (ADR-002)
│       │   ├── ManagedCallbacks.cs     # [UnmanagedCallersOnly]-методы (ADR-002)
│       │   └── NativeCallbackScenarios.cs # I04–I05
│       └── Workload/
│           ├── CommodityType.cs
│           └── WorkloadGenerator.cs
│
├── benchmarks/
│   ├── ArenaBench.Serialization.Bench/ # PoC-1
│   │   ├── ArenaBench.Serialization.Bench.csproj
│   │   ├── Program.cs
│   │   ├── BenchmarkConfig.cs
│   │   ├── T1StreamBench.cs            # V01–V08
│   │   ├── T2StructuredBench.cs        # S01–S22
│   │   ├── T3TextBench.cs              # C01–C10
│   │   └── SourcesBench.cs             # R01–R04: raw throughput чтения без парсинга
│   │
│   └── ArenaBench.Allocator.Bench/     # PoC-2
│       ├── ArenaBench.Allocator.Bench.csproj
│       ├── Program.cs
│       ├── BenchmarkConfig.cs
│       ├── AllocBench.cs               # A01–A04
│       ├── AccessBench.cs              # B01–B03
│       ├── SolverBench.cs              # E01–E05
│       ├── BoundariesBench.cs          # I01–I08
│       └── MixedBench.cs               # M01–M04
│
├── tests/
│   ├── ArenaBench.Core.Tests/          # round-trip, утечки native-памяти
│   ├── ArenaBench.Serialization.Tests/
│   └── ArenaBench.Allocator.Tests/
│
└── docs/
    ├── concept.md                      # этот файл
    ├── adr/
    │   ├── ADR-001-arena-design.md     # дизайн arena
    │   └── ADR-002-native-side.md      # эмуляция native-стороны
    ├── backlog.md                      # отложенные задачи (B-001…)
    ├── poc-1-serialization.md          # результаты PoC-1
    ├── poc-2-allocator.md              # результаты PoC-2
    ├── summary.md                      # общий вывод и решения
    └── methodology.md                  # как запускать, как читать
```

**Утечки native-памяти** в тестах проверяются через счётчик живых
`SafeArenaHandle` (финализация + `GC.Collect` /
`GC.WaitForPendingFinalizers` до стабилизации счётчика) и тренд
RSS при массовых create/dispose.

## 9. План работ

### День 0 — фундамент (полдня)

- Создать репозиторий, `.slnx`, `.gitignore`, `LICENSE`, `README.md`.
- Создать `ArenaBench.Core` со скелетом arena по
  [ADR-001](adr/ADR-001-arena-design.md): single block, `Mark`/`Release`,
  auto + explicit align API.
- Настроить CI: build + test.
- Настроить `BenchmarkDotNet` в обоих bench-проектах.

### День 1–2 — PoC-1: Serialization

**День 1:**

- Структуры 16/32/64/128/256 B.
- Моки источников: MemoryStream, FileStream, NetworkStream,
  UnmanagedMemoryStream.
- T2 бенчи: BinaryReader baseline (S01–S04).
- T2 бенчи: Span-based (S05–S09).

**День 2:**

- T2 бенчи: arena (S10–S22).
- T1 бенчи: stream, alignment (V01–V08).
- T3 бенчи: text, JSON (C01–C10).
- Прогон, сбор результатов.
- Написать `docs/poc-1-serialization.md`.

### День 3 — PoC-2: Allocator (часть 1)

- `ArenaBench.Allocator.Core`: микро-CSR, Mini B&B.
- A бенчи: alloc/free, batch (A01–A04).
- B бенчи: Span/Memory, AoS/SoA (B01–B03).
- E бенчи: CSR, B&B rollback, parallel (E01–E05).

### День 4 — PoC-2: Allocator (часть 2)

- I бенчи: P/Invoke через libc, callbacks через `[UnmanagedCallersOnly]`,
  egress (I01–I07; I08 mmap — опционально, через BCL
  `MemoryMappedFile`, [ADR-002](adr/ADR-002-native-side.md)).
- M бенчи: mixed workload, skew (M01–M04).
- Прогон, сбор результатов.
- Написать `docs/poc-2-allocator.md`.
- Написать `docs/summary.md` — общий вывод и решения.

**Итого:** 4–5 дней.

## 10. Критерии успеха

### 10.1. PoC-1 успешен, если

1. Получен численный ответ «BinaryReader vs Span-based» —
   во сколько раз и на каких размерах.
2. Получен численный ответ «align 64 vs tight» —
   для каких размеров выигрыш, для каких потеря.
3. Получен ответ «где arena выигрывает, где нет» —
   по каждому из T1/T2/T3.
4. **Сформулирована рекомендация** по netgraph-optimizer: какие форматы
   и способы чтения использовать или проверить на реальных данных.
5. Отрицательные результаты (например, T3/JSON) зафиксированы
   с объяснением.

### 10.2. PoC-2 успешен, если

1. Получен численный ответ «arena vs ArrayPool / NativeMemory /
   PathPool» на alloc/free и batch.
2. Получен ответ «arena vs pinned T[] на P/Invoke» —
   во сколько раз и на каких размерах.
3. Получен ответ «per-type арены vs shared» на mixed workload.
4. Получен ответ «arena на B&B rollback» — обходит ли PathPool.
5. **Определены кандидаты для продуктовой проверки** arena и сценарии, где
   измерения PoC не дают основания её внедрять.

### 10.3. Общий успех PoC

- Два законченных артефакта (`poc-1-serialization.md`,
  `poc-2-allocator.md`).
- Один сводный документ (`summary.md`) с конкретными
  рекомендациями.
- Инфраструктура (`ArenaBench.Core`) пригодна для дальнейшего
  использования.

## 11. Ожидаемые результаты (гипотезы)

### 11.1. PoC-1 (Serialization)

| Сценарий | Гипотеза |
|---|---|
| BinaryReader (MemoryStream) | 1× (baseline) |
| BinaryReader (FileStream) | 0.2–0.5× |
| BinaryReader (NetworkStream) | 0.1–0.3× |
| UnmanagedMemoryStream + BinaryReader | 0.5–0.8× |
| BinaryPrimitives (byte[]) | 10–20× |
| arena + MemoryMarshal.Read (align 16) | 15–25× |
| arena + Unsafe.Read (align 64) | 18–28× |
| arena + Cast<T> (align 64) | 20–30× |
| Align 64 при size 64 | +50–100% throughput |
| Align 64 при size 16 | −30–50% (padding loss) |
| Align 64 при size 128/256 | ≈ tight — вырожденный случай, контрольный прогон |
| Endianness BE vs LE | −5–10% |
| T3 JSON в arena | ≈ ArrayBufferWriter (arena не выигрывает) |

### 11.2. PoC-2 (Allocator)

| Сценарий | Гипотеза |
|---|---|
| alloc/free × N (мелкие) | Arena, stackalloc ≫ NativeMemory (arena и stackalloc — одного порядка) |
| alloc/free × N (крупные) | Arena ≈ ArrayPool |
| batch / Reset | Arena+Reset ≫ ArrayPool |
| CSR обход | arena ≈ int[] (выигрыш только в GC) |
| B&B rollback | Arena+Mark/Release ≥ PathPool |
| P/Invoke out | arena ≫ pinned T[] |
| Native callback | arena ≫ copy |
| 3 арены vs 1 (mixed, skew) | 3 арены выигрывают |
| Interference | per-type арена устраняет |

### 11.3. Главный ожидаемый вывод

**Arena оправдана:**
- на **границах** (P/Invoke, native callbacks) — нет pinning;
- на **alignment-критичных** структурах (64 B) — cache-line;
- на **mixed workload** (per-type арены, skew);
- на **B&B rollback** (Mark/Release за O(1)).

**Arena не оправдана:**
- внутри солвера на CSR-обходе — `int[]` уже даёт zero-alloc;
- для **текста/JSON** (T3) — `Utf8JsonWriter` + `ArrayBufferWriter`
  справляются без arena;
- для **homogeneous bulk** — `ArrayPool` достаточен.

## 12. Риски и подводные камни

### 12.1. Методологические

- NetworkStream бенчи нестабильны → использовать loopback + `Socket`
  или `PipeStream`.
- FileStream бенчи зависят от ОС/диска → `tmpfs` или явный cold-start.
- Один размер → выводы не переносятся на другие.
- Забыть `Reset()` у арены → overflow, сравнение не то.
- Warmup / tiered JIT → первые итерации искажают.
- «Allocated: 0» у native-путей (arena, NativeMemory) — 0 по
  определению: `MemoryDiagnoser` не видит unmanaged-память;
  контролировать RSS / working set.
- GC pause и pinning time не измеряются в основном прогоне;
  если arena vs pinned окажется близко по ns/op —
  [backlog](backlog.md) B-001/B-002.

### 12.2. Технические

- `Unsafe.Read<T>` (aligned-вариант) на unaligned-адресе →
  penalty, на ARM — падение. В бенчах — `Unsafe.ReadUnaligned<T>`;
  `Unsafe.Read<T>` — только с align 16/64.
- Pinned T[] блокирует GC → GC pause — в backlog (B-001);
  в основном прогоне смотрим ns/op и RSS.
- `Cast<byte, T>` требует layout-совместимости.
- `BinaryReader` над FileStream аллоцирует буфер — часть baseline.
- JSON в arena: `Utf8JsonWriter` требует `IBufferWriter<byte>`,
  обёртка добавляет виртуальные вызовы.
- Thread-local arena: `[ThreadStatic]` vs `ThreadLocal<Arena>` —
  разная цена.
- Shared arena в Parallel.For: нужен lock-free bump или
  thread-safe, иначе contention.
- `PathPool` уже хорошо работает — arena может не победить.

### 12.3. Семантические

- Arena держит память до `Reset`/`Dispose` — для серверного
  сценария важно.
- `ArrayPool` возвращает буфер не обнулённым — security-нюанс.
- `MemoryPool<T>` может внутри аллоцировать массивы — GC
  никуда не уходит.
- `Span<T>` — `ref struct`, нельзя в поле класса / async.

## 13. Что вне scope (для будущих PoC)

- Multi-tenancy и per-tenant арены.
- Плагины (AssemblyLoadContext, native ABI).
- Shared memory / mmap как IPC.
- ARM-специфичные оптимизации.
- Incremental solving с persistent arena.
- Benchmarking на реальных данных netgraph.
- Промышленная библиотека arena (публикация в NuGet).

Актуальный список с оценками — в [backlog](backlog.md).

## 14. Как запускать

```bash
# Сборка
dotnet build -c Release

# Тесты
dotnet test -c Release

# PoC-1 (полный прогон)
dotnet run -c Release --project benchmarks/ArenaBench.Serialization.Bench

# PoC-1 (фильтр)
dotnet run -c Release --project benchmarks/ArenaBench.Serialization.Bench \
  -- --filter *T2Structured*

# PoC-2 (полный прогон)
dotnet run -c Release --project benchmarks/ArenaBench.Allocator.Bench

# CI (быстрый прогон, оба PoC)
dotnet run -c Release --project benchmarks/ArenaBench.Serialization.Bench \
  -- --job short
dotnet run -c Release --project benchmarks/ArenaBench.Allocator.Bench \
  -- --job short
```

## 15. Артефакты

По завершении PoC в репозитории:

- `docs/poc-1-serialization.md` — таблицы, графики, выводы.
- `docs/poc-2-allocator.md` — таблицы, графики, выводы.
- `docs/summary.md` — общий вывод и рекомендации
  для netgraph-optimizer.
- `docs/methodology.md` — как воспроизвести, как читать
  результаты, ограничения.
- Весь код PoC — для повторных экспериментов.

## 16. Лицензия

MIT — как в netgraph-optimizer.

## 17. Ссылки

- netgraph-optimizer — https://github.com/cherninkiy/netgraph-optimizer
- BenchmarkDotNet — https://benchmarkdotnet.org
- .NET NativeMemory — https://learn.microsoft.com/dotnet/api/system.runtime.interopservices.nativememory
- MemoryMarshal — https://learn.microsoft.com/dotnet/api/system.runtime.interopservices.memorymarshal