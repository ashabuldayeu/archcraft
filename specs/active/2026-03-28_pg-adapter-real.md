# Spec: PgAdapter — Real PostgreSQL Implementation

**Status:** approved
**Created:** 2026-03-28
**Author:** —

---

## Summary

Замена заглушки в `PgAdapter` на реальную реализацию с доступом к PostgreSQL через Npgsql (ADO.NET, без ORM). Адаптер выполняет фиксированные операции (`query`, `insert`, `update`) над встроенной таблицей `synthetic_items`, которую создаёт при старте. Connection string строится CLI из конфигурации связанного PostgreSQL-сервиса в `project.yaml` и инжектируется как env var. Параметры пула соединений и политика retry настраиваются через env vars.

## Problem Statement

`PgAdapter` является заглушкой — он симулирует задержку вместо реального взаимодействия с БД. Для полноценной симуляции нагрузки и наблюдаемости нужен адаптер, который выполняет настоящие SQL-запросы, порождает реальные метрики I/O и корректно обрабатывает ошибки подключения.

## Goals

- [ ] Npgsql (ADO.NET) используется для подключения к PostgreSQL без ORM
- [ ] При старте адаптер создаёт таблицу `synthetic_items` если она не существует
- [ ] Реализованы три операции: `query` (SELECT), `insert` (INSERT), `update` (UPDATE)
- [ ] Операции используют фиксированный SQL, payload содержит только данные
- [ ] Connection string инжектируется через env var `PG_CONNECTION_STRING`
- [ ] CLI строит `PG_CONNECTION_STRING` из env vars PostgreSQL-сервиса (`POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD`) и имени сервиса как хоста
- [ ] В `project.yaml` адаптер явно линкуется к PostgreSQL-сервису через поле `connects-to`
- [ ] Параметры пула настраиваются через `PG_POOL_MIN_SIZE` (default: 1) и `PG_POOL_MAX_SIZE` (default: 10)
- [ ] Retry политика: `PG_RETRY_COUNT` (default: 0), `PG_RETRY_DELAY_MS` (default: 500)
- [ ] Ошибки подключения и ошибки выполнения запросов отражаются в метриках (`operation.calls.total` с `result=error`)

## Non-Goals (Out of Scope)

- Поддержка произвольного SQL через payload
- Миграции схемы (только CREATE TABLE IF NOT EXISTS при старте)
- Транзакции между операциями
- Аутентификация через SSL/TLS (используется стандартное подключение)
- Реализация Redis и HTTP адаптеров (отдельные задачи)

## Acceptance Criteria

- [ ] AC-1: При старте адаптера таблица `synthetic_items (id SERIAL PRIMARY KEY, name TEXT, value TEXT, created_at TIMESTAMPTZ)` создаётся если не существует
- [ ] AC-2: `POST /execute` с `{ "operation": "insert", "payload": { "name": "foo", "value": "bar" } }` вставляет запись и возвращает `{ "outcome": "success", "durationMs": N }`
- [ ] AC-3: `POST /execute` с `{ "operation": "query", "payload": { "id": 1 } }` возвращает запись по id; если не найдена — `{ "outcome": "not_found" }`
- [ ] AC-4: `POST /execute` с `{ "operation": "update", "payload": { "id": 1, "name": "new", "value": "val" } }` обновляет запись; если не найдена — `{ "outcome": "not_found" }`
- [ ] AC-5: При недоступном PostgreSQL `POST /execute` возвращает `{ "outcome": "error", "data": { "error": "..." } }` и инкрементирует `operation.calls.total{result=error}`
- [ ] AC-6: `PG_POOL_MIN_SIZE` и `PG_POOL_MAX_SIZE` применяются к Npgsql connection pool
- [ ] AC-7: При `PG_RETRY_COUNT=3` и `PG_RETRY_DELAY_MS=200` адаптер делает до 3 повторных попыток с задержкой 200ms перед возвратом ошибки
- [ ] AC-8: `PG_CONNECTION_STRING` строится CLI из сервиса на который указывает `connects-to` в `project.yaml`: `Host={service-name};Port=5432;Database={POSTGRES_DB};Username={POSTGRES_USER};Password={POSTGRES_PASSWORD}`
- [ ] AC-9: Адаптер стартует и проходит `GET /health` при наличии доступного PostgreSQL
- [ ] AC-10: Все операции записывают `operation.duration` и `operation.calls.total` с атрибутами `operation_type` и `result`

