# Spec: Adapter Protocol — Technology Adapters for SynteticApi

**Status:** approved
**Created:** 2026-03-26
**Author:** —

---

## Summary

Вводится уровень адаптеров — отдельных ASP.NET-контейнеров, каждый из которых реализует базовые операции над конкретной технологией (PostgreSQL, Redis и т.д.). `SynteticApi` общается с адаптерами по унифицированному HTTP-протоколу. Операции `redis-call`, `pg-call` в pipeline synthetic-сервиса маппятся на соответствующий адаптер. CLI понимает эту зависимость при компиляции топологии и валидирует её.

## Problem Statement

`SynteticApi` должен уметь симулировать вызовы к разным технологиям (Redis, PostgreSQL, HTTP). Реализовывать прямой доступ к каждой из них внутри одного сервиса — архитектурная проблема: сервис разрастается, зависимости смешиваются. Нужен общий интерфейс и отдельные адаптеры, каждый из которых инкапсулирует одну технологию.

## Goals

- [ ] Определён единый HTTP-протокол общения `SynteticApi ↔ Adapter` (`POST /execute`, `X-Correlation-Id` header)
- [ ] Контракт вынесен в общую библиотеку `Adapters.Contracts`
- [ ] Реализован `PgAdapter` (заглушка) с операциями: `query`, `insert`, `update`
- [ ] Реализован `RedisAdapter` (заглушка) с операциями: `get`, `set`
- [ ] Операции `pg-call` и `redis-call` в `SynteticApi` переключены с локальных заглушек на вызовы соответствующих адаптеров через HTTP
- [ ] Адаптеры регистрируются в `project.yaml` в секции `adapters:`
- [ ] Сервисы линкуются к адаптерам по имени в `project.yaml`
- [ ] CLI при компиляции топологии строит граф зависимостей сервис → адаптеры
- [ ] Команда `validate` проверяет что каждая операция в pipeline имеет соответствующий зарегистрированный адаптер
- [ ] Адрес адаптера в runtime определяется через Docker network alias; env var `ADAPTER_{NAME}_URL` переопределяет адрес

## Non-Goals (Out of Scope)

- Реальная реализация операций в адаптерах (заглушки, реальный доступ — следующий этап)
- Аутентификация между `SynteticApi` и адаптерами
- Динамическое добавление адаптеров без изменения `project.yaml`
- Адаптеры для технологий кроме PostgreSQL, Redis и HTTP на данном этапе

## Acceptance Criteria

- [ ] AC-1: `POST /execute` с `X-Correlation-Id` заголовком и телом `{ "operation": "query", "payload": {} }` к `PgAdapter` возвращает `{ "outcome": "success", "durationMs": N, "data": {} }`
- [ ] AC-2: `PgAdapter` возвращает `{ "outcome": "error" }` при передаче неизвестной операции (не `query`/`insert`/`update`)
- [ ] AC-3: `RedisAdapter` аналогично обрабатывает операции `get`, `set` и возвращает ошибку на неизвестную операцию
- [ ] AC-4: `SynteticApi` при выполнении шага `redis-call` в pipeline делает HTTP-запрос к адресу Redis-адаптера, а не вызывает локальную заглушку
- [ ] AC-5: `SynteticApi` при выполнении шага `pg-call` делает HTTP-запрос к адресу Pg-адаптера
- [ ] AC-6: Адрес адаптера определяется из env var `ADAPTER_{NAME}_URL` если задан, иначе из Docker network alias `http://{adapter-name}`
- [ ] AC-7: `X-Correlation-Id` пробрасывается из `SynteticApi` в исходящий запрос к адаптеру
- [ ] AC-8: В `project.yaml` можно объявить адаптеры в секции `adapters:` и слинковать сервис с адаптером по имени
- [ ] AC-9: `archcraft validate` возвращает ошибку если сервис использует операцию `pg-call` но адаптер с `technology: postgres` не зарегистрирован
- [ ] AC-10: `archcraft validate` возвращает ошибку если сервис ссылается на адаптер по имени, которого нет в секции `adapters:`
- [ ] AC-11: `HttpAdapter` обрабатывает операцию `request`, возвращает `{ "outcome": "success", "durationMs": N }` (заглушка)
- [ ] AC-12: `SynteticApi` `http-call` → HTTP-запрос к `HttpAdapter`
- [ ] AC-13: Каждый адаптер (`PgAdapter`, `RedisAdapter`, `HttpAdapter`) собирается в Docker-образ из Dockerfile и стартует без ошибок

