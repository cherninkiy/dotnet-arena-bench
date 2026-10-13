# ADR-002: Native Side

**Статус:** Accepted
**Дата:** 2026-10-13
**Контекст:** dotnet-arena-bench, PoC-2 (границы: I01–I08)
**Связанные документы:** [concept.md](../concept.md), [ADR-001: Arena Design](ADR-001-arena-design.md)

---

## Контекст

PoC-2 покрывает сценарии на границах системы:
P/Invoke (I01–I03), native callbacks (I04–I05), egress (I06–I07),
mmap (I08). Для честного сравнения «arena vs pinned managed array»
нужна **нативная сторона** — код, который реально работает с `IntPtr`.

Проблема: включение C/C++ shim в репозиторий усложняет CI
(кросс-платформенная сборка `.so`/`.dll`/`.dylib`), требует
установки toolchain и ломает «простой .NET-only CI».

Нужно решение: **как эмулировать нативную сторону**, не теряя
честности сравнения.

---

## Решения

### D1. Никакого C/C++ кода в репозитории

**Решение:** PoC **не содержит** native-проекта на C/C++/Rust.
Вся native-сторона эмулируется через .NET.

**Альтернативы:**

| Вариант | Почему отклонён |
|---|---|
| Мини C-библиотека + сборка в CI | Кросс-платформенная сборка `.so`/`.dll`/`.dylib` усложняет CI, требует toolchain на каждой платформе |
| Prebuilt `.so`/`.dll` в репозитории | Не воспроизводимо, вопросы безопасности, привязка к архитектуре |
| NativeAOT-компонент внутри репо | Излишне для PoC, добавляет сложности сборки |
| Rust/C++ через `cargo`/`cmake` в CI | Удваивает сложность CI ради PoC |

**Обоснование:**
- Цель PoC — **сравнить arena vs managed** на границе, а не
  тестировать C ABI. Обе стороны работают через один и тот же
  механизм (`IntPtr`), поэтому сравнение остаётся честным.
- Простой CI — важное нефункциональное требование: только .NET SDK.
- Все 8 I-сценариев покрываются .NET-средствами (см. D2, D3).

**Последствия:**
- CI: `dotnet build` + `dotnet test`, без `build-essential`, `gcc`,
  `clang`, `cmake`.
- Native-специфичные ошибки (ABI mismatch, layout) **не тестируются** —
  это явно вне scope.
- Все результаты относятся к **семантике границы**, а не к
  поведению конкретной C-библиотеки.

---

### D2. P/Invoke через libc

**Решение:** для сценариев «мы пишем / нативный читает» (I01),
«нативный пишет / мы читаем» (I02), «inout» (I03), egress (I06–I07)
используются функции **libc** через P/Invoke:

- `memcpy` — нативный код читает из нашей памяти.
- `memset` — нативный код пишет в нашу память.
- `memcmp` — сравнение буферов (для верификации).

**Кросс-платформенная резолюция библиотек:**

| ОС | Библиотека | Имя для `LibraryImport` |
|---|---|---|
| Linux | `libc` | `libc` |
| macOS | `libSystem.dylib` (симлинк на `libSystem.B.dylib`) | `libSystem` |
| Windows | `ucrtbase.dll` | `ucrtbase` |

