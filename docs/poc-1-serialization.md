# PoC-1: Serialization — результаты

> Вопрос PoC-1 ([concept.md](concept.md), 5.1): какой способ
> сериализации/десериализации быстрее и экономнее на интеграциях, и даёт ли
> arena выигрыш?

**Статус: предварительные числа.** Прогон выполнен с `--job short`
(smoke-уровень: 3 warmup + 3 итерации, короткий launch) на рабочей машине, а не
на выделенной тишине, как требует [concept.md](concept.md) 7.4. Числа годятся
для **порядка величин и отбора гипотез**, не для финальных таблиц: для них нужен
полный прогон на выделенной машине (команды — в разделе «Как воспроизвести»).

## 1. Что измерялось

Матрица из [concept.md](concept.md) 5.1, три семейства:

| Семейство | Формат | Диапазон | Что сравнивается |
|---|---|---|---|
| **T2** (`S01`–`S22`) | fixed-layout records 16/32/64/128/256 B | `S01`–`S09` — потоки и span, `S10`–`S20` — arena, `S21`/`S22` — variable-length | способ чтения и память под ним |
| **T1** (`V01`–`V08`) | variable-length frames (8 B header + 16..128 B payload) | 256 кадров (~20 KiB) | BinaryReader / Span / Unsafe / Pipelines / arena / carry |
| **T3** (`C01`–`C10`) | text (CSV-строки) и JSON | 512 строк / 64 сообщения | span-парсинг, Utf8JsonReader/Writer, JsonSerializer, ручная запись |

Общие свойства прогона:

- **Генерация данных детерминированная, без RNG**: каждый формат задаёт
  `Create(index)` — фиксированные формулы (seed 42 из README здесь не нужен,
  случайности нет вообще). Один и тот же образ читают все стратегии.
- **Проверка корректности — отпечаток**: каждая стратегия возвращает сумму
  отпечатков записей/кадров/сообщений, поэтому (а) числа сравнимы,
  (б) JIT не выбрасывает чтения как мёртвый код, (в) расхождение в смещениях,
  порядке полей или байт-порядке видно сразу. Инвариант закреплён тестами
  (`ArenaBench.Serialization.Tests`).
- **Арена** — `ArenaBench.Core` (ADR-001), выравнивание задаётся на аллокацию
  (`AllocPtr(size, align)`): `S10/S11/S12` читают одну и ту же нагрузку при
  align 1/16/64.
- **`Allocated`** — управляемые байты от `MemoryDiagnoser`. Для arena и
  `NativeMemory` он равен нулю **по определению** (unmanaged-память диагносер не
  видит), RSS в этом прогоне не собирался ([concept.md](concept.md) 7.1).

## 2. Окружение прогона

```
BenchmarkDotNet v0.15.8, Windows 10 (10.0.19045.6456/22H2/2022Update)
AMD Ryzen 7 4800H with Radeon Graphics 2.90GHz, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.400
  [Host]   : .NET 10.0.11, X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.11, X64 RyuJIT x86-64-v3
Job=ShortRun  IterationCount=3  LaunchCount=1  WarmupCount=3
```

Диагностеры: `MemoryDiagnoser` + `ThreadingDiagnoser` (обязательные по
[concept.md](concept.md) 7.3), настроены один раз в `BenchmarkConfig`.

## 3. Как воспроизвести

```bash
# Сборка и тесты
dotnet build -c Release
dotnet test -c Release

# Один класс из матрицы (пример: 16 B ladder, S01-S20)
dotnet run -c Release --project benchmarks/ArenaBench.Serialization.Bench \
  -- --filter *T2ServiceRecord16Bench* --job short

# T1 и T3
dotnet run -c Release --project benchmarks/ArenaBench.Serialization.Bench \
  -- --filter *T1StreamBench* --job short
dotnet run -c Release --project benchmarks/ArenaBench.Serialization.Bench \
  -- --filter *T3TextBench* --job short

# Финальные числа (выделенная машина, без фоновой нагрузки)
dotnet run -c Release --project benchmarks/ArenaBench.Serialization.Bench -- --filter *
```

Отчёты каждого класса BDN складывает в
`BenchmarkDotNet.Artifacts/results/*-report-github.md`.

## 4. Ограничения прогона

- `--job short` — мало итераций: разброс по быстрым кейсам (десятки–сотни нс)
  заметен, выводы делаются по кратным различиям, а не по единицам процентов.
