# Backlog

Задачи, явно **вне scope** текущего PoC (см. [concept.md](concept.md),
разделы «Не-цели» и «Метрики»), но потенциально ценные для следующих
этапов.

Возвращаться к ним — после появления сырых данных PoC-1/PoC-2:
если основной вывод (ns/op, allocated, throughput) окажется
неоднозначным, пункты ниже дают недостающий контекст.

---

## B-001. GC pause measurement

**Проблема:** сравнение arena vs pinned `T[]` на P/Invoke
по ns/op не показывает полной картины. Pinned-объект блокирует
компактификацию кучи, что бьёт по другим потокам и вызывает
GC-паузы.

**Что нужно:**
- Фоновый GC-нагрузочный harness: поток, аллоцирующий под load
  (например, короткоживущие объекты разного размера).
- Счётчик пауз: EventPipe или ETW-трейс, агрегация pause time.
- Прогон P/Invoke-сценариев (I01–I03) под нагрузкой.

**Оценка:** 1 день.
**Почему отложено:** цель PoC — ответить на главный вопрос
(ns/op, allocated, throughput). GC pause — метрика второго порядка.

## B-002. Pinning time measurement

**Проблема:** сколько времени объект находится в pinned state,
и как это влияет на компактификацию.

**Что нужно:**
- `dotnet-trace` + EventPipe-анализ `GCHeapStats` /
  `PinObjectAtGCTime`.
- Агрегация: total pinning time, max pinned object size,
  fragmentation.

**Оценка:** 0.5 дня.
**Почему отложено:** требует tooling, не блокирует основное решение.

## B-003. Cache miss rate и branch mispredictions

**Проблема:** alignment-бенчи (T2) выигрывают в throughput,
но непонятно, за счёт cache-line или за счёт чего-то ещё.

**Что нужно:**
- `perf stat -e cache-misses,cache-references,branch-misses` —
  **только на Linux-машине**; PMU-метрики в основном прогоне
  не собираются.
- На Windows — альтернативы: WPR/ETW (Windows Performance Recorder,
  профиль CPU sampling + PMU-счётчики), дороже в настройке
  и анализе.

**Оценка:** 0.5 дня (Linux), 1.5 дня (Windows).
**Почему отложено:** платформо-зависимо, не блокирует решение.

## B-004. Contention profiling для E04

**Проблема:** shared arena + lock vs thread-local arena —
разница в ns/op видна, но цена contention не измерена явно.

**Что нужно:**
- `dotnet-counters monitor --counters System.Runtime` —
  contention count.
- EventPipe-трейс `ContentionStart`/`ContentionStop`.

**Оценка:** 0.5 дня.
**Почему отложено:** E04 даёт ответ по ns/op, явная метрика
contention — дополнительный контекст.

## B-005. ARM64 специфика

**Проблема:** `Unsafe.Read<T>` на unaligned памяти на ARM64
ведёт себя иначе, чем на x64 (penalty или падение).
`[UnmanagedCallersOnly]` на ARM64 имеет нюансы с выравниванием
стека.

**Что нужно:**
- Прогон всех бенчей на ARM64 (GitHub Actions ARM runners или
  локальный Apple Silicon / Ampere).
- Сравнение с x64.

**Оценка:** 0.5 дня (если есть доступ к ARM CI).
**Почему отложено:** CI-инфраструктура, отдельная задача.

## B-006. Persistent arena для incremental solve

**Проблема:** сценарий E05 (incremental solve) покрыт поверхностно.
Реальный use case — сохранение состояния между запусками солвера.

**Что нужно:**
- Persistent arena: сериализация состояния в arena, восстановление.
- Отдельный бенч: recreate vs persistent.

**Оценка:** 1 день.
**Почему отложено:** расширение PoC-2, не блокирует основной вывод.

## B-007. Multi-tenancy и изоляция

**Проблема:** per-tenant арены как архитектурный паттерн.

**Что нужно:**
- Модель tenant-изоляции.
- Бенч: shared arena vs per-tenant arena.
- Latency p99 для приоритетного tenant под нагрузкой.

**Оценка:** 1 день.
**Почему отложено:** если multi-tenancy появится в netgraph,
это отдельный PoC.

---

## Как пользоваться backlog

- **Перед стартом PoC** — просмотреть, убедиться, что ничего
  блокирующего не отложено.
- **После PoC** — если основной вывод неоднозначен, вернуться
  к пунктам по приоритету: B-001/B-002 — если arena vs pinned
  в пределах ~10% по ns/op; B-003 — если alignment-выигрыш
  требует объяснения; B-004 — если E04 неоднозначен.
- **Не тащить в основной PoC** без явного решения — иначе PoC
  растянется на недели.