## Domain & Architecture

### Изменения в `services/adapters/PgAdapter/`

```
services/adapters/PgAdapter/
├── PgAdapter.csproj            # изм: добавить Npgsql
├── Program.cs                  # изм: регистрация DbConnectionFactory, PgSchemaInitializer
├── Configuration/
│   └── PgAdapterOptions.cs     # новый: connection string, pool, retry settings
├── Database/
│   ├── DbConnectionFactory.cs  # новый: NpgsqlDataSource с настройками пула
│   ├── PgSchemaInitializer.cs  # новый: CREATE TABLE IF NOT EXISTS при старте
│   └── RetryPolicy.cs          # новый: retry логика
└── Operations/
    ├── QueryOperation.cs       # изм: реальный SELECT по id
    ├── InsertOperation.cs      # изм: реальный INSERT
    └── UpdateOperation.cs      # изм: реальный UPDATE
```

### Изменения в `src/Archcraft.ProjectModel/`

```
src/Archcraft.ProjectModel/
└── AdapterModel.cs             # изм: добавить ConnectsTo (string?)
```

### Изменения в `src/Archcraft.Domain/`

```
src/Archcraft.Domain/
└── Entities/
    └── AdapterDefinition.cs    # изм: добавить ConnectsTo (string?)
```

### Изменения в CLI/Compiler

```
src/Archcraft.ProjectCompiler/
└── ArchcraftProjectCompiler.cs # изм: строить PG_CONNECTION_STRING и инжектить в env адаптера
```

### Схема таблицы

```sql
CREATE TABLE IF NOT EXISTS synthetic_items (
    id         SERIAL PRIMARY KEY,
    name       TEXT NOT NULL,
    value      TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
```

### Операции и SQL

| Operation | SQL |
|-----------|-----|
| `query`   | `SELECT id, name, value, created_at FROM synthetic_items WHERE id = @id` |
| `insert`  | `INSERT INTO synthetic_items (name, value) VALUES (@name, @value) RETURNING id` |
| `update`  | `UPDATE synthetic_items SET name = @name, value = @value WHERE id = @id` |

### project.yaml пример

```yaml
services:
  - name: postgres
    image: postgres:16
    port: 5432
    env:
      POSTGRES_DB: archcraft
      POSTGRES_USER: user
      POSTGRES_PASSWORD: secret

adapters:
  - name: pg-adapter
    image: archcraft/pg-adapter:latest
    port: 8081
    technology: postgres
    connects-to: postgres
```

### Env vars адаптера

| Var | Default | Description |
|-----|---------|-------------|
| `PG_CONNECTION_STRING` | — | Строится CLI, обязательна |
| `PG_POOL_MIN_SIZE` | `1` | Минимум соединений в пуле |
| `PG_POOL_MAX_SIZE` | `10` | Максимум соединений в пуле |
| `PG_RETRY_COUNT` | `0` | Количество повторных попыток |
| `PG_RETRY_DELAY_MS` | `500` | Задержка между попытками (мс) |

## Key Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Драйвер | Npgsql (ADO.NET) | Нативный, без ORM, максимальный контроль |
| SQL | Фиксированный в коде | Простота, предсказуемость, no injection risk |
| Схема | CREATE IF NOT EXISTS при старте | Нет миграций, достаточно для симуляции |
| Connection string | Из CLI через env var | Пользователь работает только с `project.yaml` |
| Retry | Конфигурируемый, default=0 | Реалистичное поведение, не скрывает проблемы |
| not_found | Отдельный outcome | Соответствует протоколу адаптера и not-found-rate механизму |

## Open Questions

*(нет)*

## Dependencies

- Spec: `specs/active/2026-03-26_adapter-protocol.md` — базовый протокол адаптера
- NuGet: `Npgsql` — ADO.NET драйвер PostgreSQL
- PostgreSQL 15+ (совместимость с образом `postgres:16`)

---

*Approved by: user Date: 2026-03-28*