- Машина не выделенная: фоновые процессы и турбо-частоты влияют на числа.
- **Не измерялись** (вне scope, [backlog.md](backlog.md)): GC pause (B-001),
  pinning time (B-002), cache miss / branch mispredict через PMU (B-003),
  contention (B-004), ARM64 (B-005).
- Серии **R01–R04** (`SourcesBench`, чистая цена чтения источника без парсинга)
  в этот прогон не входят: они нужны, чтобы отделить цену `Stream.Read` от цены
  парсинга, и остаются на следующий шаг.

## 5. T2: fixed-layout records (S01–S22)

Одна итерация читает **256 записей** (16 B → 4 KiB, 256 B → 64 KiB). Baseline —
`S01` (BinaryReader поверх MemoryStream) внутри своего класса, `Ratio` — доля от
baseline: меньше 1.00 — быстрее.

### 5.1. Крайние точки лестницы (16 B и 256 B)

| Method | 16 B, ns | Ratio | 256 B, ns | Ratio |
|---|---:|---:|---:|---:|
| S01 MemoryStream + BinaryReader | 4 175.6 | 1.00 | 16 828 | 1.00 |
| S02 FileStream + BinaryReader | 10 617.4 | 2.54 | 66 894 | 3.98 |
| S03 NetworkStream (loopback) + BinaryReader | 559 706.9 | 134.04 | 1 648 725 | 97.97 |
| S04 UnmanagedMemoryStream + BinaryReader | 10 135.3 | 2.43 | 29 225 | 1.74 |
| S05 Span + BinaryPrimitives (LE) | 1 659.1 | 0.40 | 3 186 | 0.19 |
| S06 Span + BinaryPrimitives (BE) | 1 554.9 | 0.37 | 3 095 | 0.18 |
| S07 Span + MemoryMarshal.Read | 1 486.3 | 0.36 | 2 746 | 0.16 |
| S08 Span + Unsafe.ReadUnaligned | 1 437.3 | 0.34 | 2 815 | 0.17 |
| S09 Span + Cast\<T\> (bulk view) | 518.9 | 0.12 | 1 335 | 0.08 |
| S10 Arena tight (align 1) + primitives | 1 535.4 | 0.37 | 2 815 | 0.17 |
| S11 Arena align 16 + primitives | 1 494.9 | 0.36 | 3 001 | 0.18 |
| S12 Arena align 64 + primitives | 1 464.0 | 0.35 | 2 961 | 0.18 |
| S13 Arena + MemoryMarshal.Read | 1 526.7 | 0.37 | 2 659 | 0.16 |
| S14 Arena + Unsafe.ReadUnaligned | 1 545.5 | 0.37 | 2 614 | 0.16 |
| S15 Arena + Cast\<T\> | 640.7 | 0.15 | 1 306 | 0.08 |
| S16 Arena через Memory\<byte\> + primitives | 1 501.3 | 0.36 | 2 785 | 0.17 |
| S17 Arena + UnmanagedMemoryStream + BinaryReader | 9 770.3 | 2.34 | 29 339 | 1.74 |
| S18 Arena → ArrayPool staging copy + primitives | 1 596.4 | 0.38 | 3 895 | 0.23 |
| S19 Arena + explicit BE | 1 594.8 | 0.38 | 3 028 | 0.18 |
| S20 Arena + zeroed region + copy + primitives | 1 626.6 | 0.39 | 4 636 | 0.28 |
| S21 Arena + variable-length frames (span) | 545.0 | — | — | — |
| S22 Arena + variable-length frames (BinaryReader) | 9 870.6 | — | — | — |

`Allocated` во всех кейсах S01–S22 — **0 B/op**: scratch-буфер и `ArrayPool`
переиспользуются, а arena/unmanaged-память диагносер не видит по определению.

### 5.2. Тренд по размеру записи

| Размер | S01 memory+BR | S05 span | S09 Cast | S11 arena a16 | S15 arena Cast | S17 arena мост |
|---|---:|---:|---:|---:|---:|---:|
| 16 B | 4 176 | 1 659 | 519 | 1 495 | 641 | 9 770 |
| 32 B | 5 964 | 1 840 | 612 | 1 856 | 629 | 15 620 |
| 64 B | 8 720 | 1 806 | 692 | 1 785 | 682 | 17 686 |
| 128 B | 11 052 | 2 215 | 1 011 | 2 212 | 1 000 | 26 776 |
| 256 B | 16 828 | 3 186 | 1 335 | 3 001 | 1 306 | 29 339 |

