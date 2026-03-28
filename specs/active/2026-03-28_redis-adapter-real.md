# Spec: RedisAdapter — Real Redis Implementation

**Status:** approved
**Created:** 2026-03-28
**Author:** —

---

## Summary

Замена заглушки в `RedisAdapter` на реальную реализацию с доступом к Redis через `StackExchange.Redis`. Адаптер выполняет две операции (`get`, `set`) над ключами с фиксированным префиксом `archcraft:`. Connection string строится CLI из конфигурации связанного Redis-сервиса в `project.yaml` и инжектируется как env var `REDIS_CONNECTION_STRING`. Политика retry настраивается через env vars. TTL не поддерживается.

## Problem Statement

`RedisAdapter` является заглушкой — симулирует задержку вместо реального взаимодействия с Redis. Для полноценной симуляции нагрузки нужен адаптер, который выполняет настоящие команды Redis.

## Goals

- [ ] `StackExchange.Redis` используется для подключения к Redis
- [ ] Реализованы две операции: `get` (GET) и `set` (SET)
- [ ] Ключи хранятся с фиксированным префиксом `archcraft:`
- [ ] Connection string инжектируется через env var `REDIS_CONNECTION_STRING`
- [ ] CLI строит `REDIS_CONNECTION_STRING` из имени сервиса (`connects-to`) и порта 6379; если у Redis-сервиса задан `REDIS_PASSWORD` — включает его
- [ ] В `project.yaml` адаптер линкуется к Redis-сервису через `connects-to`
- [ ] Retry политика: `REDIS_RETRY_COUNT` (default: 0), `REDIS_RETRY_DELAY_MS` (default: 500)
- [ ] Ошибки подключения и выполнения команд возвращают `outcome: error`

## Non-Goals (Out of Scope)

- TTL для операции `set`
- Дополнительные операции (del, exists, lpush и т.д.)
- SSL/TLS подключение
- Кластерный режим Redis

## Acceptance Criteria

- [ ] AC-1: `POST /execute` с `{ "operation": "set", "payload": { "key": "foo", "value": "bar" } }` сохраняет значение в Redis под ключом `archcraft:foo` и возвращает `{ "outcome": "success", "durationMs": N }`
- [ ] AC-2: `POST /execute` с `{ "operation": "get", "payload": { "key": "foo" } }` возвращает `{ "outcome": "success", "data": { "value": "bar" } }` если ключ существует
- [ ] AC-3: `POST /execute` с `{ "operation": "get", "payload": { "key": "missing" } }` возвращает `{ "outcome": "not_found" }` если ключ отсутствует
- [ ] AC-4: При недоступном Redis `POST /execute` возвращает `{ "outcome": "error", "data": { "error": "..." } }`
- [ ] AC-5: При `REDIS_RETRY_COUNT=3` и `REDIS_RETRY_DELAY_MS=200` адаптер делает до 3 повторных попыток перед возвратом ошибки
- [ ] AC-6: `REDIS_CONNECTION_STRING` строится CLI: `{service-name}:6379` (без пароля) или `{service-name}:6379,password={REDIS_PASSWORD}` (если задан)
- [ ] AC-7: Адаптер стартует и проходит `GET /health` при наличии доступного Redis
- [ ] AC-8: Все операции записывают `operation.duration` и `operation.calls.total` с атрибутами `operation_type` и `result`

## Domain & Architecture

```
services/adapters/RedisAdapter/
├── RedisAdapter.csproj             # изм: добавить StackExchange.Redis
├── Program.cs                      # изм: регистрация RedisConnectionFactory, RetryPolicy
├── Configuration/
│   └── RedisAdapterOptions.cs      # новый: connection string, retry settings
├── Database/
│   ├── RedisConnectionFactory.cs   # новый: IConnectionMultiplexer с настройками
│   └── RetryPolicy.cs              # новый: retry логика (аналог PgAdapter)
└── Operations/
    ├── GetOperation.cs             # изм: реальная команда GET
    └── SetOperation.cs             # изм: реальная команда SET
```

```
src/Archcraft.ProjectCompiler/
└── ArchcraftProjectCompiler.cs     # изм: строить REDIS_CONNECTION_STRING и инжектить в env адаптера
```

### Env vars адаптера

| Var | Default | Description |
|-----|---------|-------------|
| `REDIS_CONNECTION_STRING` | — | Строится CLI, обязательна |
| `REDIS_RETRY_COUNT` | `0` | Количество повторных попыток |
| `REDIS_RETRY_DELAY_MS` | `500` | Задержка между попытками (мс) |

### project.yaml пример

```yaml
adapters:
  - name: redis-adapter
    image: archcraft/redis-adapter:latest
    port: 8082
    technology: redis
    connects-to: redis
```

## Key Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Клиент | StackExchange.Redis | Стандарт для .NET, поддерживает пул соединений |
| Префикс ключей | `archcraft:` фиксированный | Изоляция в общем Redis, без конфигурации |
| TTL | Не поддерживается | Упрощает реализацию, не нужно сейчас |
| Connection string | Из CLI через env var | Аналогично PgAdapter, пользователь работает только с `project.yaml` |

## Open Questions

*(нет)*

## Dependencies

- Spec: `specs/active/2026-03-26_adapter-protocol.md` — базовый протокол адаптера
- NuGet: `StackExchange.Redis`
- Redis 7+ (официальный образ `redis:7`)

---

*Approved by: user Date: 2026-03-28*