**Оговорка:** голый `libc` **не** резолвится на современных macOS
(`DllNotFoundException`, dotnet/sdk #6642) — поэтому macOS идёт через
явный `libSystem`.

Резолюция — через `RuntimeInformation.IsOSPlatform` в `LibcResolver.cs`
(имена библиотек + пробы ОС). Так как `LibraryImport` требует
compile-time константу, каждая функция объявлена по одному разу на ОС
(`Linux`/`Mac`/`Win`), а выбор между ними делается **один раз** при
инициализации типа — в `NativeMethods` в функциональный указатель
(`delegate* managed<...>`). На hot path границы per-call OS-dispatch нет.

**Альтернативы:**

| Вариант | Почему отклонён |
|---|---|
| P/Invoke на `kernel32`/`ntdll` (Windows-only) | Не кросс-платформенно |
| P/Invoke на `libcmt`/CRT | Привязка к Visual Studio toolchain |
| Своя C-функция `do_work` | Требует C-кода (см. D1) |

**Обоснование:**
- `memcpy`/`memset` — это **настоящий native-код** (не эмуляция),
  выполняемый ОС. Сравнение arena vs pinned на этих функциях
  полностью честное.
- `memcpy` — реалистичный сценарий: передача буфера в нативную
  библиотеку, которая копирует/читает данные.
- Нет зависимости от C-toolchain.

**Ограничения:**
- Не покрывает структуры by value (только указатели).
- Не покрывает COM, C++ vtables, custom ABI.
- `memcpy`/`memset` — простые функции без сложной логики; для
  более реалистичных сценариев нужен C-код, но это вне scope.
- **Оговорка:** на больших размерах (1 MB) стоимость `memcpy`
  (RAM bandwidth) доминирует над ценой P/Invoke-транзиции
  (~2–5 ns); на мелких (32 B) — наоборот, транзиция доминирует.
  Результаты I-бенчей читаются **по размерам**, а не одной цифрой.

**Последствия:**
- I01, I02, I03, I06, I07 — покрыты.
- `NativeMethods.cs` содержит `LibraryImport`-декларации.
- Тесты проверяют корректность: пишем в arena → `memcpy` → читаем.

---

### D3. Native callbacks через `[UnmanagedCallersOnly]`

**Решение:** для сценариев «нативный код зовёт managed» (I04)
и «много вызовов подряд» (I05) используется
`[UnmanagedCallersOnly]` + функциональные указатели
`delegate* unmanaged<...>`.

Схема:

```csharp
[UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
static int ProcessBuffer(IntPtr data, int size) { ... }

// вызов через указатель:
delegate* unmanaged[Cdecl]<IntPtr, int, int> fp = &ProcessBuffer;
int result = fp(ptr, size);
```

**Альтернативы:**

| Вариант | Почему отклонён |
|---|---|
| `Marshal.GetFunctionPointerForDelegate` (классика) | Медленнее, требует держать делегат живым, риск GC |
| `[DllImport]` с callback-параметром | Требует C-кода, который вызовет callback |
| `NativeAOT`-компонент | См. D1 |

**Обоснование:**
- `[UnmanagedCallersOnly]` — **современный** механизм .NET 5+,
  даёт **реальный** unmanaged ABI (не эмуляцию на уровне JIT).
- Функциональные указатели работают на x64 и ARM64.
- Полностью managed-код, не нужен C.
- Функционально **эквивалентно** native callback: managed-метод
  вызывается через unmanaged-ABI, стек и регистры — как при
  настоящем native-вызове.

**Оговорка:** измеряется стоимость **одного перехода**
managed → unmanaged ABI → managed. Нативный цикл-драйвер
(библиотека итерирует и вызывает fp N раз) **не эмулируется**:
в бенче цикл итераций — managed, fp вызывается по одному разу
за итерацию. Для цели PoC — сравнение arena vs copy — этого
достаточно: обе стороны проходят одинаковый путь.

**Ограничения:**
- Метод должен быть `static`, не в generic-классе.
- Нельзя использовать `ref struct`, `Span<T>` напрямую в параметрах —
  только `IntPtr`.
- Для сложных сценариев (делегаты, замыкания) не подходит.

**Последствия:**
- I04, I05 — покрыты.
- `ManagedCallbacks.cs` содержит `[UnmanagedCallersOnly]`-методы.
- Тесты проверяют корректность: arena buffer → callback → результат.

---

### D4. mmap — через BCL `MemoryMappedFile`

**Решение:** для I08 (shared memory) используется
`System.IO.MemoryMappedFiles.MemoryMappedFile` из BCL.
Arena размещается поверх `MemoryMappedViewAccessor`.

**Альтернативы:**

| Вариант | Почему отклонён |
|---|---|
| P/Invoke на `mmap`/`CreateFileMapping` | Дублирует BCL, платформо-зависимо |
| Свой C-shim поверх mmap | Требует C-кода (см. D1) |
| `NativeMemory.AlignedAlloc` без mmap | Не даёт shared memory между процессами |

**Обоснование:**
- `MemoryMappedFile` — стандартный API, работает на Windows/Linux/macOS.
- Покрывает use case «arena поверх shared memory» без native-кода.
- Сравнение `arena-over-mmap` vs `MemoryMappedViewAccessor`
  остаётся честным.

**Ограничения:**
- Нет контроля над `madvise`, `MAP_HUGETLB`, page alignment —
  это вне scope.
- Ограничения BCL по размеру view (зависит от ОС).

**Последствия:**
- I08 — опциональный бенч; если не успеваем, выносим в следующий PoC.
- Arena должна уметь работать поверх произвольного `IntPtr`
  (не только `NativeMemory.AlignedAlloc(64, ...)` из
  [ADR-001, D1](ADR-001-arena-design.md)) — это **влияет на дизайн
  arena**.

---

### D5. Что НЕ покрывается

Явно **вне scope** этого PoC:

- **Настоящий C/C++ shim** — ABI-совместимость, layout, выравнивание
  структур — не тестируются.
- **Exotic ABI** — COM, C++ vtables, `stdcall`/`thiscall`, custom
  calling conventions.
- **Structs by value** сложных размеров — только указатели.
- **GC pause от pinned `T[]` не измеряется в основном прогоне** —
  требует фонового GC-нагрузочного harness. Если решение
  «arena vs pinned» окажется близким по ns/op — см.
  [backlog](../backlog.md) B-001.
- **Pinning time не измеряется** — требует EventPipe-анализа.
  См. [backlog](../backlog.md) B-002.
- **Реальные сетевые вызовы** — `NetworkStream` эмулируется через
  loopback + `Socket` или `PipeStream`.
- **`libuv`/`io_uring`/`epoll`** — асинхронные примитивы — вне scope.
- **CUDA/OpenCL/GPU** — вне scope.
- **NativeAOT interop** — отдельная задача.

---

## Сводка решений

| # | Решение | Покрывает |
|---|---|---|
| D1 | Никакого C/C++ кода | всё |
| D2 | P/Invoke на libc (`memcpy`, `memset`, `memcmp`) | I01, I02, I03, I06, I07 |
| D3 | `[UnmanagedCallersOnly]` + функциональные указатели | I04, I05 |
| D4 | BCL `MemoryMappedFile` | I08 (опционально) |
| D5 | Явные ограничения scope | — |

---

## Последствия для CI

CI остаётся простым:

```yaml
jobs:
  build:
    runs-on: ${{ matrix.os }}
    strategy:
      matrix:
        os: [ubuntu-latest, windows-latest, macos-latest]
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.x'
      - run: dotnet build -c Release
      - run: dotnet test -c Release
      - run: dotnet run -c Release --project benchmarks/ArenaBench.Serialization.Bench -- --job short
      - run: dotnet run -c Release --project benchmarks/ArenaBench.Allocator.Bench -- --job short
```

**Никаких** `apt-get install build-essential`, `brew install cmake`,
`choco install visualstudio-build-tools`.

**Почему short bench в CI:** `--job short` — это smoke-прогон
для детекции регрессий, не для сбора метрик. Не требует
C-toolchain, поэтому простота CI сохраняется. Полные прогоны —
локально или отдельным workflow по расписанию.

---

## Последствия для concept.md

- Раздел 5.2 concept.md, абзац «Нативная сторона» — ссылается
  на этот ADR.
- Раздел «Риски» — обновлён с учётом D5.
- Раздел «Что вне scope» — дополнен D5.
- План работ, День 4 — уточнён: I-бенчи через libc
  и `[UnmanagedCallersOnly]`.

---

## Последствия для Arena Design (ADR-001)

D4 (mmap через BCL) требует, чтобы arena умела работать поверх
**произвольного `IntPtr`**, а не только своей памяти. Это значит:

- `Arena` должна иметь конструктор, принимающий `IntPtr` + размер.
- `SafeArenaHandle` должен различать «владеет памятью» и
  «обёртывает чужую».
- Это **внесено в ADR-001** как уточнение D1.

---

## Ссылки

- [concept.md](../concept.md) — общая концепция PoC.
- [ADR-001: Arena Design](ADR-001-arena-design.md) — дизайн arena,
  включая требование работать поверх произвольного `IntPtr`.
- [.NET `UnmanagedCallersOnly`](https://learn.microsoft.com/dotnet/api/system.runtime.interopservices.unmanagedcallersonlyattribute)
- [.NET `LibraryImport`](https://learn.microsoft.com/ru-ru/dotnet/standard/native-interop/pinvoke)
- [BCL `MemoryMappedFile`](https://learn.microsoft.com/dotnet/api/system.io.memorymappedfiles.memorymappedfile)