Величины линейны по числу байт (256 записей на итерацию): `S01` — 16.3 ns на
16-байтовую запись и 65.7 ns на 256-байтовую, `S09` — 2.0 ns и 5.2 ns
соответственно.

### 5.3. Наблюдения по T2

1. **Arena как память под парсинг не выигрывает.** `S05` (`byte[]`) и `S11`
   (arena align 16) различаются на ±10%, причём знак меняется от размера
   (1 659 против 1 495 ns при 16 B; 3 186 против 3 001 ns при 256 B). Оба пути
   читают линейный span, разницы в машинном коде нет — подтверждение гипотезы
   concept.md 11.3 «arena не оправдана для homogeneous bulk».
2. **Alignment не влияет на последовательный парсинг.** `S10`/`S11`/`S12` в
   пределах шума на всех размерах (при 64 B: 1 788 / 1 785 / 1 784 ns).
   Гипотезы «align 64 при size 64 даёт +50–100%» и «align 64 при size 16 теряет
   30–50%» **не подтвердились** для sequential-чтения: запись читается подряд, и
   кэш-линия не становится узким местом. Проверять это надо на «случайном доступе
   к отдельным полям» плюс PMU-метриками (backlog B-003).
3. **Struct view быстрее полей, bulk view быстрее всех.** `S07`/`S08`/`S13`/`S14`
   (0.16–0.37×) экономят повторные чтения полей; `S09`/`S15`
   (`MemoryMarshal.Cast`, 0.08–0.15×) не делают ни копий, ни повторных смещений —
   **в 6–13 раз быстрее потока**, 2–5 ns на запись.
4. **BinaryReader — самый медленный способ чтения** (2.3–4× медленнее baseline при
   прочих равных): `S01`–`S04` выигрывают у span-вариантов только тогда, когда
   узкое место — сам источник (файл, сеть).
5. **UnmanagedMemoryStream как мост — дорого.** `S04`/`S17` медленнее
   MemoryStream-варианта в 1.7–2.4×, то есть «мост в BCL» чтение не ускоряет.
   При этом `S18` (скопировать арену в pooled `byte[]` и парсить span-ом)
   **в 6 раз быстрее** `S17` при 16 B (1 596 против 9 770 ns) и в 7.5 раз при
   256 B (3 895 против 29 339 ns): если данные нужны managed-коду, копия дешевле
   чтения через `UnmanagedMemoryStream`.
6. **Endianness-своп не виден**: `S06` и `S19` (explicit BE) в пределах шума от
   LE-вариантов (1 555 против 1 659 ns при 16 B). Гипотеза «−5–10%» не
   подтвердилась — на x64 `bswap` тонет в стоимости доступа к памяти.
7. **Zeroing дорог только на крупных записях**: `S20` (`AllocSpanZeroed` + copy) —
   0.39× от baseline при 16 B (1 627 ns, из них ~130 ns поверх `S11`) и 0.28× при
   256 B (4 636 против 3 001 ns у `S11`, то есть +55% на 64 KiB).
8. **Variable-length:** обход кадров span-ом (`S21`, 545 ns на 256 кадров) против
   BinaryReader через мост (`S22`, 9 871 ns) — та же картина, что в фиксированных
   записях.

## 6. T1: variable-length frames (V01–V08)

256 кадров (8 B header + 16..128 B payload, ~20 KiB), baseline — `V01`.

| Method | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| V01 MemoryStream + BinaryReader | 5 604.2 ns | 1.00 | — |
| V02 Span.Slice + BinaryPrimitives | 501.4 ns | 0.09 | — |
| V03 Span + Unsafe.ReadUnaligned | 494.7 ns | 0.09 | — |
| V04 Pipelines (PipeReader + SequenceReader) | 3 516.7 ns | 0.63 | 944 B |
| V05 Arena tight (align 1) + primitives | 498.4 ns | 0.09 | — |
| V06 Arena align 64 + primitives | 499.3 ns | 0.09 | — |
| V07 Carry (managed buffer), 13 B за чтение | 8 719.2 ns | 1.56 | — |
| V08 Carry (arena buffer), 13 B за чтение | 8 661.2 ns | 1.55 | — |

