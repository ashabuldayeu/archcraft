# Spec: HttpAdapter — Real HTTP Implementation

**Status:** approved
**Created:** 2026-03-28
**Author:** —

---

## Summary

Замена заглушки в `HttpAdapter` на реальную реализацию, которая пробрасывает HTTP-запросы к целевому сервису в Docker-сети. Адаптер привязывается к конкретному сервису через `connects-to` в `project.yaml`; CLI инжектирует базовый URL целевого сервиса как `HTTP_TARGET_URL`. Операция одна — `request` с минимальным payload (метод, путь, опционально тело). Политика retry настраивается через env vars.

## Problem Statement

`HttpAdapter` является заглушкой — не выполняет реальных HTTP-вызовов. Нужна реализация, которая пробрасывает запросы к другому сервису в топологии и возвращает реальный результат.

## Goals

- [ ] Реализована операция `request`, выполняющая HTTP-запрос к целевому сервису
- [ ] Целевой URL строится CLI из `connects-to`: `http://{service-name}:{service-port}` и инжектируется как `HTTP_TARGET_URL`
- [ ] Payload содержит: `method` (строка), `path` (строка), `body` (строка, опционально)
- [ ] Маппинг результата: 2xx → `success`, 404 → `not_found`, остальное → `error`
- [ ] Retry политика: `HTTP_RETRY_COUNT` (default: 0), `HTTP_RETRY_DELAY_MS` (default: 500)
- [ ] `X-Correlation-Id` из входящего запроса пробрасывается к целевому сервису

## Non-Goals (Out of Scope)

- Поддержка произвольных заголовков в payload
- Аутентификация (Basic, Bearer и т.д.)
- HTTPS / TLS
- Несколько целевых сервисов в одном адаптере

## Acceptance Criteria

- [ ] AC-1: `POST /execute` с `{ "operation": "request", "payload": { "method": "GET", "path": "/health" } }` выполняет `GET http://{target}/health` и возвращает `{ "outcome": "success", "durationMs": N }` при 2xx ответе
- [ ] AC-2: Ответ 404 от целевого сервиса → `{ "outcome": "not_found" }`
- [ ] AC-3: Недоступный целевой сервис или ответ 5xx → `{ "outcome": "error", "data": { "error": "..." } }`
- [ ] AC-4: Payload с `"body": "..."` включает тело в исходящий запрос
- [ ] AC-5: Заголовок `X-Correlation-Id` пробрасывается к целевому сервису
- [ ] AC-6: При `HTTP_RETRY_COUNT=3` адаптер делает до 3 повторных попыток перед возвратом ошибки
- [ ] AC-7: `HTTP_TARGET_URL` строится CLI: `http://{service-name}:{service-port}` из сервиса, указанного в `connects-to`
- [ ] AC-8: `GET /health` возвращает 200

## Domain & Architecture

```
services/adapters/HttpAdapter/
├── HttpAdapter.csproj              # изм: нет новых пакетов (IHttpClientFactory встроен)
├── Program.cs                      # изм: регистрация HttpAdapterOptions, IHttpClientFactory, RetryPolicy
├── Configuration/
│   └── HttpAdapterOptions.cs       # новый: target URL, retry settings
├── Http/
│   └── RetryPolicy.cs              # новый: retry логика (аналог Pg/Redis)
└── Operations/
    └── RequestOperation.cs         # изм: реальный HTTP-запрос через IHttpClientFactory
```

```
src/Archcraft.ProjectCompiler/
└── ArchcraftProjectCompiler.cs     # изм: строить HTTP_TARGET_URL и инжектить в env адаптера
```

### Env vars адаптера

| Var | Default | Description |
|-----|---------|-------------|
| `HTTP_TARGET_URL` | — | Строится CLI, обязательна |
| `HTTP_RETRY_COUNT` | `0` | Количество повторных попыток |
| `HTTP_RETRY_DELAY_MS` | `500` | Задержка между попытками (мс) |

### Payload операции `request`

| Поле | Тип | Обязательное | Описание |
|------|-----|--------------|----------|
| `method` | string | да | HTTP метод (GET, POST, PUT, DELETE, ...) |
| `path` | string | да | Путь запроса (например `/api/items/1`) |
| `body` | string | нет | Тело запроса |

### project.yaml пример

```yaml
adapters:
  - name: http-adapter
    image: archcraft/http-adapter:latest
    port: 8083
    technology: http
    connects-to: my-service
```

## Key Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| HTTP клиент | `IHttpClientFactory` | Встроен в ASP.NET, управляет пулом соединений |
| Target URL | Из CLI через env var | Аналогично Pg/Redis, пользователь работает только с `project.yaml` |
| Маппинг результата | 2xx=success, 404=not_found, иначе=error | Соответствует семантике протокола адаптера |
| Тело ответа | Не включается в `data` | Минимальная реализация, достаточно для симуляции |

## Open Questions

*(нет)*

## Dependencies

- Spec: `specs/active/2026-03-26_adapter-protocol.md` — базовый протокол адаптера

---

*Approved by: user Date: 2026-03-28*
