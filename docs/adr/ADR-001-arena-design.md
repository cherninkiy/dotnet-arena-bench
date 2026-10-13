# ADR-001: Arena Design

**Статус:** Accepted
**Дата:** 2026-10-13
**Контекст:** dotnet-arena-bench, PoC-1 и PoC-2
**Связанные документы:** [concept.md](../concept.md), [ADR-002: Native Side](ADR-002-native-side.md)

---

## Контекст

PoC измеряет, даёт ли memory arena выигрыш в .NET для сценариев
сериализации и интеграции с внешними компонентами. Дизайн arena
напрямую влияет на результаты бенчей:

- **P/Invoke-сценарии (I01–I08)** требуют стабильных указателей —
  если arena перевыделяет память, `IntPtr`, отданный в нативный код,
  становится невалидным.
- **Parallel B&B (E04)** требует решения по thread safety — от него
  зависит, как выглядит thread-local vs shared arena.
- **Alignment-бенчи (T2)** требуют явного API выравнивания —
  auto-align недостаточно.

Без явной фиксации этих решений реализация начнётся с произвольного
дизайна, а бенчи станут невоспроизводимыми.

---

## Решения

### D1. Growth policy: single block, фиксированный размер

**Решение:** arena выделяет один непрерывный блок unmanaged-памяти
фиксированного размера в конструкторе. Не растёт. При переполнении —
`InvalidOperationException`.

**Альтернативы:**

| Вариант | Почему отклонён |
|---|---|
| Chunked (linked list) | Указатели нестабильны → ломает I01–I08 |
| Single block + realloc | Указатели становятся невалидны при росте |
| Virtual memory reservation (`mmap`/`VirtualAlloc`) | Сложность, платформо-зависимость, вне scope PoC |
| Рост «на лету» | Непредсказуемый lifetime, мешает измерениям |

**Обоснование:**
- I-бенчи (P/Invoke, callbacks, egress) требуют, чтобы `IntPtr`
  оставался валидным на всём протяжении вызова.
- Размер известен заранее: в бенчах — фиксированный (1KB–1MB),
  в микро-солвере — бюджет узлов B&B.
- Простота реализации: bump-pointer за O(1), без управления чанками.

**Последствия:**
- Все бенчи явно указывают размер arena.
- E04 (Parallel.For) использует arena per-thread фиксированного размера.
- Ошибка в оценке размера → exception, а не тихий рост.

### Уточнение от ADR-002-native-side

I08 (mmap) требует, чтобы `Arena` умела работать поверх **произвольного
`IntPtr`**, не только своей памяти. Поэтому в API добавляется:

```csharp
// Arena, обёртывающая внешний блок (например, mmap)
public Arena(IntPtr memory, nuint size, bool ownsMemory = false);
```

`SafeArenaHandle` различает владение: если `ownsMemory = false`,
память освобождает внешний владелец.

### Уточнение (от ревью): выравнивание базового блока

Базовый блок выделяется через `NativeMemory.AlignedAlloc(64, size)` —
**всегда 64-байтовое выравнивание базы**. Это гарантирует, что
bump-логика с align 16/64 даёт **абсолютное** выравнивание указателей,
а не относительное. Без этого alignment-ось бенчей (S10–S12)
невалидна.

Для внешнего блока (`Arena(IntPtr, ...)`) выравнивание базы
гарантируется владельцем памяти.

---

### D2. Thread safety: single-thread bump + ThreadLocal wrapper

**Решение:**
- `Arena` — single-thread bump без синхронизации.
- `ThreadLocalArena` — обёртка над `[ThreadStatic]` `Arena`
  для параллельных сценариев.
- Shared arena в Parallel.For эмулируется через `lock` **только
  в бенче** — как baseline для измерения цены contention.
- Lock-free shared arena — вне scope.

**Альтернативы:**

| Вариант | Почему отклонён |
|---|---|
| Lock-based shared arena | Медленно, contention съедает выигрыш; для baseline достаточно эмуляции в бенче |
| `Interlocked` bump | Не работает с `Mark`/`Release` (rollback требует синхронизации) |
| Lock-free general arena | Нерешаемая задача в общем случае (ABA, rollback); вне scope PoC |

**Обоснование:**
- Bump-allocator с `Mark`/`Release` **нельзя** сделать lock-free
  в общем случае: `Release` откатывает offset, что требует
  синхронизации с другими потоками.
- Для целей PoC достаточно показать: **thread-local arena ≫
  shared arena + lock** на parallel-нагрузке.
- Эмуляция shared arena через `lock` в бенче даёт честный baseline
  без production-сложности.