1. **Span-обход кадров — в 11 раз быстрее BinaryReader** и без аллокаций
   (`V02`/`V03`/`V05`/`V06` ≈ 500 ns на 256 кадров ≈ 2 ns на кадр).
2. **Pipelines работоспособен, но не бесплатен**: 3 517 ns (0.63×) и **944 B на
   вызов** — цена буферов и сегментов `PipeReader`. Для hot path без реальной
   асинхронности он проигрывает span-чтению в 7 раз.
3. **Alignment на кадрах не проявляется** (`V05` 498 против `V06` 499 ns) — тот же
   вывод, что в T2.
4. **Carry — самая дорогая стратегия** (1.55–1.56× от baseline, 8.7 µs): чтение по
   13 байт за раз с компактированием буфера стоит в 17 раз дороже готового буфера
   в памяти. Managed-буфер и арена дают одинаковый результат (8 719 против
   8 661 ns, разница в шуме): узкое место — не аллокатор, а цена `Read` и логика
   carry.

## 7. T3: text и JSON (C01–C10)

Text-нагрузка — 512 строк, JSON-нагрузка — 64 сообщения; writer'ы измеряются на
**одном** сообщении. Baseline — `C01` (span-парсинг текста).

| Method | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| C01 Text: span + SearchValues + Utf8Parser | 22 504.7 ns | 1.000 | — |
| C02 Text: то же из арены | 22 649.2 ns | 1.006 | — |
| C03 JSON: Utf8JsonWriter → ArrayBufferWriter | 302.8 ns | 0.013 | — |
| C04 JSON: Utf8JsonWriter → arena IBufferWriter | 317.9 ns | 0.014 | — |
| C05 JSON: Utf8JsonReader (byte[]) | 38 120.1 ns | 1.694 | — |
| C06 JSON: Utf8JsonReader (арена) | 34 977.2 ns | 1.554 | — |
| C07 JSON: JsonSerializer → string | 380.2 ns | 0.017 | 272 B |
| C08 JSON: JsonSerializer → byte[] | 331.1 ns | 0.015 | 152 B |
| C09 Text: ручная запись в арену | 155.2 ns | 0.007 | — |
| C10 Text: GetString + Split + Parse | 70 964.2 ns | 3.153 | 145 272 B |

1. **Arena под текст/JSON не выигрывает** — ни в парсинге (`C01` против `C02`:
   +0.6%), ни в чтении JSON (`C05` против `C06`: −8%, в пределах шума). Гипотеза
   concept.md 11.1 «T3 JSON в arena ≈ ArrayBufferWriter» подтверждается: арена не
   даёт ни выигрыша, ни заметной потери.
2. **Мост `IBufferWriter` в арену стоит ~5%** (`C03` 303 ns против `C04` 318 ns) и
   **не аллоцирует**: предупреждение concept.md 12.2 («обёртка добавляет
   виртуальные вызовы») верно, но цена мала — `Utf8JsonWriter` и так работает
   через интерфейс.
3. **Ручная запись быстрее JSON-писателя вдвое** (`C09` 155 ns против `C03`
   303 ns), но она и делает меньше: без экранирования, проверок и состояния
   writer'а. Для известного ASCII-формата это оправданный путь, для произвольных
   данных — нет.
4. **Managed-путь разгромно проигрывает** (`C10` — 3.15× медленнее span-парсинга
   и **145 KB аллокаций** на 512 строк ≈ 284 B на строку). Главный отрицательный
   результат T3: `GetString` + `Split` — не вариант для hot path.
5. **`JsonSerializer` на одном сообщении дешевле ручного reader'а** в пересчёте на
   сообщение (`C07`/`C08` ≈ 0.3–0.4 µs против ≈ 0.6 µs у `C05`), но платит
   аллокациями (152–272 B на вызов).
6. **JSON-чтение дороже текстового** (≈ 596 ns на сообщение против ≈ 44 ns на
   строку): `Utf8JsonReader` проверяет структуру и типы, а не режет по
   разделителям.

## 8. Гипотезы (concept.md 11.1) против измерений

Замеры — 16 B, если не сказано иное; «быстрее» = больше раз, чем baseline `S01`.

