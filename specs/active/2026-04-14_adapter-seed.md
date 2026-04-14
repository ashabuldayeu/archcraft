# Spec: Adapter Seed / Clear

**Status:** approved
**Created:** 2026-04-14
**Author:** ashabuldayeu

---

## Summary

Добавить поддержку инициализации тестовых данных в БД через адаптеры. Каждый адаптер реализует `IDataSeeder` и регистрирует два HTTP-эндпоинта (`POST /seed`, `POST /clear`). Кол-во строк задаётся per-adapter в `project.yaml`. В REPL появляются команды `seed <name>|all` и `clear <name>|all`, которые напрямую вызывают адаптер по хост-порту, минуя ToxiProxy.

## Problem Statement

Нагрузочные тесты работают с пустой БД. Для реалистичного измерения latency (cache hit/miss ratio, query planner hints, index scan vs seq scan) БД должна содержать предварительно засеянные данные. Сейчас заполнить данные можно только вручную.

## Goals

- [ ] Новый интерфейс `IDataSeeder` в `Adapters.Contracts` с методами `SeedAsync` и `ClearAsync`
- [ ] `POST /seed` и `POST /clear` в `AdapterEndpoints` — единые для всех адаптеров
- [ ] PgAdapter: seed вставляет N строк в `synthetic_items`, clear — TRUNCATE
- [ ] RedisAdapter: seed записывает N ключей `archcraft:<i>` → случайная строка, clear удаляет все ключи по префиксу `archcraft:*`
- [ ] HttpAdapter: реализует `IDataSeeder` как no-op, всегда возвращает успех
- [ ] `seed_rows` per-adapter в `project.yaml`, маппируется в `AdapterDefinition.SeedRows`
- [ ] CLI хранит host-URL каждого запущенного адаптера в `EnvironmentContext`
- [ ] REPL-команды: `seed <name>`, `seed all`, `clear <name>`, `clear all`

## Non-Goals (Out of Scope)

- Кастомные SQL-схемы или форматы данных (только `synthetic_items`)
- Seed во время/после сценария
- Seed для synthetic-сервисов (только адаптеры с БД)
- Прогресс-бар или streaming-вывод во время seed

## Acceptance Criteria

- [ ] AC-1: `AdapterModel` содержит поле `seed_rows: int?`. `AdapterDefinition` содержит `int SeedRows` (default 0). `YamlProjectLoader` маппирует значение.

- [ ] AC-2: `IDataSeeder` объявлен в `Adapters.Contracts`:
  ```csharp
  public interface IDataSeeder
  {
      Task SeedAsync(int rows, CancellationToken cancellationToken = default);
      Task ClearAsync(CancellationToken cancellationToken = default);
  }
  ```

- [ ] AC-3: `AdapterEndpoints` регистрирует `POST /seed` (тело `{ "rows": N }`) и `POST /clear`. Оба резолвят `IDataSeeder` из DI. Если `IDataSeeder` не зарегистрирован — возвращают `200 OK` с `{ "status": "no-op" }`.

- [ ] AC-4: `PgDataSeeder.SeedAsync(N)` вставляет ровно N строк в `synthetic_items` батчами (≤ 1000 строк за INSERT). Данные: `name = "item-{i}"`, `value` — случайный UUID.

- [ ] AC-5: `PgDataSeeder.ClearAsync()` выполняет `TRUNCATE synthetic_items RESTART IDENTITY`.

- [ ] AC-6: `RedisDataSeeder.SeedAsync(N)` записывает N ключей `archcraft:0` … `archcraft:{N-1}` со случайным строковым значением (UUID).

- [ ] AC-7: `RedisDataSeeder.ClearAsync()` удаляет все ключи по паттерну `archcraft:*` через SCAN + DEL (не KEYS, чтобы не блокировать).

- [ ] AC-8: `HttpAdapter` регистрирует `NoOpDataSeeder : IDataSeeder` — оба метода немедленно возвращают `Task.CompletedTask`.