**Последствия:**
- E04 — три варианта: `ThreadLocalArena`, shared arena + lock, `PathPool`.
- M-бенчи — single-thread.
- Production-grade thread safety — вне scope.

---

### D3. Alignment API: auto + explicit

**Решение:** два API:

```csharp
// Auto-align по defaultAlign
Span<T> AllocSpan<T>(int count) where T : unmanaged;
IntPtr AllocPtr<T>(int count) where T : unmanaged;

// Explicit align
Span<byte> AllocBytes(int size, int align);
IntPtr AllocPtr(int size, int align);
```

`AllocSpan<T>` / `AllocPtr<T>` выравнивают по
`max(defaultAlign, естественное выравнивание примитивов)`.

**Уточнение (от реализации):** в BCL **нет** публичного `AlignOf<T>()`-API
(только proposal dotnet/runtime #2167), поэтому auto-align берёт
`max(defaultAlign, естественное выравнивание примитивов)` — точный
`typeof(T)`-switch, JIT-константа. Для составных unmanaged-типов
естественное выравнивание неизвестно — `defaultAlign` и есть контракт:
должен быть **не меньше** естественного выравнивания `T` (default 16
покрывает все типы PoC без явного layout-выравнивания); иначе —
explicit API.

`defaultAlign` — параметр конструктора арены, по умолчанию **16**.
Для alignment-бенчей (S10–S12) `defaultAlign = 64` задаётся явно
через конструктор, либо используется `AllocBytes(size, 64)` /
`AllocPtr(size, 64)`:

```csharp
var arena16 = new Arena(1 << 20);              // defaultAlign = 16
var arena64 = new Arena(1 << 20, defaultAlign: 64);
var span    = arena16.AllocSpan<Commodity32>(); // align = defaultAlign = 16
var buf     = arena16.AllocBytes(1024, 64);     // явный align 64
```

**Альтернативы:**

| Вариант | Почему отклонён |
|---|---|
| Только auto-align | Не покрывает T2-бенчи с align 16/64 |
| Только explicit align | Неудобно для типичных случаев |
| Default align = 8 | Не покрывает SIMD-128 |
| Default align = 64 | Тратит память на мелких структурах |

**Обоснование:**
- **Auto-align** — для типичных сценариев (`Span<Commodity32>`,
  CSR, M-бенчи).
- **Explicit align** — для T2-бенчей с alignment-осью
  (S10–S12, S15–S17).
- Default 16 — компромисс: покрывает `sizeof(T)` до 16,
  покрывает SIMD-128, не тратит много на padding для мелких типов.

**Последствия:**
- T2 alignment-бенчи используют explicit API.
- E01, E02, M-бенчи — auto API.
- Padding overhead документируется в результатах.

---

### D4. Rollback: `Mark` / `Release` за O(1)

**Решение:**

```csharp
ArenaMark Mark();
void Release(ArenaMark mark);
```

- `Mark()` снимает текущий offset, возвращает struct.
- `Release(mark)` восстанавливает offset.
- **Не поддерживает** out-of-order release.

**Обоснование:**
- Классический паттерн bump-allocator для B&B: войти в узел →
  аллокация → откат при возврате.
- O(1) — ключевое преимущество перед `PathPool` (возврат N буферов).
- Out-of-order release не нужен для scoped-паттернов (B&B, per-request).

**Последствия:**
- E03 (B&B rollback) использует `Mark`/`Release`.
- Тесты проверяют, что `Release` возвращает ровно то, что взял `Mark`.

---

### D5. Zeroing: выключен по умолчанию

**Решение:**
- `AllocSpan<T>()` / `AllocPtr<T>()` возвращают **не обнулённую** память.
- Отдельный API: `AllocSpanZeroed<T>()`.

**Обоснование:**
- Zeroing стоит времени; в hot path не нужен.
- Аналогия со `stackalloc` и `ArrayPool` (тоже не обнуляют).
- Явный `AllocSpanZeroed` делает намерение явным.

**Последствия:**
- A-бенчи сравнивают `AllocSpan` vs `AllocSpanZeroed` vs `ArrayPool`.
- Тесты проверяют, что `AllocSpanZeroed` возвращает нули.

---

### D6. Lifetime: ручной через `IDisposable` / `SafeHandle`

**Решение:**
- `Arena : IDisposable`.
- `SafeArenaHandle` для безопасного владения.
- **Без собственного финализатора на `Arena`.** Safety net —
  финализатор `SafeHandle`, который вызывает `ReleaseHandle()` при
  забытом `Dispose`. На hot path финализатор не участвует
  (срабатывает только при утечке). Без `GC.AddMemoryPressure`.

**Обоснование:**
- В hot path финализация вредна (непредсказуемые паузы).
- `GC.AddMemoryPressure` для PoC не нужен — arena живёт в рамках
  бенча.
- `SafeHandle` даёт частичную защиту от утечек без накладных расходов
  финализатора.

**Последствия:**
- Тесты на утечки обязательны.
- Пользователь отвечает за `Dispose`.
- При забытом `Dispose` память освободится через финализатор
  `SafeHandle`. Тест на утечки в `ArenaBench.Core.Tests` проверяет
  это через `GC.Collect` + `GC.WaitForPendingFinalizers`.

---

### D7. `Reset`: O(1), память не освобождает

**Решение:** `Reset()` возвращает offset в 0. Память остаётся
выделенной, содержимое — мусор.

**Обоснование:**
- Классический паттерн arena: одна аллокация на весь жизненный цикл,
  быстрое освобождение между батчами.
- O(1) — ключевое преимущество перед `ArrayPool` на batch-сценариях.

**Последствия:**
- A02 (batch alloc / Reset) — ключевой бенч.
- Не забывать про `Reset` в бенчах — иначе overflow.

---

### D8. Размер: фиксированный, не растёт

**Решение:** размер задаётся в конструкторе, `Free` доступен
для проверки перед аллокацией.

**Обоснование:**
- Следствие D1 (single block).
- `Free` позволяет бенчам избегать exception в hot path.

**Последствия:**
- Все бенчи явно указывают размер.
- `Free` используется в тестах для проверки корректности.

---

### D9. Контракт ArenaMemoryManager vs Reset/Dispose

**Решение:** арена **не отслеживает** активные `Memory<T>`.
Ответственность на вызывающем.

**Контракт:**
- `ArenaMemoryManager<T>` создаётся per-allocation и живёт не дольше,
  чем арена.
- `Reset()` / `Dispose()` инвалидируют все ранее выданные
  `Memory<T>` / `Span<T>` / `IntPtr`.
- Использование после `Reset` — **undefined behavior**
  (не exception, не падение — просто мусор).

**Обоснование:**
- Отслеживание активных аренд ломает zero-overhead цель arena.
- В hot path `Memory<T>` либо не используется (Span достаточно),
  либо создаётся и умирает в пределах scope.
- В B-бенчах (B02) `Memory<T>` создаётся и сразу используется —
  конфликта с `Reset` нет.

**Последствия:**
- Документировать в XML-комментариях `Arena.Reset` и `Arena.Dispose`.
- Тесты проверяют: `Reset` без активных `Memory<T>` — ок;
  `Reset` с активными `Memory<T>` — не тестируется (UB).
- В README и concept — предупреждение для пользователя.

---

## Сводка решений

| # | Решение | Влияет на |
|---|---|---|
| D1 | Single block, fixed size, стабильные указатели | I01–I08, E04, A01–A04 |
| D2 | Single-thread bump + ThreadLocal wrapper | E04, M, single-thread |
| D3 | Auto + explicit align, default 16 | T2, E01–E02, M |
| D4 | `Mark`/`Release` O(1), без out-of-order | E03 |
| D5 | Zeroing off, отдельный API | A01–A04 |
| D6 | Ручной lifetime, `SafeHandle` | тесты, M |
| D7 | `Reset` O(1) | A02 |
| D8 | Фиксированный размер, `Free` | все |
| D9 | `Memory<T>` без отслеживания аренд; UB после `Reset` | B02, тесты |

---

## Ограничения (вне scope)

Явно **вне scope** этого PoC:

- Chunked / realloc / mmap-based arena (динамический рост).
- Lock-free shared arena.
- Финализация и `GC.AddMemoryPressure`.
- Production-grade thread safety.
- Персистентность arena между запусками процесса.

---

## Последствия для concept.md

Разделы concept.md, которые ссылаются на дизайн arena:

- **Структура PoC** — упоминает `Arena`, `ThreadLocalArena`,
  `SafeArenaHandle`, `Mark`/`Release`, но без деталей.
- **Оси бенчей** — alignment-ось ссылается на D3.
- **План работ, День 0** — перечисляет компоненты arena
  для реализации.
- **ArenaMemoryManager (D9)** — контракт UB после `Reset`
  документируется в XML-комментариях.
- **Риски** — ссылаются на ограничения D1–D9.

Полные детали — в этом ADR.

---

## Ссылки

- [concept.md](../concept.md) — общая концепция PoC.
- [ADR-002: Native Side](ADR-002-native-side.md) — как эмулируется native-сторона для I-бенчей.