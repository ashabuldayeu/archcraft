# Spec: SynteticApi — Unified Adapter Call Operation

**Status:** approved
**Created:** 2026-03-28
**Author:** —

---

## Summary

Рефакторинг `SynteticApi`: три отдельных класса операций (`RedisCallOperation`, `PgCallOperation`, `HttpCallOperation`) заменяются одним универсальным `AdapterCallOperation`. Класс при старте читает env vars вида `ADAPTER_OP_{TYPE}_URL` в словарь, затем на каждый вызов pipeline находит URL по типу операции и делегирует вызов `AdapterHttpClient`. CLI инжектирует эти env vars при компиляции проекта на основе статического маппинга `тип операции → технология → адаптер`.

## Problem Statement

Каждый тип операции требует отдельного класса в `SynteticApi`, хотя логика идентична: взять тип операции из шага, вызвать `POST /execute` на нужном адаптере. Это дублирование — информация о том, какой адаптер обслуживает какую операцию, должна жить в конфигурации (компилятор), а не в коде сервиса.

## Goals

- [ ] Удалены `RedisCallOperation`, `PgCallOperation`, `HttpCallOperation`
- [ ] Создан единый `AdapterCallOperation`, который при старте кэширует маппинг `operationType → URL` из env vars и на каждый вызов pipeline резолвит адрес адаптера
- [ ] `AdapterHttpClient` рефакторится: принимает URL как параметр метода вместо `BaseAddress` именованного клиента; использует `IHttpClientFactory.CreateClient()` без имени
- [ ] `PipelineExecutor` вызывает `AdapterCallOperation` напрямую, без роутинга через интерфейс
- [ ] CLI строит `ADAPTER_OP_{TYPE}_URL` для каждого synthetic-сервиса на основе `SyntheticOperations` и `Adapters` в `ProjectDefinition`
- [ ] Регистрация именованных HTTP-клиентов по адаптерам в `Program.cs` удаляется

## Non-Goals (Out of Scope)

- Изменение протокола `POST /execute` между сервисом и адаптером
- Динамическое добавление типов операций в runtime
- Изменение формата `SYNTETIC_CONFIG`

## Acceptance Criteria

- [ ] AC-1: В `SynteticApi` нет классов, специфичных для технологии (`RedisCallOperation`, `PgCallOperation`, `HttpCallOperation`)
- [ ] AC-2: `AdapterCallOperation` при инициализации читает все `ADAPTER_OP_*_URL` из `IConfiguration` и сохраняет в `Dictionary<string, string>`
- [ ] AC-3: `AdapterCallOperation.ExecuteAsync(operationType, request, ct)` находит URL в словаре и вызывает `AdapterHttpClient`; если URL не найден — возвращает `OperationResult.Error` с сообщением о неизвестной операции
- [ ] AC-4: `AdapterHttpClient.ExecuteAsync` принимает `url` как параметр; использует `IHttpClientFactory.CreateClient()` (без имени) для создания клиента
- [ ] AC-5: `PipelineExecutor` передаёт `step.Operation` напрямую в `AdapterCallOperation`, не ищет реализацию через интерфейс
- [ ] AC-6: CLI строит `ADAPTER_OP_REDIS_CALL_URL`, `ADAPTER_OP_PG_CALL_URL`, `ADAPTER_OP_HTTP_CALL_URL` для каждого synthetic-сервиса на основе `SyntheticOperations` → статический маппинг → `Adapters`
- [ ] AC-7: В `Program.cs` нет регистрации именованных `HttpClient` для адаптеров
- [ ] AC-8: При компиляции проекта без адаптера для нужной операции — ошибка валидации (уже обеспечивается `TopologyValidator`)

## Domain & Architecture

```
services/synthetic/SynteticApi/
├── Program.cs                      # изм: убрать IHttpClientFactory с именованными клиентами,
│                                   #      убрать регистрацию 3 операций, добавить AdapterCallOperation
├── Operations/
│   ├── AdapterCallOperation.cs     # новый: единая точка вызова адаптера
│   ├── RedisCallOperation.cs       # удалить
│   ├── PgCallOperation.cs          # удалить
│   └── HttpCallOperation.cs        # удалить
├── Pipeline/
│   └── PipelineExecutor.cs         # изм: вызывать AdapterCallOperation напрямую
└── Observability/
    └── AdapterHttpClient.cs        # изм: URL как параметр, CreateClient() без имени

src/Archcraft.ProjectCompiler/
└── ArchcraftProjectCompiler.cs     # изм: строить ADAPTER_OP_{TYPE}_URL для synthetic-сервисов
```

### Env vars инжектируемые CLI в synthetic-сервис

Для каждой операции из `SyntheticOperations` сервиса:

| Операция | Env var | Пример значения |
|----------|---------|----------------|
| `redis-call` | `ADAPTER_OP_REDIS_CALL_URL` | `http://redis-adapter` |
| `pg-call` | `ADAPTER_OP_PG_CALL_URL` | `http://pg-adapter` |
| `http-call` | `ADAPTER_OP_HTTP_CALL_URL` | `http://http-adapter` |

Формат нормализации: `operationType.Replace("-", "_").ToUpperInvariant()`
Полное имя переменной: `$"ADAPTER_OP_{normalized}_URL"`

### Статический маппинг в CLI

```
redis-call  → technology: redis    → адаптер с technology=redis
pg-call     → technology: postgres → адаптер с technology=postgres
http-call   → technology: http     → адаптер с technology=http
```

Существует в `TopologyValidator.OperationTechnologyMap` — переиспользуется в компиляторе.

### Логика `AdapterCallOperation`

```
Конструктор:
  Для каждого ключа из IConfiguration вида ADAPTER_OP_*_URL:
    нормализовать обратно → operationType → URL
    добавить в _adapterUrls словарь

ExecuteAsync(operationType, request, ct):
  Нормализовать operationType → ключ
  Если URL не найден → вернуть OperationResult.Error("Unknown operation type")
  Вызвать AdapterHttpClient.ExecuteAsync(url, operationType, request, ct)
```

## Key Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Кэширование URL | При инициализации в словарь | Env vars не меняются, нет смысла читать на каждый вызов |
| URL в AdapterHttpClient | Параметр метода | Убирает зависимость от именованных клиентов |
| Ошибка при отсутствии URL | `OperationResult.Error` | Не роняет сервис, видно в метриках |
| Удаление IOperation из executor | Да | Один класс — нет смысла в интерфейсе для роутинга |

## Open Questions

*(нет)*

## Dependencies

- `AdapterHttpClient` — рефакторится в рамках этой задачи
- `TopologyValidator.OperationTechnologyMap` — переиспользуется компилятором

---

*Approved by: user Date: 2026-03-28*