- [ ] AC-9: `DockerEnvironmentRunner` после старта каждого адаптера регистрирует `RunningAdapter { Name, BaseUrl }` в `EnvironmentContext`. `BaseUrl = http://localhost:<mapped-port>`.

- [ ] AC-10: REPL-команда `seed <name>` находит адаптер по имени в `ExecutionPlan.Adapters`, берёт `SeedRows`, вызывает `POST <BaseUrl>/seed {"rows": SeedRows}`. Если `SeedRows == 0` — выводит предупреждение и не вызывает API.

- [ ] AC-11: REPL-команда `seed all` последовательно вызывает seed для всех адаптеров с `SeedRows > 0`.

- [ ] AC-12: REPL-команды `clear <name>` и `clear all` — аналогично, вызывают `POST /clear`.

- [ ] AC-13: После успешного ответа от адаптера REPL выводит: `[seed] pg-adapter — seeded 10000 rows.` / `[clear] redis-adapter — cleared.`

- [ ] AC-14: При ошибке HTTP или `SeedRows == 0` для `seed <name>` REPL выводит понятное сообщение, не падает.

- [ ] AC-15: `help` показывает новые команды.

## Domain & Architecture

```
services/adapters/
├── Adapters.Contracts/
│   ├── IDataSeeder.cs              # new
│   ├── SeedRequest.cs              # new: { int Rows }
│   └── AdapterEndpoints.cs         # +POST /seed, +POST /clear
├── PgAdapter/Database/
│   └── PgDataSeeder.cs             # new: IDataSeeder
├── RedisAdapter/Database/
│   └── RedisDataSeeder.cs          # new: IDataSeeder
└── HttpAdapter/Http/
    └── NoOpDataSeeder.cs           # new: IDataSeeder no-op

src/
├── Archcraft.ProjectModel/
│   └── AdapterModel.cs             # +SeedRows: int?
├── Archcraft.Domain/Entities/
│   └── AdapterDefinition.cs        # +SeedRows: int
├── Archcraft.Serialization.Yaml/
│   └── YamlProjectLoader.cs        # +map SeedRows
├── Archcraft.Execution/
│   ├── RunningAdapter.cs           # new: { Name, BaseUrl }
│   └── EnvironmentContext.cs       # +RegisterAdapter, +GetAdapter, +AllAdapters
├── Archcraft.Execution.Docker/
│   └── DockerEnvironmentRunner.cs  # +register RunningAdapter after StartAdapterAsync
└── Archcraft.App/UseCases/
    └── InteractiveSessionUseCase.cs # +seed/clear command handling
```

## Key Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Протокол seed/clear | HTTP `POST /seed`, `POST /clear` на порту адаптера | Адаптеры уже имеют HTTP-сервер; единообразно с `/execute` и `/health` |
| Доступ к адаптеру из CLI | Напрямую по хост-порту, минуя ToxiProxy | Seed — инфраструктурная операция, не должна имитировать production-трафик и не должна зависеть от токсиков |
| Батч-вставка в PG | ≤ 1000 строк за INSERT | Избегает OOM и сетевых таймаутов при большом N |
| Clear Redis | SCAN + DEL по паттерну | `KEYS archcraft:*` блокирует Redis при большом keyspace |
| `SeedRows == 0` | Предупреждение без вызова API | Позволяет оставить адаптер без seed, не удаляя поле из конфига |
| HttpAdapter | No-op реализация | Контракт соблюдён, имплементация пустая; не создаёт ложных ожиданий |

## Open Questions

_(нет)_

## Dependencies

- `PgSchemaInitializer` уже создаёт таблицу `synthetic_items` при старте — seed-данные ложатся в готовую схему
- `RedisConnectionFactory` уже реализована — `RedisDataSeeder` использует её напрямую

---

*Approved by: — Date: —*