## Domain & Architecture

### Новые проекты

```
services/adapters/
├── Adapters.Contracts/         # новый: shared контракт
│   ├── Adapters.Contracts.csproj
│   ├── ExecuteRequest.cs       # { Operation, Payload }
│   ├── ExecuteResponse.cs      # { Outcome, DurationMs, Data }
│   └── AdapterOutcome.cs       # enum: Success, NotFound, Error
├── PgAdapter/                  # новый: ASP.NET Minimal API, заглушка
│   ├── PgAdapter.csproj
│   ├── Dockerfile
│   ├── Program.cs
│   └── Operations/
│       ├── QueryOperation.cs   # stub
│       ├── InsertOperation.cs  # stub
│       └── UpdateOperation.cs  # stub
├── RedisAdapter/               # новый: ASP.NET Minimal API, заглушка
│   ├── RedisAdapter.csproj
│   ├── Dockerfile
│   ├── Program.cs
│   └── Operations/
│       ├── GetOperation.cs     # stub
│       └── SetOperation.cs     # stub
└── HttpAdapter/                # новый: ASP.NET Minimal API, заглушка
    ├── HttpAdapter.csproj
    ├── Dockerfile
    ├── Program.cs
    └── Operations/
        └── RequestOperation.cs # stub, симулирует задержку
```

### Изменения в существующих проектах

```
services/synthetic/
└── Operations/
    ├── RedisCallOperation.cs   # изм: HTTP-вызов к RedisAdapter вместо локальной заглушки
    ├── PgCallOperation.cs      # изм: HTTP-вызов к PgAdapter вместо локальной заглушки
    └── HttpCallOperation.cs    # изм: HTTP-вызов к HttpAdapter вместо локальной заглушки

src/Archcraft.ProjectModel/
└── AdapterModel.cs             # новый: { Name, Image, Port, Technology }
    ProjectFileModel.cs         # изм: добавить List<AdapterModel> Adapters

src/Archcraft.Domain/
└── Entities/
    └── AdapterDefinition.cs    # новый: { Name, Image, Port, Technology }
    ServiceDefinition.cs        # изм: добавить IReadOnlyList<string> Adapters (имена)

src/Archcraft.ProjectCompiler/
└── TopologyValidator.cs        # изм: валидация adapter → operation mapping
```

### project.yaml пример

```yaml
adapters:
  - name: pg-adapter
    image: archcraft/pg-adapter:latest
    port: 8081
    technology: postgres
  - name: redis-adapter
    image: archcraft/redis-adapter:latest
    port: 8082
    technology: redis

services:
  - name: order-api
    image: archcraft/syntetic-api:latest
    port: 8080
    synthetic:
      adapters:
        - pg-adapter
        - redis-adapter
      endpoints:
        - alias: checkout
          pipeline:
            - operation: redis-call
              not-found-rate: 0.3
              fallback:
                - operation: pg-call
```

### Маппинг operation → technology → adapter

| operation   | technology |
|-------------|------------|
| `redis-call`| `redis`    |
| `pg-call`   | `postgres` |
| `http-call` | `http`     |

## Key Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Протокол | HTTP `POST /execute` | Простота, стандарт, легко тестировать |
| Correlation ID | `X-Correlation-Id` header | Не засоряет бизнес-payload, стандарт |
| Runtime discovery | Docker network alias + `ADAPTER_{NAME}_URL` override | Alias — декларативно, без хардкода URL; env var — гибкость для CI/нестандартных сред |
| Shared контракт | `Adapters.Contracts` библиотека | Единый источник истины для request/response типов |
| Адаптеры | Заглушки | Реальные реализации — следующий этап |

## Open Questions

- [x] Q1: `HttpAdapter` добавлен — одна операция `request`, заглушка с симулированной задержкой.

## Dependencies

- Spec: `specs/active/2026-03-26_syntetic-api.md` — операции `RedisCallOperation`, `PgCallOperation` реализованы как заглушки, здесь они заменяются на HTTP-клиент
- .NET 10 / ASP.NET Minimal API
- `System.Net.Http` — HTTP-клиент в `SynteticApi` для вызова адаптеров

---

*Approved by: user Date: 2026-03-26*