| Гипотеза | Ожидалось | Измерено | Итог |
|---|---|---|---|
| BinaryReader (MemoryStream) | 1× | 1.00 | ✅ baseline |
| BinaryReader (FileStream) | 0.2–0.5× | 0.39× (2.54× медленнее) | ✅ |
| BinaryReader (NetworkStream) | 0.1–0.3× | 0.007× (134× медленнее) | ⚠️ много хуже: в эмуляцию входит `Send` на каждую итерацию (R-серия это разделит) |
| UnmanagedMemoryStream + BinaryReader | 0.5–0.8× | 0.41× (2.43× медленнее) | ✅ |
| BinaryPrimitives (byte[]) | 10–20× быстрее | **2.5× быстрее** (0.40×) | ❌ завышено |
| arena + MemoryMarshal.Read (align 16) | 15–25× быстрее | **2.7× быстрее** (0.37×) | ❌ завышено |
| arena + Unsafe.Read (align 64) | 18–28× быстрее | **2.9× быстрее** (0.35×) | ❌ завышено |
| arena + Cast\<T\> (align 64) | 20–30× быстрее | **8.0× быстрее** (0.12×) | ❌ завышено, но лучший результат матрицы |
| Align 64 при size 64 | +50–100% | в пределах шума | ❌ не подтвердилась |
| Align 64 при size 16 | −30–50% | в пределах шума | ❌ не подтвердилась |
| Endianness BE vs LE | −5–10% | в пределах шума | ❌ не подтвердилась |
| T3 JSON в arena | ≈ ArrayBufferWriter | +5%, 0 аллокаций | ✅ |
| Arena против `byte[]` в парсинге | (не заявлено прямо) | паритет ±10% | ➖ новый факт |

## 9. Критерии успеха PoC-1 (concept.md 10.1)

| Критерий | Статус |
|---|---|
| Численный ответ «BinaryReader vs Span-based» | ✅ 2.5× (примитивы) … 8× (`Cast`) на одной итерации; на кадры — 11× |
| Численный ответ «align 64 vs tight» | ✅ для sequential-парсинга разницы нет; PMU-прогон для доступа к полям остаётся в backlog B-003 |
| «Где arena выигрывает, где нет» по T1/T2/T3 | ✅ в сериализации выигрыша нет нигде: паритет с `byte[]` при равных стратегиях, проигрыш там, где начинается мост в BCL |
| Решение по netgraph-optimizer | ⏳ предварительное, см. 10 |
| Отрицательные результаты зафиксированы | ✅ T3/managed-путь, alignment, endianness, Pipelines, carry |

## 10. Предварительное решение для netgraph-optimizer

До PoC-2 и полного прогона картина такая:

- **Hot-path структуры читать span-ом, а не через `BinaryReader`.** Минимум
  потерь: `MemoryMarshal.Cast<T>` для однородных массивов записей и
  `Unsafe.ReadUnaligned<T>` / `MemoryMarshal.Read<T>` там, где нужен доступ к
  отдельным записям. `BinaryReader` — для кода вне hot path.
- **Арена под сериализацию ничего не добавляет**: парсинг из арены и из `byte[]`
  одинаков, а мост в BCL (`UnmanagedMemoryStream` + `BinaryReader`) дороже копии.
  Если данные уже в арене, дешевле один раз скопировать их в pooled `byte[]` и
  парсить span-ом (S18 против S17).
- **Выравнивание — не «бесплатная» оптимизация**: на sequential-чтении эффекта не
  видно, поэтому align 64 стоит брать только там, где он измерен (кэш-линия,
  SIMD, PMU — B-003).
- **Текст и JSON — не тот сценарий**: arena не нужна; `GetString`/`Split` не
  использовать; `Utf8JsonWriter` в арену допустим без потерь, но и без выигрыша.
- **Что остаётся за ареной — границы и аллокации, а не парсинг.** Это проверяет
  PoC-2: P/Invoke, native callbacks, alloc/free, B&B rollback.

## 11. Что дальше

1. **R01–R04** (`SourcesBench`): чистая цена `Stream.Read` по источникам —
   отделит «дорогой источник» от «дорогого парсинга». Сейчас видно, что `S03`
   доминируется отправкой данных в loopback на каждую итерацию, а не чтением.
2. **Полный прогон** на выделенной машине (`--job default`, без фоновой нагрузки)
   для финальных таблиц и графиков.
3. `docs/methodology.md`: как воспроизводить, как читать отчёты, ограничения.
4. PoC-2 (allocator) и `docs/summary.md` с итоговым решением.
